﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace NATS
{
    // A Subscription represents interest in a given subject.
    /// <summary>
    /// A Subscription object represents interest in a given subject.
    /// A subscription is created and attached to a connection.
    /// </summary>
    public class Subscription : IDisposable
    {
        readonly  internal  Object mu = new Object(); // lock

        internal  long           sid = 0; // subscriber ID.
        private   long           msgs;
        internal  protected long delivered;
        private   long           bytes;
        internal  protected long max = -1;

        // slow consumer
        internal bool       sc   = false;

        internal Connection conn = null;
        internal Channel<NATS.Msg> mch  = new Channel<NATS.Msg>();

        // Subject that represents this subscription. This can be different
        // than the received subject inside a Msg if this is a wildcard.
        private string      subject = null;

        internal Subscription(Connection conn, string subject, string queue)
        {
            this.conn = conn;
            this.subject = subject;
            this.queue = queue;
        }

        internal void closeChannel()
        {
            lock (mu)
            {
                mch.close();
                mch = null;
            }
        }

        /// <summary>
        /// Gets the subject of interest.
        /// </summary>
        public string Subject
        {
            get { return subject; }
        }

        // Optional queue group name. If present, all subscriptions with the
        // same name will form a distributed queue, and each message will
        // only be processed by one member of the group.
        string queue;

        /// <summary>
        /// Gets the name of the queue groups this subscriber belongs to.
        /// </summary>
        public string Queue
        {
            get { return queue; }
        }

        /// <summary>
        /// Gets the Connection this subscriber was created on.
        /// </summary>
        public Connection Connection
        {
            get
            {
                return conn;
            }
        }
 
        internal bool tallyMessage(long bytes)
        {
            lock (mu)
            {
                if (max > 0 && msgs > max)
                    return true;

                this.msgs++;
                this.bytes += bytes;
            }

            return false;
        }


        protected internal virtual bool processMsg(NATS.Msg msg)
        {
            return true;
            // NOOP;
        }

        // returns false if the message could not be added because
        // the channel is full, true if the message was added
        // to the channel.
        internal bool addMessage(Msg msg, int maxCount)
        {
            if (mch != null)
            {
                if (mch.Count >= maxCount)
                {
                    return false;
                }
                else
                {
                    sc = false;
                    mch.add(msg);
                }
            }
            return true;
        }

        /// <summary>
        /// True if the subscription is active, false otherwise.
        /// </summary>
        public bool IsValid
        {
            get
            {
                lock (mu)
                {
                    return (conn != null);
                }
            }
        }

        /// <summary>
        /// Removes interest in the given subject.
        /// </summary>
        public virtual void Unsubscribe()
        {
            Connection c;
            lock (mu)
            {
                c = this.conn;
            }

            if (c == null)
                throw new NATSBadSubscriptionException();

            c.unsubscribe(this, 0);
        }

        /// <summary>
        /// AutoUnsubscribe will issue an automatic Unsubscribe that is
        /// processed by the server when max messages have been received.
        /// This can be useful when sending a request to an unknown number
        /// of subscribers. Request() uses this functionality.
        /// </summary>
        /// <param name="max">Number of messages to receive before unsubscribing.</param>
        public virtual void AutoUnsubscribe(int max)
        {
            Connection localConn = null;

            lock (mu)
            {
                if (conn == null)
                    throw new NATSBadSubscriptionException();

                localConn = conn;
            }

            localConn.unsubscribe(this, max);
        }

        /// <summary>
        /// Gets the number of messages received, but not processed,
        /// this subscriber.
        /// </summary>
        public int QueuedMessageCount
        {
            get
            {
                lock (mu)
                {
                    if (this.conn == null)
                        throw new NATSBadSubscriptionException();

                    return mch.Count;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                Unsubscribe();
            }
            catch (Exception)
            { 
                // We we get here with normal usage, for example when
                // auto unsubscribing, so just this here.
            }
        }

    }  // Subscription

}