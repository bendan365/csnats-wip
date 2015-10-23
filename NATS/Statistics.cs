using System;

namespace NATS
{
    // Tracks various stats received and sent on this connection,
    // including counts for messages and bytes.
    public class Statistics
    {
        internal Statistics() { }

        internal long inMsgs = 0;

        /// <summary>
        /// Gets the number of inbound messages received.
        /// </summary>
        public long InMsgs
        {
            get { return inMsgs; }
        }

        internal long outMsgs = 0;

        /// <summary>
        /// Gets the number of messages sent.
        /// </summary>
        public long OutMsgs
        {
            get { return outMsgs; }
        }

        internal long inBytes = 0;
        /// <summary>
        /// Gets the number of incoming bytes.
        /// </summary>
        public long InBytes
        {
            get { return inBytes; }
        }

        internal long outBytes = 0;

        /// <summary>
        /// Gets the outgoing number of bytes.
        /// </summary>
        public long OutBytes
        {
            get { return outBytes; }
        }

        internal long reconnects = 0;

        /// <summary>
        /// Gets the number of reconnections.
        /// </summary>
        public long Reconnects
        {
            get {  return reconnects; }
        }

        // deep copy contructor
        internal Statistics(Statistics obj)
        {
            Statistics s = new Statistics();

            s.inMsgs = obj.inMsgs;
            s.inBytes = obj.inBytes;
            s.outBytes = obj.outBytes;
            s.outMsgs = obj.outMsgs;
            s.reconnects = obj.reconnects;
        }
    }


}