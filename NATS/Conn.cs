using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;

namespace NATS
{
    /// <summary>
    /// State of the connection.
    /// </summary>
    public enum ConnState
    {
        DISCONNECTED = 0,
        CONNECTED,
        CLOSED,
        RECONNECTING,
        CONNECTING
    }

    internal class ServerInfo
    {
        internal string Id;
        internal string Host;
        internal int Port;
        internal string Version;
        internal bool AuthRequired;
        internal bool SslRequired;
        internal Int64 MaxPayload;

        Dictionary<string, string> parameters = new Dictionary<string, string>();

        // A quick and dirty way to convert the server info string.
        // .NET 4.5/4.6 natively supports JSON, but 4.0 does not, and we
        // don't want to require users to to download a seperate json.NET
        // tool for a minimal amount of parsing.
        internal ServerInfo(string jsonString)
        {
            // TODO.  .NET json?
            string[] kv_pairs = jsonString.Split(',');
            foreach (string s in kv_pairs)
                addKVPair(s);

            //parameters = kv_pairs.ToDictionary<string, string>(v => v.Split(',')[0], v=>v.Split(',')[1]);

            Id = parameters["server_id"];
            Host = parameters["host"];
            Port = Convert.ToInt32(parameters["port"]);
            Version = parameters["version"];

            AuthRequired = "true".Equals(parameters["auth_required"]);
            SslRequired = "true".Equals(parameters["ssl_required"]);
            MaxPayload = Convert.ToInt64(parameters["max_payload"]);
        }

        private void addKVPair(string kv_pair)
        {
            string key;
            string value;

            kv_pair.Trim();
            string[] parts = kv_pair.Split(':');
            if (parts[0].StartsWith("{"))
                key = parts[0].Substring(1);
            else
                key = parts[0];

            if (parts[1].EndsWith("}"))
                value = parts[1].Substring(0, parts[1].Length - 1);
            else
                value = parts[1];

            key.Trim();
            value.Trim();

            // trim off the quotes.
            int len = key.Length - 1;
            key = key.Substring(1, key.Length - 2);

            // bools and numbers may not have quotes.
            if (value.StartsWith("\""))
            {
                value = value.Substring(1, value.Length - 2);
            }

            parameters.Add(key, value);
        }

    }

    /// <summary>
    /// Represents the connection to the server.
    /// </summary>
    public class Connection : IDisposable
    {
        Statistics stats = new Statistics();

        // NOTE: We aren't using Mutex here to support enterprises using
        // .NET 4.0.
        readonly object mu = new Object(); 


        private Random r = null;

        Options opts = new Options();

        // returns the options used to create this connection.
        public Options Opts
        {
            get { return opts; }
        }

        List<Task> wg = new List<Task>(2);


        private Uri             url     = null;
        private LinkedList<Srv> srvPool = new LinkedList<Srv>();
        private BufferedStream  bw      = null;
        private MemoryStream    pending = null;

        // flush channel
        private Channel<bool>   fch      = new Channel<bool>();

        // ping channel
        private Channel<bool>  pingChannel = new Channel<bool>();

        private ServerInfo     info = null;
        private Int64          ssid = 0;

        private ConcurrentDictionary<Int64, Subscription> subs = 
            new ConcurrentDictionary<Int64, Subscription>();

        private Channel<Msg>         mch   = new Channel<Msg>(Defaults.flushChanSize);
        private Queue<Channel<bool>> pongs = new Queue<Channel<bool>>();

        internal NATS.MsgArg msgArgs = new MsgArg();

        ConnState status = ConnState.CLOSED;

        // TODO - look at error handling, hold on to the last Ex where
        // the go client holds on to err.
        Exception lastEx;

        Parser              ps = null;
        System.Timers.Timer ptmr = null;

        int                 pout = 0;
        int                 reconnects = 0;

        // Prepare static protocol messages to minimize encoding costs.
        private byte[] pingProtoBytes = null;
        private int    pingProtoBytesLen;
        private byte[] pongProtoBytes = null;
        private int    pongProtoBytesLen;
        private byte[] _CRLF_AS_BYTES = Encoding.UTF8.GetBytes(IC._CRLF_);

        // Use a string builder to generate basic protocol messages.
        StringBuilder publishSb = new StringBuilder(Defaults.scratchSize);

        TCPConnection conn = new TCPConnection();

        /// <summary>
        /// Convenience class representing the TCP connection to prevent 
        /// managing two variables throughout the NATs client code.
        /// </summary>
        private class TCPConnection
        {
            /// A note on the use of streams.  .NET provides a BufferedStream
            /// that can sit on top of an IO stream, in this case the network
            /// stream. It increases performance by providing an additional
            /// buffer.
            /// 
            /// So, here's what we have:
            ///     Client code
            ///          ->BufferedStream (bw)
            ///              ->NetworkStream (srvStream)
            ///                  ->TCPClient (srvClient);
            /// 
            /// TODO:  Test various scenarios for efficiency.  Is a 
            /// BufferedReader directly over a network stream really 
            /// more efficient for NATS?
            /// 
            Object        mu     = new Object();
            TcpClient     client = null;
            NetworkStream stream = null;

            internal void open(Srv s)
            {
                lock (mu)
                {
                    client = new TcpClient(s.url.Host, s.url.Port);

                    client.NoDelay = false;  // TODO:  See how this works w/ flusher.

                    client.ReceiveBufferSize = Defaults.defaultBufSize;
                    client.SendBufferSize    = Defaults.defaultBufSize;

                    stream = client.GetStream();
                }
            }

            internal int SendTimeout
            {
                set
                {
                    client.SendTimeout = value;
                }
            }

            internal bool isSetup()
            {
                return (client != null);
            }

            internal void teardown()
            {
                lock (mu)
                {
                    TcpClient c = client;
                    NetworkStream s = stream;

                    client = null;
                    stream = null;

                    try
                    {
                        s.Dispose();
                        c.Close();
                    }
                    catch (Exception)
                    {
                        // ignore
                    }
                }
            }

            internal BufferedStream getBufferedStream(int size)
            {
                return new BufferedStream(stream, size);
            }

