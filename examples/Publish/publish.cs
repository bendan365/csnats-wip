// Copyright 2015 Apcera Inc. All rights reserved.

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using NATS.Client;


namespace NATSExamples
{
    class Publisher
    {
        Dictionary<string, string> parsedArgs = new Dictionary<string, string>();

        int count = 2000000;
        string url = Defaults.Url;
        string subject = "foo";
        byte[] payload = null;

        public void Run(string[] args)
        {
            Stopwatch sw = null;

            parseArgs(args);
            banner();

            Options opts = ConnectionFactory.GetDefaultOptions();
            opts.Url = url;

            using (IConnection c = new ConnectionFactory().CreateConnection(opts))
            {
                sw = Stopwatch.StartNew();

                for (int i = 0; i < count; i++)
                {
                    c.Publish(subject, payload);
                }
                c.Flush();

                sw.Stop();

                System.Console.Write("Published {0} msgs in {1} seconds ", count, sw.Elapsed.TotalSeconds);
                System.Console.WriteLine("({0} msgs/second).",
                    (int)(count / sw.Elapsed.TotalSeconds));
                printStats(c);

            }
        }

        private void printStats(IConnection c)
        {
            IStatistics s = c.Stats;
            System.Console.WriteLine("Statistics:  ");
            System.Console.WriteLine("   Incoming Payload Bytes: {0}", s.InBytes);
            System.Console.WriteLine("   Incoming Messages: {0}", s.InMsgs);
            System.Console.WriteLine("   Outgoing Payload Bytes: {0}", s.OutBytes);
            System.Console.WriteLine("   Outgoing Messages: {0}", s.OutMsgs);
        }

        private void usage()
        {
            System.Console.Error.WriteLine(
                "Usage:  Publish [-url url] [-subject subject] " +
                "-count [count] [-payload payload]");

            System.Environment.Exit(-1);
        }

        private void parseArgs(string[] args)
        {
            if (args == null)
                return;

            for (int i = 0; i < args.Length; i++)
            {
                if (i + 1 == args.Length)
                    usage();

                parsedArgs.Add(args[i], args[i + 1]);
                i++;
            }

            if (parsedArgs.ContainsKey("-count"))
                count = Convert.ToInt32(parsedArgs["-count"]);

            if (parsedArgs.ContainsKey("-url"))
                url = parsedArgs["-url"];

            if (parsedArgs.ContainsKey("-subject"))
                subject = parsedArgs["-subject"];

            if (parsedArgs.ContainsKey("-payload"))
                payload = Encoding.UTF8.GetBytes(parsedArgs["-payload"]);
        }

        private void banner()
        {
            System.Console.WriteLine("Publishing {0} messages on subject {1}",
                count, subject);
            System.Console.WriteLine("  Url: {0}", url);
            System.Console.WriteLine("  Payload is {0} bytes.",
                payload != null ? payload.Length : 0);
        }

        public static void Main(string[] args)
        {
            try
            {
                new Publisher().Run(args);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine("Exception: " + ex.Message);
                System.Console.Error.WriteLine(ex);
            }

        }
    }
}
