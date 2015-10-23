﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace NATS
{
    /// <summary>
    /// An object of this class is an asynchronous subscription representing interest
    /// in a subject.   The subject can have wildcards (partial:*, full:>).
    /// Messages will be delivered to the associated MsgHandler event delegates.
    /// While nothing prevents event handlers from being added or 
    /// removed while processing messages, no messages will be received until
    /// Start() has been called.  This allows all event handlers to be added
    /// before message processing begins.
    /// </summary>
    /// <remarks><see cref="MsgHandler">See MsgHandler</see>.</remarks>
    public sealed class AsyncSubscription : Subscription
    {

        private NATS.MsgHandler     msgHandler = null;
        private MsgHandlerEventArgs msgHandlerArgs = new MsgHandlerEventArgs();
        private Task                msgFeeder = null;

        internal AsyncSubscription(Connection conn, string subject, string queue)
            : base(conn, subject, queue) { }

        protected internal override bool processMsg(NATS.Msg msg)
        {
            Connection c;
            MsgHandler handler;
            long max;

            lock (mu)
            {
                c = this.conn;
                handler = this.msgHandler;
                max = this.max;
            }

            // the message handler has not been setup yet, drop the 
            // message.
            if (msgHandler == null)
                return true;

            if (conn == null)
                return false;

            long d = Interlocked.Increment(ref delivered);
            if (max <= 0 || d <= max)
            {
                msgHandlerArgs.msg = msg;
                msgHandler(this, msgHandlerArgs);
            }

            return true;
        }

        private void enableAsyncProcessing()
        {
            if (msgFeeder == null)
            {
                msgFeeder = new Task(() => { conn.deliverMsgs(mch); });
                msgFeeder.Start();
            }
        }

        private void disableAsyncProcessing()
        {
            if (msgHandler != null)
                return;

            if (msgFeeder != null)
            {
                mch.close();
                msgFeeder.Wait();
            }
        }

        /// <summary>
        /// Adds or removes a message handler to this subscriber.
        /// </summary>
        /// <remarks><see cref="MsgHandler">See MsgHandler</see></remarks>
        public event MsgHandler MessageHandler
        {
            add
            {
                msgHandler += value;
            }
            remove
            {
                msgHandler -= value;
            }
        }

        /// <summary>
        /// This completes the subsciption process notifying the server this subscriber
        /// has interest.
        /// </summary>
        public void Start()
        {
            conn.sendSubscriptonMessage(this);
            enableAsyncProcessing();
        }

        override public void Unsubscribe()
        {
            disableAsyncProcessing();
            base.Unsubscribe();
        }
    }
}