            internal bool Connected
            {
                get
                {
                    if (client == null)
                        return false;

                    return client.Connected;
                }
            }

            internal bool DataAvailable
            {
                get
                {
                    if (stream == null)
                        return false;

                    return stream.DataAvailable;
                }
            }
        }

        private class ConnectInfo
        {
            bool verbose;
            bool pedantic;
            string user;
            string pass;
            bool ssl;
            string name;
            string lang = Defaults.LangString;
            string version = Defaults.Version;

            internal ConnectInfo(bool verbose, bool pedantic, string user, string pass,
                bool secure, string name)
            {
                this.verbose  = verbose;
                this.pedantic = pedantic;
                this.user     = user;
                this.pass     = pass;
                this.ssl      = secure;
                this.name     = name;
            }

            /// <summary>
            /// .NET 4 does not natively support JSON parsing.  When moving to 
            /// support only .NET 4.5 and above use the JSON support provided
            /// by Microsoft. (System.json)
            /// </summary>
            /// <returns>JSON string repesentation of the current object.</returns>
            internal string ToJson()
            {
                StringBuilder sb = new StringBuilder();

                sb.Append("{");

                sb.AppendFormat("\"verbose\":{0},\"pedantic\":{1},",
                    verbose ? "true" : "false",
                    pedantic ? "true" : "false");
                if (user != null)
                {
                    sb.AppendFormat("\"user\":\"{0}\",", user);
                    if (pass != null)
                        sb.AppendFormat("\"pass\":\"{0}\",", pass);
                }

                sb.AppendFormat(
                    "\"ssl_required\":{0},\"name\":\"{1}\",\"lang\":\"{2}\",\"version\":\"{3}\"",
                    ssl ? "true" : "false", name, lang, version);

                sb.Append("}");

                return sb.ToString();
            }
        }

        // Ensure we cannot instanciate a connection this way.
        private Connection() { }

        internal Connection(Options opts)
        {
            this.opts = opts;
            this.pongs = createPongs();
            this.ps = new Parser(this);
            this.pingProtoBytes = System.Text.Encoding.UTF8.GetBytes(IC.pingProto);
            this.pingProtoBytesLen = pingProtoBytes.Length;
            this.pongProtoBytes = System.Text.Encoding.UTF8.GetBytes(IC.pongProto);
            this.pongProtoBytesLen = pongProtoBytes.Length;
        }

        /// <summary>
        /// Connect will attempt to connect to the NATS server.
        /// The url can contain username/password semantics.
        /// </summary>
        /// <param name="url">The url</param>
        /// <returns>A new connection to the NATS server</returns>
        public static Connection Connect(string url)
        {
            Options opts = new Options();
            opts.Url = url;
            return Connect(opts);
        }

        /// <summary>
        /// Connect will attempt to connect to the NATS server.
        /// The url can contain username/password semantics.
        /// </summary>
        /// <param name="url">The url</param>
        /// <returns>A new connection to the NATS server</returns>
        public static Options GetDefaultOptions()
        {
            return new Options();
        }

        /// <summary>
        /// SecureConnect will attempt to connect to the NATS server using TLS.
        //. The url can contain username/password semantics.
        /// </summary>
        /// <param name="url">connect url</param>
        /// <returns>A new connection to the NATS server</returns>
        public static NATS.Connection SecureConnect(string url)
        {
            Options opts = new Options();
            opts.Url = url;
            opts.Secure = true;
            return Connect(opts);
        }

        /// <summary>
        /// Connect to the NATs server using default options.
        /// </summary>
        /// <returns>A new connection to the NATS server</returns>
        public static NATS.Connection Connect()
        {
            return Connect(new Options());
        }

        /// <summary>
        /// Connect to the NATs server using the provided options.
        /// </summary>
        /// <param name="opts">NATs client options</param>
        /// <returns>A new connection to the NATS server</returns>
        public static NATS.Connection Connect(Options opts)
        {
            NATS.Connection nc = new Connection(opts);
            nc.setupServerPool();
            nc.connect();
            return nc;
        }

        /// <summary>
        /// Return bool indicating if we have more servers to try to establish
        /// a connection.
        /// </summary>
        /// <returns>True if more servers are available, false otherwise.</returns>
        private bool IsServerAvailable()
        {
            return (srvPool.Count > 0);
        }

        // Return the currently selected server
        private NATS.Srv currentServer
        {
            get
            {
                if (!IsServerAvailable())
                    return null;

                foreach (Srv s in srvPool)
                {
                    if (s.url.OriginalString.Equals(this.url.OriginalString))
                        return s;
                }

                return null;
            }
        }

        // Pop the current server and put onto the end of the list. Select head of list as long
        // as number of reconnect attempts under MaxReconnect.
        private Srv selectNextServer()
        {
            Srv s = this.currentServer;
            if (s == null)
                throw new NATSNoServersException("No servers are configured.");

            if (s.reconnects >= Opts.MaxReconnect)
                return null;

            if (srvPool.Count == 1)
                return s;

            srvPool.Remove(s);
            srvPool.AddLast(s);

            Srv first = srvPool.First();
            this.url = first.url;

            return first;
        }

        // Will assign the correct server to the Conn.Url
        private void pickServer()
        {
            this.url = null;

            if (!IsServerAvailable())
                throw new NATSNoServersException("Unable to choose server; no servers available.");

            this.url = srvPool.First().url;
        }

        private List<string> randomizeList(string[] serverArray)
        {
            List<string> randList = new List<string>();
            List<string> origList = new List<string>(serverArray);

            Random r = new Random();

            while (origList.Count > 0)
            {
                int index = r.Next(0, origList.Count);
                randList.Add(origList[index]);
                origList.RemoveAt(index);
            }

            return randList;
        }

