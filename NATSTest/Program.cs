using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using NATS;

namespace SampleNatsApplication
{
    public class SimpleAsyncSubscriber
    {
        public SimpleAsyncSubscriber()
        {
            using (Connection c = Connection.Connect())
            {
                using (AsyncSubscription s = c.SubscribeAsync("bar"))
                {
                    s.MessageHandler += CountNatsMessage; 
                    s.MessageHandler += PrintNatsMessage;
                    s.Start();

                    Thread.Sleep(100);

                    Task.Run(() => {
                        for (int i = 0; i < 10; i++)
                        {
                            c.Publish("bar", Encoding.UTF8.GetBytes("payload"));
                            c.Flush();
                        }
                    });

                    Thread.Sleep(10000);
                }
            }
        }

        void PrintNatsMessage(object sender, MsgHandlerEventArgs args)
        {
            System.Console.WriteLine(
                "Received message on Subject {0}, data={1}",
                args.Message.Subject,
                new String(Encoding.UTF8.GetChars(args.Message.Data)));
        }

        int count = 0;
        void CountNatsMessage(object sender, MsgHandlerEventArgs args)
        {
            System.Console.WriteLine("Received message {0}.", ++count);
        }
    }


    class NatsTest
    {
        NatsTest()
        {}

        internal void runSimpleSyncSub()
        {
            Options opts = new Options();
            //opts.Url = "nats://localhost:4222";
            System.Console.WriteLine(opts.ToString());
            Connection c = Connection.Connect(opts);
            SyncSubscription s = c.SubscribeSync(">");
            asyncPublishMessage(c);
            Msg m = s.NextMessage();
            if (m != null)
            {
                System.Console.WriteLine("Message Recieved");
                System.Console.WriteLine("   Subject: " + m.Subject);
                System.Console.WriteLine("   Reply: " + m.Reply);
                System.Console.WriteLine("   Data: " + new String(Encoding.UTF8.GetChars(m.Data)));
            }
            else
            {
                System.Console.WriteLine("Message was NULL.");
            }
            
            s.Unsubscribe();

            c.Close();
        }

        private void publishMessage(NATS.Connection conn)
        {
            conn.Publish("foo", Encoding.UTF8.GetBytes("data"));
        }

        private void asyncPublishMessage(NATS.Connection conn)
        {
            Task.Run(() => { publishMessage(conn); });
        }

        void PrintMessage(object sender, MsgHandlerEventArgs args)
        {
            System.Console.WriteLine("Received message {0}.", 
                new String(Encoding.UTF8.GetChars(args.Message.Data)));
        }

        private void sleep(int seconds)
        {
            Thread.Sleep(1000 * seconds);
        }

        private volatile bool finished;

        public void TestAddRemoveDelegates()
        {
            Options opts = new Options();
            opts.Url = "nats://localhost:4222";
            using (Connection c = Connection.Connect(opts))
            {   
                finished = false;

                Msg natsMsg = new Msg();
                natsMsg.Data = Encoding.UTF8.GetBytes("payload");
                natsMsg.Subject = "foo";

                Task task = new Task(()=>{
                    int count = 1;
                    while (!finished)
                    {
                        natsMsg.Data = Encoding.UTF8.GetBytes("payload: " + count++);
                        c.Publish(natsMsg);
                        Thread.Sleep(200);
                    }
                });

                task.Start();

                AsyncSubscription s = c.SubscribeAsync("foo");

                System.Console.WriteLine("Starting async subscriber.");
                s.Start();
                sleep(1);
                System.Console.WriteLine("Adding message handler.");
                s.MessageHandler += PrintMessage;
                sleep(1);
                System.Console.WriteLine("Removing message handler.");
                s.MessageHandler -= PrintMessage;
                sleep(1);
                System.Console.WriteLine("Adding message handler.");
                s.MessageHandler += PrintMessage;
                sleep(1);

                s.Unsubscribe();

                finished = true;
                task.Wait();
            }
        }

        private void printMessage(Msg m)
        {
            System.Console.WriteLine("Message: " + m);
        }

        public void TestRequestReply()
        {
            using (Connection c = Connection.Connect())
            {
                using (AsyncSubscription s = c.SubscribeAsync("req"))
                {
                    s.MessageHandler += EchoToReply;
                    s.Start();

                    Msg m = c.Request("req", 
                        Encoding.UTF8.GetBytes("Request Data"),
                        2000000);

                    System.Console.WriteLine("Received msg from request.");
                    printMessage(m);
                }
            }
        }

