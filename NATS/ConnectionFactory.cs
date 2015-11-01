using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NATS.Client
{
    public sealed class ConnectionFactory
    {
        public ConnectionFactory() { }

        /// <summary>
        /// Connect will attempt to connect to the NATS server.
        /// The url can contain username/password semantics.
        /// </summary>
        /// <param name="url">The url</param>
        /// <returns>A new connection to the NATS server</returns>
        public IConnection Connect(string url)
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
        public IConnection SecureConnect(string url)
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
        public IConnection Connect()
        {
            return Connect(new Options());
        }

        /// <summary>
        /// Connect to the NATs server using the provided options.
        /// </summary>
        /// <param name="opts">NATs client options</param>
        /// <returns>A new connection to the NATS server</returns>
        public IConnection Connect(Options opts)
        {
            Connection nc = new Connection(opts);
            nc.connect();
            return nc;
        }


    }
}