        // Create the server pool using the options given.
        // We will place a Url option first, followed by any
        // Server Options. We will randomize the server pool unlesss
        // the NoRandomize flag is set.
        private void setupServerPool()
        {
            List<string> servers;

            if (!string.IsNullOrWhiteSpace(Opts.Url))
                srvPool.AddLast(new Srv(Opts.Url));

            if (Opts.Servers != null)
            {
                if (Opts.NoRandomize)
                    servers = new List<string>(Opts.Servers);
                else
                    servers = randomizeList(Opts.Servers);

                foreach (string s in servers)
                    srvPool.AddLast(new Srv(s));
            }

            // Place default URL if pool is empty.
            if (srvPool.Count == 0)
                srvPool.AddLast(new Srv(Defaults.Url));

            pickServer();
        }

        // createConn will connect to the server and wrap the appropriate
        // bufio structures. It will do the right thing when an existing
        // connection is in place.
        private void createConn()
        {
            currentServer.updateLastAttempt();

            conn.open(currentServer);

            // TODO:  Does this work if the underlying tcp stream is dead?
            if (pending != null && bw != null)
            {
                bw.CopyTo(pending);
            }

            bw = conn.getBufferedStream(Defaults.defaultBufSize);
        }

        // makeSecureConn will wrap an existing Conn using TLS
        private void makeTLSConn()
        {

            // TODO:  Notes... SSL for beta?  Encapsulate/overide writes to work with SSL and
            // standard streams.  Buffered writer with an SSL stream?
        }

        // waitForExits will wait for all socket watcher Go routines to
        // be shutdown before proceeding.
        private void waitForExits()
        {
            // Kick old flusher forcefully.
            fch.add(true);

            if (wg.Count > 0)
                Task.WaitAll(this.wg.ToArray());
        }

        private void spinUpSocketWatchers()
        {
            Task t = null;

            waitForExits();

            t = new Task(() => { readLoop(); });
            t.Start();
            wg.Add(t);

            t = new Task(() => { flusher(); });
            t.Start();
            wg.Add(t);

            lock (mu)
            {
                this.pout = 0;

                if (Opts.PingInterval > 0)
                {
                    if (ptmr == null)
                    {
                        ptmr = new System.Timers.Timer(Opts.PingInterval);
                        ptmr.Elapsed += pingTimerEventHandler;
                        ptmr.AutoReset = true;
                        ptmr.Enabled = true;
                        ptmr.Start();
                    }
                    else
                    {
                        ptmr.Stop();
                        ptmr.Interval = Opts.PingInterval;
                        ptmr.Start();
                    }
                }

            }
        }

        private void pingTimerEventHandler(Object sender, EventArgs args)
        {
            lock (mu)
            {
                if (status != ConnState.CONNECTED)
                {
                    return;
                }

                pout++;

                if (pout > Opts.MaxPingsOut)
                {
                    processOpError(new NATSStaleConnectionException());
                    return;
                }

                sendPing(null);
            }
        }

        /// <summary>
        /// Returns the url of the server currently connected, null otherwise.
        /// </summary>
        public string ConnectedURL
        {
            get
            {
                lock (mu)
                {
                    if (status != ConnState.CONNECTED)
                        return null;

                    return url.OriginalString;
                }
            }
        }

        /// <summary>
        /// Returns the id of the server currently connected.
        /// </summary>
        public string ConnectedId
        {
            get
            {
                lock (mu)
                {
                    if (status != ConnState.CONNECTED)
                        return IC._EMPTY_;

                    return this.info.Id;
                }
            }
        }

        private Queue<Channel<bool>> createPongs()
        {
            Queue<Channel<bool>> rv = new Queue<Channel<bool>>(8);
            return rv;
        }

        // Process a connected connection and initialize properly.
        // The lock should not be held entering this function.
        private void processConnectInit()
        {
            lock (mu)
            {
                this.status = ConnState.CONNECTING;

                processExpectedInfo();
            }

            // TODO: better inline?
            Task.Run(() => { spinUpSocketWatchers(); });

            sendConnect();
        }

        private void connect()
        {
            // Create actual socket connection
            // For first connect we walk all servers in the pool and try
            // to connect immediately.
            bool connected = false;
            foreach (Srv s in srvPool)
            {
                this.url = s.url;
                try
                {
                    lock (mu)
                    {
                        createConn();

                        s.didConnect = true;
                        s.reconnects = 0;
                    }

                    processConnectInit();

                    connected = true;
                }
                catch (Exception e)
                {
                    if (lastEx != null)
                        lastEx = new NATSConnectionException("Unable to connect.", e);

                    close(ConnState.DISCONNECTED, false);
                    lock (mu)
                    {
                        this.url = null;
                    }
                }

                if (connected)
                    break;

            } // for

            lock (mu)
            {
                if (this.status != ConnState.CONNECTED)
                {
                    if (this.lastEx == null)
                        this.lastEx = new NATSNoServersException("Unable to connect to a server.");

                    throw this.lastEx;
                }
            }
        }

        // This will check to see if the connection should be
        // secure. This can be dictated from either end and should
        // only be called after the INIT protocol has been received.
        private void checkForSecure()
        {
            // Check to see if we need to engage TLS
            // Check for mismatch in setups
            if (Opts.Secure && info.SslRequired)
            {
                throw new NATSSecureConnWantedException();
            }
            else if (info.SslRequired && !Opts.Secure)
            {
                throw new NATSSecureConnRequiredException();
            }

            // Need to rewrap with bufio
            if (Opts.Secure)
            {
                makeTLSConn();
            }
        }

        // TODO:  Move this:
        internal class Control
        {
            // for efficiency, assign these once in the contructor;
            internal string op;
            internal string args;

            static readonly internal char[] seperator = { ' ' };

            // ensure this object is always created with a string.
            private Control() { }

            internal Control(string s)
            {
                string[] parts = s.Split(seperator, 2);

                if (parts.Length == 1)
                {
                    op = parts[0].Trim();
                    args = IC._EMPTY_;
                }
                if (parts.Length == 2)
                {
                    op = parts[0].Trim();
                    args = parts[1].Trim();
                }
                else
                {
                    op = IC._EMPTY_;
                    args = IC._EMPTY_;
                }
            }
        }

