using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NATS.Client;

namespace NATSUnitTests
{
    /// <summary>
    /// Run these tests with the gnatsd auth.conf configuration file.
    /// </summary>
    [TestClass]
    public class TestBasic
    {
        UnitTestUtilities utils = new UnitTestUtilities();

        [TestInitialize()]
        public void Initialize()
        {
            utils.StartDefaultServer();
        }

        [TestCleanup()]
        public void Cleanup()
        {
            utils.StopDefaultServer();
        }

        [TestMethod]
        public void TestConnectedServer()
        {
            IConnection c = new ConnectionFactory().Connect();
           
            string u = c.ConnectedUrl;
            
            if (string.IsNullOrWhiteSpace(u))
                Assert.Fail("Invalid connected url {0}.", u);
                
            if (!Defaults.Url.Equals(u))
                Assert.Fail("Invalid connected url {0}.", u);

            c.Close();
            u = c.ConnectedUrl;

            if (u != null)
                Assert.Fail("Url is not null after connection is closed.");
        }

        [TestMethod]
        public void TestMultipleClose()
        {
            IConnection c = new ConnectionFactory().Connect();
            
            Task[] tasks = new Task[10];

            for (int i = 0; i < 10; i++)
            {

                tasks[i] = new Task(() => { c.Close(); });
                tasks[i].Start();
            }

            Task.WaitAll(tasks);
        }

        [TestMethod]
        public void TestBadOptionTimeoutConnect()
        {
            Options opts = ConnectionFactory.GetDefaultOptions();

            try
            {
                opts.Timeout = -1;
                Assert.Fail("Able to set invalid timeout.");
            }
            catch (Exception)
            {}   
        }

