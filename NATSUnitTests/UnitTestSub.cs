﻿using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NATS.Client;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace NATSUnitTests
{
    /// <summary>
    /// Run these tests with the gnatsd auth.conf configuration file.
    /// </summary>
    [TestClass]
    public class TestSubscriptions
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
        public void TestServerAutoUnsub()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                long received = 0;
                int max = 10;

                using (IAsyncSubscription s = c.SubscribeAsync("foo"))
                {
                    s.MessageHandler += (sender, arg) =>
                    {
                        received++;
                    };

                    s.AutoUnsubscribe(max);
                    s.Start();

                    for (int i = 0; i < max*2; i++)
                    {
                        c.Publish("foo", null);
                    }
                    c.Flush();

                    Thread.Sleep(1000);

                    Assert.IsTrue(received == max);
                    Assert.IsFalse(s.IsValid);
                }
            }
        }

        [TestMethod]
        public void TestClientAutoUnsub()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                long received = 0;
                int max = 10;

                using (ISyncSubscription s = c.SubscribeSync("foo"))
                {
                    s.AutoUnsubscribe(max);

                    for (int i = 0; i < max * 2; i++)
                    {
                        c.Publish("foo", null);
                    }
                    c.Flush();

                    Thread.Sleep(100);

                    try
                    {
                        while (true)
                        {
                            s.NextMessage(0);
                            received++;
                        }
                    }
                    catch (NATSBadSubscriptionException) { /* ignore */ }

                    Assert.IsTrue(received == max);
                    Assert.IsFalse(s.IsValid);
                }
            }
        }

        [TestMethod]
        public void TestCloseSubRelease()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (ISyncSubscription s = c.SubscribeSync("foo"))
                {
                    Stopwatch sw = new Stopwatch();
                    sw.Start();
                    try
                    {
                        new Task(() => { Thread.Sleep(100); c.Close(); }).Start();
                         s.NextMessage(10000);
                    }
                    catch (Exception) { /* ignore */ }

                    sw.Stop();

                    Assert.IsTrue(sw.ElapsedMilliseconds < 10000);
                }
            }
        }

        [TestMethod]
        public void TestValidSubscriber()
        {
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (ISyncSubscription s = c.SubscribeSync("foo"))
                {
                    Assert.IsTrue(s.IsValid);

                    try { s.NextMessage(100); }
                    catch (NATSTimeoutException) { }

                    Assert.IsTrue(s.IsValid);

                    s.Unsubscribe();

                    Assert.IsFalse(s.IsValid);

                    try { s.NextMessage(100); }
                    catch (NATSBadSubscriptionException) { }
                }
            }
        }

        [TestMethod]
        public void TestSlowSubscriber()
        {
            Options opts = ConnectionFactory.GetDefaultOptions();
            opts.SubChannelLength = 100;

            using (IConnection c = new ConnectionFactory().Connect(opts))
            {
                using (ISyncSubscription s = c.SubscribeSync("foo"))
                {
                    for (int i =0; i < (opts.SubChannelLength+100); i++)
                    {
                        c.Publish("foo", null);
                    }

                    c.Flush();

                    try 
                    {
                        s.NextMessage();
                    }
                    catch (NATSSlowConsumerException)
                    {
                        return;
                    }
                    Assert.Fail("Did not receive an exception.");
                }
            } 
        }

        [TestMethod]
        public void TestSlowAsyncSubscriber()
        {
            Options opts = ConnectionFactory.GetDefaultOptions();
            opts.SubChannelLength = 100;

            using (IConnection c = new ConnectionFactory().Connect(opts))
            {
                using (IAsyncSubscription s = c.SubscribeAsync("foo"))
                {
                    Object mu = new Object();

                    s.MessageHandler += (sender, args) =>
                    {
                        lock (mu)
                        {
                            Console.WriteLine("Subscriber Waiting....");
                            Assert.IsTrue(Monitor.Wait(mu, 20000));
                            Console.WriteLine("Subscriber done.");
                        }
                    };

                    s.Start();

                    for (int i = 0; i < (opts.SubChannelLength + 100); i++)
                    {
                        c.Publish("foo", null);
                    }

                    int flushTimeout = 1000;
                    Stopwatch sw = new Stopwatch();
                    sw.Start();

                    bool flushFailed = false;
                    try
                    {
                        c.Flush(5000);
                    }
                    catch (Exception)
                    {
                        flushFailed = true;
                    }

                    sw.Stop();

                    lock (mu)
                    {
                        Monitor.Pulse(mu);
                    }

                    Assert.IsFalse(sw.ElapsedMilliseconds < flushTimeout);
                    
                    Assert.IsTrue(flushFailed);
                }
            }
        }

        [TestMethod]
        public void TestAsyncErrHandler()
        {
            Object subLock = new Object();
            object testLock = new Object();
            IAsyncSubscription s ;


            Options opts = ConnectionFactory.GetDefaultOptions();
            opts.SubChannelLength = 10;

            bool handledError = false;

            using (IConnection c = new ConnectionFactory().Connect(opts))
            {
                using (s = c.SubscribeAsync("foo"))
                {
                    opts.AsyncErrorEventHandler = (sender, args) =>
                    {
                        if (handledError)
                            return;

                        handledError = true;

                        Assert.IsTrue(args.Subscription == s);

                        System.Console.WriteLine("Expected Error: " + args.Error);
                        Assert.IsTrue(args.Error.Contains("Slow"));

                        // release the subscriber
                        lock (subLock) { Monitor.Pulse(subLock); }

                        // release the test
                        lock (testLock) { Monitor.Pulse(testLock); }
                    };

                    s.MessageHandler += (sender, args) =>
                    {
                        lock (subLock)
                        {
                            Console.WriteLine("Subscriber Waiting....");
                            Assert.IsTrue(Monitor.Wait(subLock, 30000));
                            Console.WriteLine("Subscriber done.");
                        }
                    };

                    s.Start();

                    lock(testLock)
                    {

                        for (int i = 0; i < (opts.SubChannelLength + 100); i++)
                        {
                            c.Publish("foo", null);
                        }
                        c.Flush();

                        Assert.IsTrue(Monitor.Wait(testLock, 10000));
                    }
                }
            }
        }

        [TestMethod]
        public void TestAsyncSubscriberStarvation()
        {
            Object waitCond = new Object();

            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (IAsyncSubscription helper = c.SubscribeAsync("helper"),
                                          start = c.SubscribeAsync("start"))
                {
                    helper.MessageHandler += (sender, arg) =>
                    {
                        System.Console.WriteLine("Helper");
                        c.Publish(arg.Message.Reply,
                            Encoding.UTF8.GetBytes("Hello"));
                    };
                    helper.Start();

                    start.MessageHandler += (sender, arg) =>
                    {
                        System.Console.WriteLine("Responsder");
		                string responseIB = c.NewInbox();
                        IAsyncSubscription ia = c.SubscribeAsync(responseIB);

                        ia.MessageHandler += (iSender, iArgs) =>
                        {
                            System.Console.WriteLine("Internal subscriber.");
                            lock (waitCond) { Monitor.Pulse(waitCond); }
                        };
                        ia.Start();
 
		                c.Publish("helper", responseIB,
                            Encoding.UTF8.GetBytes("Help me!"));
                    };

                    start.Start();
                     
                    c.Publish("start", Encoding.UTF8.GetBytes("Begin"));
                    c.Flush();

                    lock (waitCond) 
                    { 
                        Assert.IsTrue(Monitor.Wait(waitCond, 2000));
                    }
                }
            }
        }


        [TestMethod]
        public void TestAsyncSubscribersOnClose()
        {
            /// basically tests if the subscriber sub channel gets
            /// cleared on a close.
            Object waitCond = new Object();
            int callbacks = 0;

            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (IAsyncSubscription s = c.SubscribeAsync("foo"))
                {
                    s.MessageHandler += (sender, args) =>
                    {
                        callbacks++;
                        lock (waitCond)
                        {
                            Monitor.Wait(waitCond);
                        }
                    };

                    s.Start();

                    for (int i = 0; i < 10; i++)
                    {
                        c.Publish("foo", null);
                    }
                    c.Flush();

                    Thread.Sleep(500);
                    c.Close();

                    lock (waitCond)
                    {
                        Monitor.Pulse(waitCond);
                    }

                    Thread.Sleep(500);

                    Assert.IsTrue(callbacks == 1);
                }
            }
        }
    } // class

} // namespace