        // processExpectedInfo will look for the expected first INFO message
        // sent when a connection is established. The lock should be held entering.
        private void processExpectedInfo()
        {
            Control c;

            try
            {
                conn.SendTimeout = 2;
                c = readOp();
            }
            catch (Exception e)
            {
                // UNLOCK????
                processOpError(e);
                // Throw?
                return;
            }
            finally
            {
                conn.SendTimeout = 0;
            }

            if (!IC._INFO_OP_.Equals(c.op))
            {
                throw new NATSConnectionException("nats: Protocol exception, INFO not received");
            }

            processInfo(c.args);
            checkForSecure();
        }

        private void writeString(string format, object a, object b)
        {
            writeString(String.Format(format, a, b));
        }

        private void writeString(string format, object a, object b, object c)
        {
            writeString(String.Format(format, a, b, c));
        }

        private void writeString(string value)
        {
            byte[] sendBytes = System.Text.Encoding.UTF8.GetBytes(value);
            bw.Write(sendBytes, 0, sendBytes.Length);
        }

        // Sends a protocol control message by queueing into the bufio writer
        // and kicking the flush method.  These writes are protected.
        private void sendProto(string value)
        {
            lock (mu)
            {
                writeString(value);
                kickFlusher();
            }
        }

        private void sendProto(byte[] value, int length)
        {
            lock (mu)
            {
                bw.Write(value, 0, length);
                kickFlusher();
            }
        }

        // Generate a connect protocol message, issuing user/password if
        // applicable. The lock is assumed to be held upon entering.
        private string connectProto()
        {
            String u = url.UserInfo;
            String user = null;
            String pass = null;

            if (!string.IsNullOrEmpty(u))
            {
                if (u.Contains(":"))
                {
                    string[] userpass = u.Split(':');
                    if (userpass.Length > 0)
                    {
                        user = userpass[0];
                    }
                    if (userpass.Length > 1)
                    {
                        pass = userpass[1];
                    }
                }
                else
                {
                    user = u;
                }
            }

            ConnectInfo info = new ConnectInfo(opts.Verbose, opts.Pedantic, user,
                pass, opts.Secure, opts.Name);

            StringBuilder sb = new StringBuilder();

            sb.AppendFormat(IC.conProto, info.ToJson());
            return sb.ToString();
        }


        private void sendConnect()
        {
            string cProto = null;
 
            lock (mu)
            {
                cProto = connectProto();
            }

            sendProto(cProto);

            try
            {
                FlushTimeout(opts.Timeout);
            }
            catch (Exception ex)
            {
                if (lastEx == null)
                    throw new NATSException("Error sending connect protocol message", ex);
            }

            lock (mu)
            {
                if (isClosed())
                {
                    if (lastEx != null)
                        throw lastEx;
                }

                // This is where we are truly connected.
                status = ConnState.CONNECTED;
            }
        }

        private Control readOp()
        {
            // This is only used when creating a connection, so simplify
            // life and just create a stream reader to read the incoming
            // info string.  If this becomes part of the fastpath, read
            // the string directly using the buffered reader.
            //
            // Do not close or dispose the stream reader - we need the underlying
            // BufferedStream.
            StreamReader sr = new StreamReader(bw);
            return new Control(sr.ReadLine());
        }

        private void processDisconnect()
        {
            status = ConnState.DISCONNECTED;
            if (lastEx == null)
                return;

            if (info.SslRequired)
                lastEx = new NATSSecureConnRequiredException();
            else
                lastEx = new NATSConnectionClosedException();
        }

        // This will process a disconnect when reconnect is allowed.
        // The lock should not be held on entering this function.
        private void processReconnect()
        {
            lock (mu)
            {
                if (!isClosed())
                {
                    // If we are already in the proper state, just return.
                    if (isReconnecting())
                        return;

                    status = ConnState.RECONNECTING;
                    if (ptmr != null)
                        ptmr.Stop();

                    if (conn.isSetup())
                    {
                        bw.Flush();
                        conn.teardown();
                    }

                    // TODO:  need to hold onto task?
                    Task.Run(() => { doReconnect(); });
                }
            }
        }

        // flushReconnectPending will push the pending items that were
        // gathered while we were in a RECONNECTING state to the socket.
        private void flushReconnectPendingItems()
        {
            if (pending == null)
                return;

            if (pending.Length > 0)
                pending.CopyTo(bw);

            pending = null;
        }

        // Try to reconnect using the option parameters.
        // This function assumes we are allowed to reconnect.
        private void doReconnect()
        {
            // We want to make sure we have the other watchers shutdown properly
            // here before we proceed past this point
            waitForExits();

            // FIXME(dlc) - We have an issue here if we have
            // outstanding flush points (pongs) and they were not
            // sent out, but are still in the pipe.

            // Hold the lock manually and release where needed below.
            Monitor.Enter(mu);

            pending = new MemoryStream();
            bw = new BufferedStream(pending);

            // Clear any errors.
            lastEx = null;

            if (Opts.DisconnectedEventHandler != null)
            {
                Monitor.Exit(mu);
                Opts.DisconnectedEventHandler(this, new ConnEventArgs(this));
                Monitor.Enter(mu);
            }

            Srv s;
            while ((s = selectNextServer()) != null)
            {
                // Sleep appropriate amount of time before the
                // connection attempt if connecting to same server
                // we just got disconnected from.
                double elapsedMillis = s.TimeSinceLastAttempt.TotalMilliseconds;

                if (elapsedMillis < Opts.ReconnectWait)
                {
                    double sleepTime = Opts.ReconnectWait - elapsedMillis;

                    Monitor.Exit(mu);
                    Thread.Sleep((int)sleepTime);
                    Monitor.Enter(mu);
                }

                if (isClosed())
                    break;

                try
                {
                    createConn();
                }
                catch (Exception)
                {
                    continue;  // ignore
                }

                // We are reconneced
                reconnects++;

                // Clear out server stats for the server we connected to..
                s.didConnect = true;
                s.reconnects = 0;

                // Set our status to connecting.
                status = ConnState.CONNECTING;

                // Process Connect logic
                try
                {
                    // send our connect info as normal
                    processExpectedInfo();
                    writeString(connectProto());

                    // Send existing subscription state
                    resendSubscriptions();

                    // Now send off and clear pending buffer
                    flushReconnectPendingItems();

                    status = ConnState.CONNECTED;

                    Task.Run(() => { spinUpSocketWatchers(); });
                }
                catch (Exception)
                {
                    status = ConnState.RECONNECTING;
                    continue;
                }

                // get the event handler under the lock
                ConnEventHandler eh = Opts.ReconnectedEventHandler;

                // Release the lock here, we will return below
                Monitor.Exit(mu);

                // flush everything
                Flush();

                if (eh != null)
                    eh(this, new ConnEventArgs(this));

                return;

            }

            if (lastEx == null)
                lastEx = new NATSNoServersException("Unable to reconnect");

            Monitor.Exit(mu);
            Close();
        }