        private void EchoToReply(object sender, MsgHandlerEventArgs args)
        {
            System.Console.WriteLine("Received msg on {0}, replying to {1}",
                args.Message.Subject, args.Message.Reply);

            printMessage(args.Message);

            Subscription s = args.Message.ArrivalSubcription;

            String replyMsgData = new String(
                Encoding.UTF8.GetChars(args.Message.Data))
                + " (Reply)";

            s.Connection.Publish(args.Message.Reply, 
                Encoding.UTF8.GetBytes(replyMsgData));
            s.Connection.Flush();

            System.Console.WriteLine("Sent Reply.");
        }

        public void TestOptions()
        {
            Options opts = new Options();
            opts.AsyncErrorEventHandler = ErrorHandler;
            opts.Servers = new string[2] {"nats://localhost:4221","nats://localhost:4222"};
            opts.ReconnectedEventHandler = ReconnectedHandler;
            opts.ClosedEventHandler = ClosedHandler;
            System.Console.WriteLine(opts);

            Connection c = Connection.Connect(opts);
            System.Console.WriteLine("Connected!");
            Thread.Sleep(10000);
            c.Close();
        }

        private void ClosedHandler(object sender, ConnEventArgs e)
        {
            System.Console.WriteLine("Connection Closed.");
        }

        private void ReconnectedHandler(object sender, ConnEventArgs args)
        {
            System.Console.WriteLine("Connection Reconnected to {0}.",
                args.Conn.ConnectedURL);
            
        }

        private void DisconnectedHandler(object sender, ConnEventArgs e)
        {
            System.Console.WriteLine("Connection Disconnected.");
        }

        private void ErrorHandler(object sender, ErrEventArgs e)
        {
            System.Console.WriteLine("ErrorHandler: ");
            System.Console.WriteLine(e.Conn);
            System.Console.WriteLine(e.Error);
            System.Console.WriteLine(e.Subscription);
        }

        private void TestReconnect()
        {
            Options o = new Options();
            o.Servers = new string[2] { "nats://localhost:4222", "nats://localhost:4223" };
            o.ReconnectedEventHandler  = ReconnectedHandler;
            o.AsyncErrorEventHandler   = ErrorHandler;
            o.DisconnectedEventHandler = DisconnectedHandler;

            using (Connection c = Connection.Connect(o))
            {
                Console.WriteLine("Kill and restart the server....");
                Thread.Sleep(600000);
                Console.WriteLine("Exiting.");
            }
        }

        private void TestLargeMessage()
        {
            byte[] returnData = null;

            byte[] data = new byte[10240];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)'A';
            }

            Msg m = new Msg();
            m.Subject = "subject";
            m.AssignData(data);

            using (Connection c = Connection.Connect())
            {
                using (SyncSubscription s = c.SubscribeSync("subject"))
                {
                    Task.Run(() => { c.Publish(m); });
                    returnData = s.NextMessage(1000).Data;
                }
            }

            if (data.Length != returnData.Length)
            {
                System.Console.WriteLine("Size does not match.");
                return;
            }

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != returnData[i])
                {
                    System.Console.WriteLine("Data does not match.");
                    return;
                }
            }

            System.Console.WriteLine("Large Message Test Passed.");
        }

        private void SubscribeAndSend(Connection c)
        {
            using (SyncSubscription s = c.SubscribeSync("foo"))
            {
                c.Publish("foo", Encoding.UTF8.GetBytes("payload"));
                Msg m = s.NextMessage();
                System.Console.WriteLine("Received Msg:  " + m);
            }
        }

        public void TestConnectUserInfo()
        {
            System.Console.WriteLine("Connecting");

            Options opts = new Options();
            opts.Timeout = 120000;
            opts.Url = "nats://Colin:BadPassword@localhost:4222";
            using (Connection c = Connection.Connect(opts))
            {
                System.Console.WriteLine("Connected");
                SubscribeAndSend(c);
            }
        }

        static void Main(string[] args)
        {
            try
            {
                NatsTest nt = new NatsTest();
                //nt.runSimpleSyncSub();
                //nt.TestAddRemoveDelegates();
                //nt.TestRequestReply();
                //nt.TestOptions();
                //nt.TestReconnect();
                //nt.TestLargeMessage();
                nt.TestConnectUserInfo();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("Error:  " + ex.Message);
                System.Console.WriteLine(ex);
            }
                
        }
    }

}