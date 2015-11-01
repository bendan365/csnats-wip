using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NATS.Client;

namespace NATSBenchmark
{
    /// <summary>
    /// This test is not added to the unit tests as to provide a method of
    /// performance testing outside of the visual studio test harness.
    /// </summary>
    class Benchmark
    {
        private enum testMode
        {
            PUB,
            SUB,
            PUBSUB
        };

        private int      count = 10000000;
        private testMode tm    = testMode.PUBSUB;
        byte[] msg = System.Text.Encoding.UTF8.GetBytes("Hello World");

        private void benchmarkPublishSpeed()
        {
            TimeSpan ts;

            using (IConnection c = new ConnectionFactory().Connect())
            {
                Stopwatch stopWatch = Stopwatch.StartNew();

                for (int i = 0; i < count; i++)
                {
                    c.Publish("foo", msg);
                }
                c.Flush();

                stopWatch.Stop();

                ts = stopWatch.Elapsed;
            }

            System.Console.WriteLine("Sent {0} msgs in {1} seconds", count, ts.TotalSeconds);
            System.Console.WriteLine("Published {0} msgs/second ", (int)(count / ts.TotalSeconds)); 
        }

        private void benchmarkSubscribeSpeed()
        {
            
        }

        private void benchmarkPubSubSpeed()
        {

        }

        private void usage()
        {

        }

        private void parseArgs(string[] args)
        {

        }

        private void runTest(string[] args)
        {
            parseArgs(args);
            benchmarkPublishSpeed();
        }

        static void Main(string[] args)
        {
            new Benchmark().runTest(args);
        }
    }
}

#if sdlkfjsdflkjdsf

func BenchmarkPubSubSpeed(b *testing.B) {
	b.StopTimer()
	s := RunDefaultServer()
	defer s.Shutdown()
	nc := NewDefaultConnection(b)
	defer nc.Close()

	ch := make(chan bool)
	b.StartTimer()

	nc.Opts.AsyncErrorCB = func(nc *nats.Conn, s *nats.Subscription, err error) {
		b.Fatalf("Error : %v\n", err)
	}

	received := int32(0)

	nc.Subscribe("foo", func(m *nats.Msg) {
		atomic.AddInt32(&received, 1)
		if atomic.LoadInt32(&received) >= int32(b.N) {
			ch <- true
		}
	})

	msg := []byte("Hello World")

	for i := 0; i < b.N; i++ {
		if err := nc.Publish("foo", msg); err != nil {
			b.Fatalf("Error in benchmark during Publish: %v\n", err)
		}
		// Don't overrun ourselves and be a slow consumer, server will cut us off
		if int32(i)-atomic.LoadInt32(&received) > 8192 {
			time.Sleep(100)
		}
	}

	// Make sure they are all processed.
	err := WaitTime(ch, 10*time.Second)
	if err != nil {
		b.Fatal("Timed out waiting for messages")
	} else if atomic.LoadInt32(&received) != int32(b.N) {
		b.Fatal(nc.LastError())
	}
	b.StopTimer()
}


#endif