        private void processOpError(Exception e)
        {
            bool allowReconnect = false;

            lock (mu)
            {
                if (isClosed() || isReconnecting())
                    return;

                allowReconnect = (Opts.AllowReconnect && status == ConnState.CONNECTED);
            }

            if (allowReconnect)
            {
                processReconnect();
            }
            else
            {
                lock (mu)
                {
                    processDisconnect();
                    lastEx = e;
                }
                Close();
            }
        }

        private void readLoop()
        {
            // Stack based buffer.
            byte[] buffer = new byte[Defaults.defaultBufSize];
            Parser parser = new Parser(this);
            int    len;
            bool   sb;

            while (true)
            {
                sb = false;
                lock (mu)
                {
                    sb = (isClosed() || isReconnecting());
                    if (sb)
                        this.ps = parser;
                }

                try
                {
                    len = bw.Read(buffer, 0, Defaults.defaultBufSize);
                    parser.parse(buffer, len);
                }
                catch (Exception e)
                {
                    if (State != ConnState.CLOSED)
                    {
                        processOpError(e);
                    }
                    break;
                }
            }

            lock (mu)
            {
                parser = null;
            }
        }

        // deliverMsgs waits on the delivery channel shared with readLoop and processMsg.
        // It is used to deliver messages to asynchronous subscribers.
        internal void deliverMsgs(Channel<Msg> ch)
        {
            Msg m;

            while (true)
            {
                lock (mu)
                {
                    if (isClosed())
                        return;
                }

                // TODO:  performance - batch messages?  get all and loop
                try
                {
                    m = ch.get();
                }
                catch (InvalidOperationException)
                {
                    // the channel has been closed, exit silently.
                    break;
                }

                // Note, this seems odd message having the sub process itself, 
                // but this is good for performance.
                if (!m.sub.processMsg(m))
                {
                    lock (mu)
                    {
                        removeSub(m.sub);
                    }
                }
            }
        }

        internal void processMsgArgs(MemoryStream argBuffer)
        {

            String s = System.Text.Encoding.UTF8.GetString(
                argBuffer.ToArray(), 0, (int)argBuffer.Position);

            string[] args = s.Split(' ');

            switch (args.Length)
            {
                case 3:
                    msgArgs.subject = args[0];
                    msgArgs.sid     = Convert.ToInt64(args[1]);
                    msgArgs.reply   = null;
                    msgArgs.size    = Convert.ToInt32(args[2]);
                    break;
                case 4:
                    msgArgs.subject = args[0];
                    msgArgs.sid     = Convert.ToInt64(args[1]);
                    msgArgs.reply   = args[2];
                    msgArgs.size    = Convert.ToInt32(args[3]);
                    break;
                default:
                    throw new NATSException("Unable to parse message arguments: " + s);
            }

            if (msgArgs.size <= 0)
            {
                throw new NATSException("Invalid Message - Bad or Missing Size: '%s': " + s);
            }
        }


        // processMsg is called by parse and will place the msg on the
        // appropriate channel for processing. All subscribers have their
        // their own channel. If the channel is full, the connection is
        // considered a slow subscriber.
        internal void processMsg(MemoryStream msgStream)
        {
            bool maxReached = false;
            Subscription s;

            byte[] msg       = msgStream.ToArray();
            long   byteCount = msgStream.Position;

            lock (mu)
            {
                stats.inMsgs++;
                stats.inBytes += byteCount;

                // In regular message processing, the key should be present,
                // so optimize by using an an exception to handle a missing key.
                // (as opposed to checking with Contains or TryGetValue)
                try
                {
                    s = subs[msgArgs.sid];
                }
                catch (Exception)
                {
                    // this can happen when a subscriber is unsubscribing.
                    return;
                }

                lock (s.mu)
                {
                    maxReached = s.tallyMessage(byteCount);
                    if (maxReached == false)
                    {
                        byte[] msgCopy = new byte[byteCount];
                        Array.Copy(msg, msgCopy, byteCount);

                        if (!s.addMessage(new Msg(msgArgs, s, msgCopy), opts.subChanLen))
                        {
                            processSlowConsumer(s);
                        }

                    } // maxreached == false

                } // lock s.mu

            } // lock conn.mu

            if (maxReached)
                removeSub(s);
        }

        // processSlowConsumer will set SlowConsumer state and fire the
        // async error handler if registered.
        void processSlowConsumer(Subscription s)
        {
            // nc.err = ErrSlowConsumer;
            if (opts.AsyncErrorEventHandler != null && !s.sc)
            {
                Task.Run(() =>
                {
                    opts.AsyncErrorEventHandler(this,
                        new ErrEventArgs(this, s, "Slow Consumer"));
                });
            }

            s.sc = true;
        }

        // flusher is a separate task that will process flush requests for the write
        // buffer. This allows coalescing of writes to the underlying socket.
        private void flusher()
        {
            // TODO:  If there are problems here, then 
            // look at the Go code.  First .NET attempt
            // is to be much simpler.
            if (conn.Connected == false)
            {
                return;
            }

            while (!fch.isComplete())
            {
                bool val = fch.get();
                if (val == false)
                    return;

                lock (mu)
                {
                    if (!isConnected())
                        return;

                    if (bw.CanWrite)
                        bw.Flush();
                }
            }
        }

        // processPing will send an immediate pong protocol response to the
        // server. The server uses this mechanism to detect dead clients.
        internal void processPing()
        {
            sendProto(pongProtoBytes, pongProtoBytesLen);
        }


