using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


///
/// This is the base NATS .NET client.  Enjoy!
/// 
/// To summarize, this Apcera supported client library follows
/// the go client closely.  While public and protected methods 
/// and properties adhere to the .NET coding guidlines, 
/// internal/private members and methods mirror the go client for
/// maintenance purposes.  Public method and properties are
/// documented with standard .NET doc.
/// 
///     - Public/Protected members and methods are in PascalCase
///     - Public/Protected members and methods are documented in 
///       standard .NET documentation.
///     - Private/Internal members and methods are in camelCase.
///     - There are no callbacks - delegates only.
///     - Public members are accessed through a property.
///     - Internal members are accessed directly.
///     - Internal Variable Names mirror those of the go client.
///     - A minimal/no reliance on third party packages.
///
///     Coding guidelines are based on:
///     http://blogs.msdn.com/b/brada/archive/2005/01/26/361363.aspx
///     although method location mirrors the go client to faciliate
///     maintenance.
///     
namespace NATS.Client
{
    /// <summary>
    /// This class contains default values for fields used throughout NATS.
    /// </summary>
    public static class Defaults
    {
        /// <summary>
        /// Client version
        /// </summary>
        public const string Version = "0.0.1";

        /// <summary>
        /// The default NATS connect url.
        /// </summary>
        public const string Url     = "nats://localhost:4222";

        /// <summary>
        /// The default NATS connect port.
        /// </summary>
        public const int    Port    = 4222;

        public const int    MaxReconnect = 60;

        public const int    ReconnectWait  = 2000; // 2 seconds.
        public const int    Timeout        = 2000; // 2 seconds.
        public const int    PingInterval   = 120000;// 2 minutes.

        public const int    MaxPingOut     = 2;
        public const int    MaxChanLen     = System.Int32.MaxValue; //65536;
        public const int    RequestChanLen = 4;
        public const string LangString     = ".NET";

        /*
         * Namespace level defaults
         */

	    // Scratch storage for assembling protocol headers
	    internal const int scratchSize = 512;

	    // The size of the bufio reader/writer on top of the socket.
        internal const int defaultBufSize = 32768;

	    // The size of the bufio while we are reconnecting
        internal const int defaultPendingSize = 1024 * 1024;

	    // The buffered size of the flush "kick" channel
        internal const int flushChanSize = 1024;

	    // Default server pool size
        internal const int srvPoolSize = 4;
    }

    public class ConnEventArgs
    {
        private Connection c;   
            
        internal ConnEventArgs(Connection c)
        {
            this.c = c;
        }

        public Connection Conn
        {
            get { return c; }
        }
    }

    public class ErrEventArgs
    {
        private Connection c;
        private Subscription s;
        private String err;

        internal ErrEventArgs(Connection c, Subscription s, String err)
        {
            this.c = c;
            this.s = s;
            this.err = err;
        }

        public Connection Conn
        {
            get { return c; }
        }

        public Subscription Subscription
        {
            get { return s; }
        }

        public string Error
        {
            get { return err; }
        }

    }

    public delegate void ConnEventHandler(object sender, ConnEventArgs e);
    public delegate void ErrorEventHandler(object sender, ErrEventArgs e);

    /**
     * Internal Constants
     */
    internal class IC
    {
        internal const string _CRLF_  = "\r\n";
        internal const string _EMPTY_ = "";
        internal const string _SPC_   = " ";
        internal const string _PUB_P_ = "PUB ";

        internal const string _OK_OP_   = "+OK";
        internal const string _ERR_OP_  = "-ERR";
        internal const string _MSG_OP_  = "MSG";
        internal const string _PING_OP_ = "PING";
        internal const string _PONG_OP_ = "PONG";
        internal const string _INFO_OP_ = "INFO";

        internal const string inboxPrefix = "_INBOX.";

        internal const string conProto   = "CONNECT {0}" + IC._CRLF_;
        internal const string pingProto  = "PING" + IC._CRLF_;
        internal const string pongProto  = "PONG" + IC._CRLF_;
        internal const string pubProto   = "PUB {0} {1} {2}" + IC._CRLF_;
        internal const string subProto   = "SUB {0} {1} {2}" + IC._CRLF_;
        internal const string unsubProto = "UNSUB {0} {1}" + IC._CRLF_;

        internal const string STALE_CONNECTION = "Stale Connection";
    }

    /// <summary>
    /// This class is passed into the MsgHandler delegate, providing the
    /// message received.
    /// </summary>
    public class MsgHandlerEventArgs
    {
        internal Msg msg = null;

        /// <summary>
        /// Retrieves the message.
        /// </summary>
        public Msg Message
        {
            get { return msg; }
        }
    }
   
    /// <summary>
    /// MsgHandler is a delegate that processes messages delivered to
    /// asynchronous subscribers.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public delegate void MsgHandler(object sender, MsgHandlerEventArgs args);


    public class ChannelTest
    {
        public static void run()
        {
            Channel<int> c = new Channel<int>();

            System.Console.WriteLine("Adding 123.");
            c.add(123);
            int n = c.get();
            System.Console.WriteLine("Got: " + n);  


            
        }
    }

}
