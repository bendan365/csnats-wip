using System;
using System.Threading;
using System.Threading.Tasks;

namespace NATS
{
    public sealed class SyncSubscription : Subscription
    {
        internal SyncSubscription(Connection conn, string subject, string queue)
            : base(conn, subject, queue) { }

        /// <summary>
        /// This method will return the next message available to a synchronous subscriber
        /// or block until one is available.
        /// </summary>
        /// <returns></returns>
        public Msg NextMessage()
        {
            return NextMessage(-1);
        }

        /// <summary>
        /// This method will return the next message available to a synchronous subscriber
        /// or block until one is available. A timeout can be used to return when no
        /// message has been delivered.
        /// </summary>
        /// <remarks>
        /// A timeout of 0 will return null immediately if there are no messages.
        /// </remarks>
        /// <param name="timeout">Timeout value</param>
        /// <returns></returns>
        public Msg NextMessage(int timeout)
        {
            Connection   localConn;
            Channel<Msg> localChannel;
            long         localMax;
            Msg          msg;

            lock (mu)
            {
                if (mch == null)
                    throw new NATSConnectionClosedException();
                if (conn == null)
                    throw new NATSBadSubscriptionException();
                if (sc)
                    throw new NATSSlowConsumerException();

                localConn = this.conn;
                localChannel = this.mch;
                localMax = this.max;
            }

            if (timeout >= 0)
            {
                msg = localChannel.get(timeout);
            }
            else
            {
                msg = localChannel.get();
            }

            if (msg != null)
            {
                long d = Interlocked.Increment(ref this.delivered);
                if (d == max)
                {
                    // Remove subscription if we have reached max.
                    localConn.removeSub(this);
                }
                if (localMax > 0 && d > localMax)
                {
                    throw new NATSException("nats: Max messages delivered");
                }
            }

            return msg;
        }
    }
}