        // processPong is used to process responses to the client's ping
        // messages. We use pings for the flush mechanism as well.
        internal void processPong()
        {
            Channel<bool> ch = null;
            lock (mu)
            {
                if (pongs.Count > 0)
                    ch = pongs.Dequeue();

                pout = 0;

                if (ch != null)
                {
                    ch.add(true);
                }
            }
        }

        // processOK is a placeholder for processing OK messages.
        internal void processOK()
        {
            // NOOP;
            return;
        }

        // processInfo is used to parse the info messages sent
        // from the server.
        internal void processInfo(string info)
        {
            if (info == null || IC._EMPTY_.Equals(info))
            {
                return;
            }

            this.info = new ServerInfo(info);
        }

        // LastError reports the last error encountered via the Connection.
        public Exception LastError
        {
            get
            {
                // TODO:  return exception?  No equivalent?
                return this.lastEx;
            }
        }

        // processErr processes any error messages from the server and
        // sets the connection's lastError.
        internal void processErr(MemoryStream errorStream)
        {
            bool invokeDelegates = false;
            Exception ex = null;

            String s = System.Text.Encoding.UTF8.GetString(
                errorStream.ToArray(), 0, (int)errorStream.Position);

            if (IC.STALE_CONNECTION.Equals(s))
            {
                processOpError(new NATSStaleConnectionException());
            }
            else
            {
                ex = new NATSException(s);
                lock (mu)
                {
                    lastEx = ex;

                    if (status != ConnState.CONNECTING)
                    {
                        invokeDelegates = true;
                    }
                }

                close(ConnState.CLOSED, invokeDelegates);
            }
        }

        // kickFlusher will send a bool on a channel to kick the
        // flush method to flush data to the server.
        private void kickFlusher()
        {
            if (bw != null)
                fch.add(true);
        }


        // Include for using directive.
        public void Dispose()
        {
            Close();
        }

        // publish is the internal function to publish messages to a nats-server.
        // Sends a protocol data message by queueing into the bufio writer
        // and kicking the flush go routine. These writes should be protected.
        private void publish(string subject, string reply, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("Invalid subject.");

            int msgSize = data != null ? data.Length : 0;

            lock (mu)
            {
                // Proactively reject payloads over the threshold set by server.
                if (msgSize > info.MaxPayload)
                    throw new NATSMaxPayloadException();

                if (isClosed())
                    throw new NATSConnectionClosedException();

                // TODO:  should this be here?  Need a global/fatal error
                if (lastEx != null)
                    throw lastEx;


                // TODO:  Initial code.  Performance test this.  .NET is very performant at
                // concatenation of 3 or 4 small strings, but more than 
                // that, string builder is better.  Combine both
                // for performance.  Stringbuilder makes a copy, but we're 
                // stuck with that unless 
                publishSb.Clear();

                publishSb.Append(IC._PUB_P_ + " " + subject + " ");

                if (string.IsNullOrWhiteSpace(reply) == false)
                    publishSb.Append(reply + " ");

                publishSb.Append(msgSize);
                publishSb.Append(IC._CRLF_);

                writeString(publishSb.ToString());

                if (msgSize > 0)
                {
                    bw.Write(data, 0, msgSize);
                    bw.Write(_CRLF_AS_BYTES, 0, _CRLF_AS_BYTES.Length);
                }

                stats.outMsgs++;
                stats.outBytes += msgSize;

                kickFlusher();
            }

        } // publish

        /// <summary>
        /// Publish publishes the data argument to the given subject. The data
        /// argument is left untouched and needs to be correctly interpreted on
        /// the receiver.
        /// </summary>
        /// <param name="subject">Subject to publish the message to.</param>
        /// <param name="data">Message payload</param>
        public void Publish(string subject, byte[] data)
        {
            publish(subject, null, data);
        }

        /// <summary>
        /// Publishes the Msg structure, which includes the
        /// Subject, an optional Reply and an optional Data field.
        /// </summary>
        /// <param name="msg">The message to send.</param>
        public void Publish(Msg msg)
        {
            publish(msg.Subject, msg.Reply, msg.Data);
        }

        /// <summary>
        /// Publish will perform a Publish() excpecting a response on the
        /// reply subject. Use Request() for automatically waiting for a response
        /// inline.
        /// </summary>
        /// <param name="subject">Subject to publish on</param>
        /// <param name="reply">Subject the receiver will on.</param>
        /// <param name="data">The message payload</param>
        public void Publish(string subject, string reply, byte[] data)
        {
            publish(subject, reply, data);
        }

        /// <summary>
        /// Request will create an Inbox and perform a Request() call
        /// with the Inbox reply and return the first reply received.
        /// This is optimized for the case of multiple responses.
        /// </summary>
        /// <remarks>
        /// A negative timeout blocks forever, zero is not allowed.
        /// </remarks>
        /// <param name="subject">Subject to send the request on.</param>
        /// <param name="data">payload of the message</param>
        /// <param name="timeout">time to block</param>
        public Msg Request(string subject, byte[] data, int timeout)
        {
            Msg m = null;

            // a timeout of 0 will never succeed - don't allow it.
            if (timeout == 0)
            {
                throw new ArgumentException("Timeout cannot be 0.");
            }

            string inbox = NewInbox();

            SyncSubscription s = subscribeSync(inbox, null);
            s.AutoUnsubscribe(1);

            publish(subject, inbox, data);
            m = s.NextMessage(timeout);
            try
            {
                // the auto unsubscribe should handle this.
                s.Unsubscribe();
            }
            catch (Exception) {  /* NOOP */ }

            return m;
        }

        /// <summary>
        /// Request will create an Inbox and perform a Request() call
        /// with the Inbox reply and return the first reply received.
        /// This is optimized for the case of multiple responses.
        /// </summary>
        /// <param name="subject">Subject to send the request on.</param>
        /// <param name="data">payload of the message</param>
        /// <param name="timeout">timeout.</param>
        public Msg Request(string subject, byte[] data)
        {
            return Request(subject, data, -1);
        }



