using System;

namespace NATS.Client
{
    // Tracks individual backend servers.
    internal class Srv
    {
        internal Uri url = null;
        internal bool didConnect = false;
        internal int reconnects = 0;
        internal System.DateTime lastAttempt = System.DateTime.Now;

        // never create a srv object without a url.
        private Srv() { }

        internal Srv(String urlString)
        {
            try
            {
                this.url = new Uri(urlString);
            }
            catch (Exception e)
            {
                throw new NATSException("Invalid URL", e);
            }
        }

        internal void updateLastAttempt()
        {
            lastAttempt = System.DateTime.Now;
        }

        internal TimeSpan TimeSinceLastAttempt
        {
            get
            {
                return (DateTime.Now - lastAttempt);
            }
        }
    }
}

