using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NATS;

namespace NATSUnitTests
{
    /// <summary>
    /// Run these tests with the gnatsd auth.conf configuration file.
    /// </summary>
    [TestClass]
    public class TestBasic
    {
        [TestMethod]
        public void TestConnectedServer()
        {
            Connection c = Connection.Connect();
           
            string u = c.ConnectedURL;
            
            if (string.IsNullOrWhiteSpace(u))
                Assert.Fail("Invalid connected url {0}.", u);
                
            if (!Defaults.Url.Equals(u))
                Assert.Fail("Invalid connected url {0}.", u);

            c.Close();
            u = c.ConnectedURL;

            if (u != null)
                Assert.Fail("Url is not null after connection is closed.");
        }

        [TestMethod]
        public void TestMultipleClose()
        {
            Connection c = Connection.Connect();
            
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
            Options opts = Connection.GetDefaultOptions();

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
            using (Connection c = Connection.Connect())
            {
                c.Publish("foo", Encoding.UTF8.GetBytes("Hello World!"));
            }
        }

        [TestMethod]
        public void TestSimplePublishNoData()
        {

            using (Connection c = Connection.Connect())
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
        Subscription asyncSub = null;
        Boolean received = false;

        [TestMethod]
        public void TestAsyncSubscribe()
        {
            using (Connection c = Connection.Connect())
            {
                using (AsyncSubscription s = c.SubscribeAsync("foo"))
                {
                    s.MessageHandler += MessageHandler;
                    s.Start();

                    c.Publish("foo", omsg);

                    lock (mu)
                    {
                        Monitor.Wait(mu, 1000);
                    }

                    if (!received)
                        Assert.Fail("Did not receive message.");
                }
            }
        }

        private void MessageHandler(object sender, MsgHandlerEventArgs args)
        {
            received = true;

            System.Console.WriteLine("Received msg.");

            if (compare(args.Message.Data, omsg) == false)
                Assert.Fail("Messages are not equal.");

            if (args.Message.ArrivalSubcription != asyncSub)
                Assert.Fail("Subscriptions do not match.");

            lock (mu)
            {
                Monitor.Pulse(mu);
            }
        }

        [TestMethod]
        public void TestSyncSubscribe()
        {
            using (Connection c = Connection.Connect())
            {
                using (SyncSubscription s = c.SubscribeSync("foo"))
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
            using (Connection c = Connection.Connect())
            {
                using (SyncSubscription s = c.SubscribeSync("foo"))
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
            using (Connection c = Connection.Connect())
            {
                using (SyncSubscription s = c.SubscribeSync("foo"))
                {
                    c.Publish("foo", "reply", omsg);
                    c.Flush();
                }
            }
        }

        [TestMethod]
        public void TestQueueSubscriber()
        {
            using (Connection c = Connection.Connect())
            {
                using (SyncSubscription s1 = c.QueueSubscribeSync("foo", "bar"),
                                        s2 = c.QueueSubscribeSync("foo", "bar"))
                {
                    c.Publish("foo", omsg);
                    c.Flush();
                    if (s1.QueuedMessageCount + s2.QueuedMessageCount != 1)
                        Assert.Fail("Invalid message count in queue.");

                    // Drain the messages.
                    try { s1.NextMessage(1000); }
                    catch (NATSTimeoutException) { }

                    try { s2.NextMessage(1000); }
                    catch (NATSTimeoutException) { }

                    c.Publish("foo", omsg);
                    c.Flush();

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

                    if (Math.Abs(r1 - r2) < total * .15)
                    {
                        Assert.Fail("Too much variance between {0} and {1}",
                            r1, r2);
                    }
                }
            }
        }
    }
}


#if sldkfjdslkfj

func TestQueueSubscriber(t *testing.T) {
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(t)
	defer nc.Close()

	s1, _ := nc.QueueSubscribeSync("foo", "bar")
	s2, _ := nc.QueueSubscribeSync("foo", "bar")
	omsg := []byte("Hello World")
	nc.Publish("foo", omsg)
	nc.Flush()
	r1, _ := s1.QueuedMsgs()
	r2, _ := s2.QueuedMsgs()
	if (r1 + r2) != 1 {
		t.Fatal("Received too many messages for multiple queue subscribers")
	}
	// Drain messages
	s1.NextMsg(time.Second)
	s2.NextMsg(time.Second)

	total := 1000
	for i := 0; i < total; i++ {
		nc.Publish("foo", omsg)
	}
	nc.Flush()
	v := uint(float32(total) * 0.15)
	r1, _ = s1.QueuedMsgs()
	r2, _ = s2.QueuedMsgs()
	if r1+r2 != total {
		t.Fatalf("Incorrect number of messages: %d vs %d", (r1 + r2), total)
	}
	expected := total / 2
	d1 := uint(math.Abs(float64(expected - r1)))
	d2 := uint(math.Abs(float64(expected - r2)))
	if d1 > v || d2 > v {
		t.Fatalf("Too much variance in totals: %d, %d > %d", d1, d2, v)
	}
}

func TestReplyArg(t *testing.T) {
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(t)
	defer nc.Close()

	ch := make(chan bool)
	replyExpected := "bar"

	nc.Subscribe("foo", func(m *nats.Msg) {
		if m.Reply != replyExpected {
			t.Fatalf("Did not receive correct reply arg in callback: "+
				"('%s' vs '%s')", m.Reply, replyExpected)
		}
		ch <- true
	})
	nc.PublishMsg(&nats.Msg{Subject: "foo", Reply: replyExpected, Data: []byte("Hello")})
	if e := Wait(ch); e != nil {
		t.Fatal("Did not receive callback")
	}
}

func TestSyncReplyArg(t *testing.T) {
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(t)
	defer nc.Close()

	replyExpected := "bar"
	sub, _ := nc.SubscribeSync("foo")
	nc.PublishMsg(&nats.Msg{Subject: "foo", Reply: replyExpected, Data: []byte("Hello")})
	msg, err := sub.NextMsg(1 * time.Second)
	if err != nil {
		t.Fatal("Received an err on NextMsg()")
	}
	if msg.Reply != replyExpected {
		t.Fatalf("Did not receive correct reply arg in callback: "+
			"('%s' vs '%s')", msg.Reply, replyExpected)
	}
}

func TestUnsubscribe(t *testing.T) {
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(t)
	defer nc.Close()

	received := int32(0)
	max := int32(10)
	ch := make(chan bool)
	nc.Subscribe("foo", func(m *nats.Msg) {
		atomic.AddInt32(&received, 1)
		if received == max {
			err := m.Sub.Unsubscribe()
			if err != nil {
				t.Fatal("Unsubscribe failed with err:", err)
			}
			ch <- true
		}
	})
	send := 20
	for i := 0; i < send; i++ {
		nc.Publish("foo", []byte("hello"))
	}
	nc.Flush()
	<-ch

	r := atomic.LoadInt32(&received)
	if r != max {
		t.Fatalf("Received wrong # of messages after unsubscribe: %d vs %d",
			r, max)
	}
}

func TestDoubleUnsubscribe(t *testing.T) {
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(t)
	defer nc.Close()

	sub, err := nc.SubscribeSync("foo")
	if err != nil {
		t.Fatal("Failed to subscribe: ", err)
	}
	if err = sub.Unsubscribe(); err != nil {
		t.Fatal("Unsubscribe failed with err:", err)
	}
	if err = sub.Unsubscribe(); err == nil {
		t.Fatal("Unsubscribe should have reported an error")
	}
}

func TestRequestTimeout(t *testing.T) {
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(t)
	defer nc.Close()

	if _, err := nc.Request("foo", []byte("help"), 10*time.Millisecond); err == nil {
		t.Fatalf("Expected to receive a timeout error")
	}
}

func TestRequest(t *testing.T) {
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(t)
	defer nc.Close()

	response := []byte("I will help you")
	nc.Subscribe("foo", func(m *nats.Msg) {
		nc.Publish(m.Reply, response)
	})
	msg, err := nc.Request("foo", []byte("help"), 50*time.Millisecond)
	if err != nil {
		t.Fatalf("Received an error on Request test: %s", err)
	}
	if !bytes.Equal(msg.Data, response) {
		t.Fatalf("Received invalid response")
	}
}

func TestRequestNoBody(t *testing.T) {
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(t)
	defer nc.Close()

	response := []byte("I will help you")
	nc.Subscribe("foo", func(m *nats.Msg) {
		nc.Publish(m.Reply, response)
	})
	msg, err := nc.Request("foo", nil, 50*time.Millisecond)
	if err != nil {
		t.Fatalf("Received an error on Request test: %s", err)
	}
	if !bytes.Equal(msg.Data, response) {
		t.Fatalf("Received invalid response")
	}
}

func TestFlushInCB(t *testing.T) {
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(t)
	defer nc.Close()

	ch := make(chan bool)

	nc.Subscribe("foo", func(_ *nats.Msg) {
		nc.Flush()
		ch <- true
	})
	nc.Publish("foo", []byte("Hello"))
	if e := Wait(ch); e != nil {
		t.Fatal("Flush did not return properly in callback")
	}
}

func TestReleaseFlush(t *testing.T) {
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(t)

	for i := 0; i < 1000; i++ {
		nc.Publish("foo", []byte("Hello"))
	}
	go nc.Close()
	nc.Flush()
}

func TestInbox(t *testing.T) {
	inbox := nats.NewInbox()
	if matched, _ := regexp.Match(`_INBOX.\S`, []byte(inbox)); !matched {
		t.Fatal("Bad INBOX format")
	}
}

func TestStats(t *testing.T) {
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(t)
	defer nc.Close()

	data := []byte("The quick brown fox jumped over the lazy dog")
	iter := 10

	for i := 0; i < iter; i++ {
		nc.Publish("foo", data)
	}

	if nc.OutMsgs != uint64(iter) {
		t.Fatalf("Not properly tracking OutMsgs: received %d, wanted %d\n", nc.OutMsgs, iter)
	}
	obb := uint64(iter * len(data))
	if nc.OutBytes != obb {
		t.Fatalf("Not properly tracking OutBytes: received %d, wanted %d\n", nc.OutBytes, obb)
	}

	// Clear outbound
	nc.OutMsgs, nc.OutBytes = 0, 0

	// Test both sync and async versions of subscribe.
	nc.Subscribe("foo", func(_ *nats.Msg) {})
	nc.SubscribeSync("foo")

	for i := 0; i < iter; i++ {
		nc.Publish("foo", data)
	}
	nc.Flush()

	if nc.InMsgs != uint64(2*iter) {
		t.Fatalf("Not properly tracking InMsgs: received %d, wanted %d\n", nc.InMsgs, 2*iter)
	}

	ibb := 2 * obb
	if nc.InBytes != ibb {
		t.Fatalf("Not properly tracking InBytes: received %d, wanted %d\n", nc.InBytes, ibb)
	}
}

func TestRaceSafeStats(t *testing.T) {
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(t)
	defer nc.Close()

	go nc.Publish("foo", []byte("Hello World"))
	time.Sleep(200 * time.Millisecond)

	stats := nc.Stats()

	if stats.OutMsgs != uint64(1) {
		t.Fatalf("Not properly tracking OutMsgs: received %d, wanted %d\n", nc.OutMsgs, 1)
	}
}

func TestBadSubject(t *testing.T) {
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(t)
	defer nc.Close()

	err := nc.Publish("", []byte("Hello World"))
	if err == nil {
		t.Fatalf("Expected an error on bad subject to publish")
	}
	if err != nats.ErrBadSubject {
		t.Fatalf("Expected a ErrBadSubject error: Got %v\n", err)
	}
}

#endif