        /// <summary>
        /// NewInbox will return an inbox string which can be used for directed replies from
        /// subscribers. These are guaranteed to be unique, but can be shared and subscribed
        /// to by others.
        /// </summary>
        /// <returns>A string representing an inbox.</returns>
        public string NewInbox()
        {
            if (r == null)
                r = new Random();

            byte[] buf = new byte[13];

            r.NextBytes(buf);

            return IC.inboxPrefix + BitConverter.ToString(buf).Replace("-","");
        }

        internal void sendSubscriptonMessage(AsyncSubscription s)
        {
            lock (mu)
            {
                // We will send these for all subs when we reconnect
                // so that we can suppress here.
                if (!isReconnecting())
                {
                    writeString(IC.subProto, s.Subject, s.Queue, s.sid);
                }
            }

            kickFlusher();
        }

        private void addSubscription(Subscription s)
        {
            s.sid = Interlocked.Increment(ref ssid);
            subs[s.sid] = s;
        }

        private AsyncSubscription subscribeAsync(string subject, string queue)
        {
            AsyncSubscription s = null;

            lock (mu)
            {
                if (isClosed())
                    throw new NATSConnectionClosedException();

                s = new AsyncSubscription(this, subject, queue);

                addSubscription(s);
            }

            return s;
        }

        // subscribe is the internal subscribe function that indicates interest in a subject.
        // TODO = do not start listening until a handler has been set.
        private SyncSubscription subscribeSync(string subject, string queue)
        {
            SyncSubscription s = null;

            lock (mu)
            {
                if (isClosed())
                    throw new NATSConnectionClosedException();

                s = new SyncSubscription(this, subject, queue);

                addSubscription(s);

                // We will send these for all subs when we reconnect
                // so that we can suppress here.
                if (!isReconnecting())
                {
                    writeString(IC.subProto, subject, queue, s.sid);
                }
            }

            kickFlusher();

            return s;
        }

        /// <summary>
        /// Subscribe will create a subscriber with interest in a given subject.
        /// The subject can have wildcards (partial:*, full:>). Messages will be delivered
        /// to the associated MsgHandler. If no MsgHandler is set, the
        /// subscription is a synchronous subscription and can be polled via
        /// Subscription.NextMsg().  Subscriber message handler delegates
        /// can be added or removed anytime.
        /// </summary>
        /// <param name="subject">Subject of interest.</param>
        /// <returns>A new Subscription</returns>
        public SyncSubscription SubscribeSync(string subject)
        {
            return subscribeSync(subject, null);
        }

        /// <summary>
        /// SubscribeAsynchronously will create an AsynchSubscriber with
        /// interest in a given subject.
        /// </summary>
        /// <param name="subject">Subject of interest.</param>
        /// <param name="handler">Message handler delegate function</param>
        /// <returns>A new Subscription</returns>
        public AsyncSubscription SubscribeAsync(string subject)
        {
            return subscribeAsync(subject, null);
        }

        /// <summary>
        /// Creates a synchronous queue subscriber on the given
        /// subject. All subscribers with the same queue name will form the queue
        /// group and only one member of the group will be selected to receive any
        /// given message synchronously.
        /// </summary>
        /// <param name="subject">Subject of interest</param>
        /// <param name="queue">Name of the queue group</param>
        /// <returns>A new Subscription</returns>
        public SyncSubscription QueueSubscribeSync(string subject, string queue)
        {
            return subscribeSync(subject, queue);
        }

        /// <summary>
        /// QueueSubscribe creates an asynchronous queue subscriber on the given subject.
        /// All subscribers with the same queue name will form the queue group and
        /// only one member of the group will be selected to receive any given
        /// message asynchronously.
        /// </summary>
        /// <param name="subject">Subject of interest</param>
        /// <param name="queue">Name of the queue group</param>
        /// <param name="handler">Message handler delegate function</param>
        /// <returns>A new Subscription</returns>
        public AsyncSubscription QueueSubscribeAsync(string subject, string queue)
        {
            return subscribeAsync(subject, queue);
        }

        // unsubscribe performs the low level unsubscribe to the server.
        // Use Subscription.Unsubscribe()
        internal void unsubscribe(Subscription sub, int max)
        {
            lock (mu)
            {
                if (isClosed())
                    throw new NATSConnectionClosedException();

                Subscription s = subs[sub.sid];
                if (s == null)
                {
                    // already unsubscribed
                    return;
                }

                if (max > 0)
                {
                    s.max = max;
                }
                else
                {
                    removeSub(s);
                }

                if (!isReconnecting())
                    writeString(IC.unsubProto, s.sid, max);

            }

            kickFlusher();
        }

        internal void removeSub(Subscription s)
        {
            Subscription o;

            subs.TryRemove(s.sid, out o);
            if (s.mch != null)
            {
                s.mch.close();
                s.mch = null;
            }

            s.conn = null;
        }

        // FIXME: This is a hack
        // removeFlushEntry is needed when we need to discard queued up responses
        // for our pings as part of a flush call. This happens when we have a flush
        // call outstanding and we call close.
        private bool removeFlushEntry(Channel<bool> chan)
        {
            if (pongs == null)
                return false;

            if (pongs.Count == 0)
                return false;
            
            Channel<bool> start = pongs.Dequeue();
            Channel<bool> c = start;

            // todo - use a different data structure.
            while (true)
            {
                if (c == chan)
                {
                    return true;
                }
                else
                {
                    pongs.Enqueue(c);
                }

                c = pongs.Dequeue();

                if (c == start)
                    break;
            }

            return false;
        }

        // The caller must lock this method.
        private void sendPing(Channel<bool> ch)
        {
            if (ch != null)
                pongs.Enqueue(ch);

            bw.Write(pingProtoBytes, 0, pingProtoBytesLen);
            bw.Flush();
        }

        private void processPingTimer()
        {
            lock (mu)
            {
                if (status != ConnState.CONNECTED)
                    return;

                // Check for violation
                this.pout++;
                if (this.pout <= Opts.MaxPingsOut)
                {
                    sendPing(null);

                    // reset the timer
                    ptmr.Stop();
                    ptmr.Start();

                    return;
                }
            }

            // if we get here, we've encountered an error.  Process
            // this outside of the lock.
            processOpError(new NATSStaleConnectionException());
        }