        [TestMethod]
        public void TestSimplePublish()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                c.Publish("foo", Encoding.UTF8.GetBytes("Hello World!"));
            }
        }

        [TestMethod]
        public void TestSimplePublishNoData()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                c.Publish("foo", null);
            }
        }

        private bool compare(byte[] p1, byte[] p2)
        {
            // null case
            if (p1 == p2)
                return true;

            if (p1.Length != p2.Length)
                return false;

            for (int i = 0; i < p2.Length; i++)
            {
                if (p1[i] != p2[i])
                    return false;
            }

            return true;
        }

        private bool compare(byte[] payload, Msg m)
        {
            return compare(payload, m.Data);
        }

        private bool compare(Msg a, Msg b)
        {
            if (a.Subject.Equals(b.Subject) == false)
                return false;

            if (a.Reply != null && a.Reply.Equals(b.Reply))
            {
                return false;
            }

            return compare(a.Data, b.Data);
        }

        readonly byte[] omsg = Encoding.UTF8.GetBytes("Hello World");
        readonly object mu = new Object();
        IAsyncSubscription asyncSub = null;
        Boolean received = false;

        [TestMethod]
        public void TestAsyncSubscribe()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (IAsyncSubscription s = c.SubscribeAsync("foo"))
                {
                    asyncSub = s;
                    s.MessageHandler += CheckRecveivedAndValidHandler;
                    s.Start();

                    lock (mu)
                    {
                        received = false;
                        c.Publish("foo", omsg);
                        Monitor.Wait(mu, 10000);
                    }

                    if (!received)
                        Assert.Fail("Did not receive message.");
                }
            }
        }

        private void CheckRecveivedAndValidHandler(object sender, MsgHandlerEventArgs args)
        {
            System.Console.WriteLine("Received msg.");

            if (compare(args.Message.Data, omsg) == false)
                Assert.Fail("Messages are not equal.");

            if (args.Message.ArrivalSubcription != asyncSub)
                Assert.Fail("Subscriptions do not match.");

            lock (mu)
            {
                received = true;
                Monitor.Pulse(mu);
            }
        }

        [TestMethod]
        public void TestSyncSubscribe()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (ISyncSubscription s = c.SubscribeSync("foo"))
                {
                    c.Publish("foo", omsg);
                    Msg m = s.NextMessage(1000);
                    if (compare(omsg, m) == false)
                        Assert.Fail("Messages are not equal.");
                }
            }
        }

        [TestMethod]
        public void TestPubWithReply()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (ISyncSubscription s = c.SubscribeSync("foo"))
                {
                    c.Publish("foo", "reply", omsg);
                    Msg m = s.NextMessage(1000);
                    if (compare(omsg, m) == false)
                        Assert.Fail("Messages are not equal.");
                }
            }
        }

        [TestMethod]
        public void TestFlush()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (ISyncSubscription s = c.SubscribeSync("foo"))
                {
                    c.Publish("foo", "reply", omsg);
                    c.Flush();
                }
            }
        }

        [TestMethod]
        public void TestQueueSubscriber()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (ISyncSubscription s1 = c.QueueSubscribeSync("foo", "bar"),
                                         s2 = c.QueueSubscribeSync("foo", "bar"))
                {
                    c.Publish("foo", omsg);
                    c.Flush();
                    if (s1.QueuedMessageCount + s2.QueuedMessageCount != 1)
                        Assert.Fail("Invalid message count in queue.");

                    // Drain the messages.
                    try { s1.NextMessage(100); }
                    catch (NATSTimeoutException) { }

                    try { s2.NextMessage(100); }
                    catch (NATSTimeoutException) { }

                    int total = 1000;

                    for (int i = 0; i < 1000; i++)
                    {
                        c.Publish("foo", omsg);
                    }
                    c.Flush();
                    
                    int r1 = s1.QueuedMessageCount;
                    int r2 = s2.QueuedMessageCount;

                    if ((r1 + r2) != total)
                    {
                        Assert.Fail("Incorrect number of messages: {0} vs {1}",
                            (r1 + r2), total);
                    }

                    if (Math.Abs(r1 - r2) > (total * .15))
                    {
                        Assert.Fail("Too much variance between {0} and {1}",
                            r1, r2);
                    }
                }
            }
        }

        [TestMethod]
        public void TestReplyArg()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (IAsyncSubscription s = c.SubscribeAsync("foo"))
                {
                    s.MessageHandler += ExpectedReplyHandler;
                    s.Start();

                    lock(mu)
                    {
                        received = false;
                        c.Publish("foo", "bar", null);
                        Monitor.Wait(mu, 5000);
                    }
                }
            }

            if (!received)
                Assert.Fail("Message not received.");
        }

        private void ExpectedReplyHandler(object sender, MsgHandlerEventArgs args)
        {
            if ("bar".Equals(args.Message.Reply) == false)
                Assert.Fail("Expected \"bar\", received: " + args.Message);

            lock(mu)
            {
                received = true;
                Monitor.Pulse(mu);
            }
        }

        [TestMethod]
        public void TestSyncReplyArg()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (ISyncSubscription s = c.SubscribeSync("foo"))
                {
                    c.Publish("foo", "bar", null);
                    c.Flush(30000);

                    Msg m = s.NextMessage(1000);
                    if ("bar".Equals(m.Reply) == false)
                        Assert.Fail("Expected \"bar\", received: " + m);
                }
            }
        }

        [TestMethod]
        public void TestUnsubscribe()
        {
            int count = 0;
            int max = 20;

            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (IAsyncSubscription s = c.SubscribeAsync("foo"))
                {
                    asyncSub = s;
                    //s.MessageHandler += UnsubscribeAfterCount;
                    s.MessageHandler += (sender, args) =>
                    {
                        if (++count == max)
                        {
                            asyncSub.Unsubscribe();
                            lock (mu)
                            {
                                Monitor.Pulse(mu);
                            }
                        }
                    };
                    s.Start();

                    max = 20;
                    for (int i = 0; i < max; i++)
                    {
                        c.Publish("foo", null, null);
                    }

                    lock (mu)
                    {
                        Monitor.Wait(mu, 2000);
                    }
                }

                if (count != max)
                    Assert.Fail("Received wrong # of messages after unsubscribe: {0} vs {1}", count, max);
            }
        }

        [TestMethod]
        public void TestDoubleUnsubscribe()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (ISyncSubscription s = c.SubscribeSync("foo"))
                {
                    s.Unsubscribe();

                    try
                    {
                        s.Unsubscribe();
                        Assert.Fail("No Exception thrown.");
                    }
                    catch (Exception e)
                    {
                        System.Console.WriteLine("Expected exception {0}: {1}",
                            e.GetType(), e.Message);
                    }
                }
            }
        }

        [TestMethod]
        public void TestRequestTimeout()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                try
                {
                    c.Request("foo", null, 500);
                    Assert.Fail("Expected an exception.");
                }
                catch (NATSTimeoutException) 
                {
                    Console.WriteLine("Received expected exception.");
                }
            }
        }

        [TestMethod]
        public void TestRequest()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (IAsyncSubscription s = c.SubscribeAsync("foo"))
                {
                    byte[] response = Encoding.UTF8.GetBytes("I will help you.");

                    s.MessageHandler += (sender, args) =>
                    {
                        c.Publish(args.Message.Reply, response);
                    };

                    s.Start();

                    Msg m = c.Request("foo", Encoding.UTF8.GetBytes("help."),
                        5000);

                    if (!compare(m.Data, response))
                    {
                        Assert.Fail("Response isn't valid");
                    }
                }
            }
        }

        [TestMethod]
        public void TestRequestNoBody()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (IAsyncSubscription s = c.SubscribeAsync("foo"))
                {
                    byte[] response = Encoding.UTF8.GetBytes("I will help you.");

                    s.MessageHandler += (sender, args) =>
                    {
                        c.Publish(args.Message.Reply, response);
                    };

                    s.Start();

                    Msg m = c.Request("foo", null, 5000);

                    if (!compare(m.Data, response))
                    {
                        Assert.Fail("Response isn't valid");
                    }
                }
            }
        }

        [TestMethod]
        public void TestFlushInHandler()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (IAsyncSubscription s = c.SubscribeAsync("foo"))
                {
                    byte[] response = Encoding.UTF8.GetBytes("I will help you.");

                    s.MessageHandler += (sender, args) =>
                    {
                        try
                        {
                            c.Flush();
                            System.Console.WriteLine("Success.");
                        }
                        catch (Exception e)
                        {
                            Assert.Fail("Unexpected exception: " + e);
                        }

                        lock (mu)
                        {
                            Monitor.Pulse(mu);
                        }
                    };

                    s.Start();

                    lock (mu)
                    {
                        c.Publish("foo", Encoding.UTF8.GetBytes("Hello"));
                        Monitor.Wait(mu);
                    }
                }
            }
        }

        [TestMethod]
        public void TestReleaseFlush()
        {
            IConnection c = new ConnectionFactory().Connect();

            for (int i = 0; i < 1000; i++)
            {
                c.Publish("foo", Encoding.UTF8.GetBytes("Hello"));
            }

            new Task(() => { c.Close(); }).Start();
            c.Flush();
        }

        [TestMethod]
        public void TestCloseAndDispose()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                c.Close();
            }
        }

        [TestMethod]
        public void TestInbox()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                string inbox = c.NewInbox();
                Assert.IsFalse(string.IsNullOrWhiteSpace(inbox));
                Assert.IsTrue(inbox.StartsWith("_INBOX."));
            }
        }

        [TestMethod]
        public void TestStats()
        {
            IConnection c = new ConnectionFactory().Connect();

            byte[] data = Encoding.UTF8.GetBytes("The quick brown fox jumped over the lazy dog");
            int iter = 10;

            for (int i = 0; i < iter; i++)
            {
                c.Publish("foo", data);
            }

            IStatistics stats = c.Stats;
            Assert.AreEqual(iter, stats.OutMsgs);
            Assert.AreEqual(iter*data.Length, stats.OutBytes);
            
            c.ResetStats();

            // Test both sync and async versions of subscribe.
            IAsyncSubscription s1 = c.SubscribeAsync("foo");
            s1.MessageHandler += (sender, arg) => { };
            s1.Start();

            ISyncSubscription s2 = c.SubscribeSync("foo");

            for (int i = 0; i < iter; i++)
            {
                c.Publish("foo", data);
            }
            c.Flush();

            stats = c.Stats;
            Assert.AreEqual(2*iter, stats.InMsgs);
            Assert.AreEqual(2*iter*data.Length, stats.InBytes);

            c.Close();
        }

        [TestMethod]
        public void TestRaceSafeStats()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {

                new Task(() => { c.Publish("foo", null); }).Start();

                Thread.Sleep(1000);

                Assert.AreEqual(1, c.Stats.OutMsgs);
            }
        }

        [TestMethod]
        public void TestBadSubject()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                new Task(() => { c.Publish("foo", null); }).Start();
                Thread.Sleep(200);

                Assert.AreEqual(1, c.Stats.OutMsgs);
            }
        }

    } // class

} // namespace