        /// <summary>
        /// Flush will perform a round trip to the server and return when it
        /// receives the internal reply.
        /// </summary>
        /// <param name="timeout">The timeout in milliseconds.</param>
        public void FlushTimeout(int timeout)
        {
            if (timeout <= 0)
                throw new ArgumentException("nats:  Bad timeout value");

            lock (mu)
            {
                if (isClosed())
                    throw new NATSConnectionClosedException();

                sendPing(pingChannel);
            }

            try
            {
                bool rv = pingChannel.get(timeout);
                if (!rv)
                {
                    lastEx = new NATSConnectionClosedException();
                }
            }
            catch (NATSTimeoutException te)
            {
                lastEx = te;
            }
            catch (Exception e)
            {
                lastEx = new NATSException("Flush channel error.", e);
            }

            if (lastEx != null)
            {
                removeFlushEntry(pingChannel);
                throw lastEx;
            }
        }

        /// <summary>
        /// Flush will perform a round trip to the server and return when it
        /// receives the internal reply.
        /// </summary>
        public void Flush()
        {
            // 60 second default.
            FlushTimeout((int)Opts.Timeout);
        }

        // resendSubscriptions will send our subscription state back to the
        // server. Used in reconnects
        private void resendSubscriptions()
        {
            foreach (Subscription s in subs.Values)
            {
                writeString(IC.subProto, s.Subject, s.Queue, s.sid);
            }

            // TODO:  no flush?
        }


        // Clear pending flush calls and reset
        private void resetPendingFlush()
        {
            lock (mu)
            {
                clearPendingFlushCalls();
                this.pongs = createPongs();
            }
        }

        // This will clear any pending flush calls and release pending calls.
        private void clearPendingFlushCalls()
        {
            lock (mu)
            {
                // Clear any queued pongs, e.g. pending flush calls.
                foreach (Channel<bool> ch in pongs)
                {
                    if (ch != null)
                        ch.add(true);
                }

                pongs = null;
            }
        }


        // Low level close call that will do correct cleanup and set
        // desired status. Also controls whether user defined callbacks
        // will be triggered. The lock should not be held entering this
        // function. This function will handle the locking manually.
        private void close(ConnState closeState, bool invokeDelegates)
        {
            ConnEventHandler disconnectedEventHandler = null;
            ConnEventHandler closedEventHandler = null;

            lock (mu)
            {
                if (isClosed())
                {
                    status = closeState;
                    return;
                }

                status = ConnState.CLOSED;
            }

            // Kick the routines so they fall out.
            // fch will be closed on finalizer
            kickFlusher();

            // Clear any queued pongs, e.g. pending flush calls.
            clearPendingFlushCalls();

            lock (mu)
            {
                if (ptmr != null)
                    ptmr.Stop();

                // Close sync subscriber channels and release any
                // pending NextMsg() calls.
                foreach (Subscription s in subs.Values)
                {
                    s.closeChannel();
                }

                subs = null;

                // perform appropriate callback is needed for a
                // disconnect;
                if (invokeDelegates && conn.isSetup() &&
                    Opts.DisconnectedEventHandler != null)
                {
                    // TODO:  Mirror go, but this can result in a callback
                    // being invoked out of order.  Why a go routine here?
                    disconnectedEventHandler = Opts.DisconnectedEventHandler;
                    Task.Run(() => { disconnectedEventHandler(this, new ConnEventArgs(this)); });
                }

                // Go ahead and make sure we have flushed the outbound buffer.
                status = ConnState.CLOSED;
                if (conn.isSetup() == false)
                {
                    bw.Flush();
                    conn.teardown();
                }

                closedEventHandler = opts.ClosedEventHandler;
            }

            if (invokeDelegates && closedEventHandler != null)
            {
                closedEventHandler(this, new ConnEventArgs(this));
            }

            lock (mu)
            {
                status = closeState;
            }
        }

        /// <summary>
        /// Close will close the connection to the server. This call will release
        /// all blocking calls, such as Flush() and NextMsg().
        /// </summary>
        public void Close()
        {
            close(ConnState.CLOSED, true);
        }

        // assume the lock is head.
        private bool isClosed()
        {
            return (status == ConnState.CLOSED);
        }

        /// <summary>
        /// Test if this connection has been closed.
        /// </summary>
        /// <returns>true if closed, false otherwise.</returns>
        public bool IsClosed()
        {
            lock (mu)
            {
                return isClosed();
            }
        }

        /// <summary>
        /// Test if this connection is reconnecting.
        /// </summary>
        /// <returns>true if reconnecting, false otherwise.</returns>
        public bool IsReconnecting()
        {
            lock (mu)
            {
                return isReconnecting();
            }
        }

        /// <summary>
        /// Gets the current state of the connection.
        /// </summary>
        public ConnState State
        {
            get
            {
                lock (mu)
                {
                    return status;
                }
            }
        }

        private bool isReconnecting()
        {
            lock (mu)
            {
                return (status == ConnState.RECONNECTING);
            }
        }

        // Test if Conn is connected or connecting.
        private bool isConnected()
        {
            return (status == ConnState.CONNECTING || status == ConnState.CONNECTED);
        }

        // Stats will return a race safe copy of connection statistics.Statistics section for the connection.
        /// <summary>
        /// Returns a race safe copy of connection statistics.
        /// </summary>
        public Statistics Stats
        {
            get
            {
                lock (mu)
                {
                    return new Statistics(this.stats);
                }
            }
        }

        /// <summary>
        /// Returns the server defined size limit that a message payload can have.
        /// </summary>
        public long MaxPayload
        {
            get
            {
                lock (mu)
                {
                    return info.MaxPayload;
                }
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.AppendFormat("url={0};", url);
            sb.AppendFormat("info={0};", info);
            sb.AppendFormat("status={0}", status);
            sb.Append("Subscriptions={");
            foreach (Subscription s in subs.Values)
            {
                sb.Append("Subscription {" + s.ToString() + "}");
            }
            sb.Append("}}");

            return sb.ToString();
        }

    } // class Conn


}
