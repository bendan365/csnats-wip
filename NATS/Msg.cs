using System;
using System.Text;

namespace NATS.Client
{
    // Msg is a structure used by Subscribers and PublishMsg().
    public sealed class Msg
    {
        private string subject;
        private string reply;
        private byte[] data;
        internal Subscription sub;

        /// <summary>
        /// Creates an empty message.
        /// </summary>
        public Msg()
        {
            subject = null;
            reply   = null;
            data    = null;
            sub     = null;
        }

        internal Msg(MsgArg arg, Subscription s, byte[] data)
        {
            this.subject = new String(arg.subject.ToCharArray());

            if (arg.reply != null)
            {
                this.reply = new String(arg.reply.ToCharArray());
            }

            this.sub = s;

            // make a deep copy of the bytes for this message.
            long len = data.Length;
            this.data = new byte[len];
            Array.Copy(data, this.data, len);
        }

        /// <summary>
        /// Gets or sets the subject.
        /// </summary>
        public string Subject
        {
            get { return subject; }
            set { subject = value; }
        }

        /// <summary>
        /// Gets or sets the reply subject.
        /// </summary>
        public string Reply
        {
            get { return reply; }
            set { reply = value; }
        }

        /// <summary>
        /// Sets data in the message.  This copies application data into the message.
        /// </summary>
        /// <remarks>
        /// See <see cref="AssignData">AssignData</see> to directly pass the bytes
        /// buffer.
        /// </remarks>
        /// <see cref="AssignData"/>
        /// 
        public byte[] Data
        {
            get { return data; }

            set
            {
                if (value == null)
                    this.data = null;

                int len = value.Length;
                if (len == 0)
                    this.data = null;
                else
                {
                    this.data = new byte[len];
                    Array.Copy(value, data, len);
                }
            }
        }

        /// <summary>
        /// Assigns the data of the message.  This is a direct assignment,
        /// to avoid expensive copy operations.  A change to the passed
        /// byte array will be changed in the message.
        /// </summary>
        /// <remarks>
        /// The application is responsible for the data integrity in the message.
        /// </remarks>
        /// <param name="data">a bytes buffer of data.</param>
        public void AssignData(byte[] data)
        {
            this.data = data;
        }

        /// <summary>
        /// Gets the subscription assigned to the messages.
        /// </summary>
        public Subscription ArrivalSubcription
        {
            get { return sub; }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.AppendFormat("Subject={0};Reply={1};Payload=<", Subject,
                Reply != null ? reply : "null");

            int len = data.Length;
            int i;

            for (i = 0; i < 32 && i < len; i++)
            {
                sb.Append((char)data[i]);
            }

            if (i < len)
            {
                sb.AppendFormat("{0} more bytes", len - i);
            }
            
            sb.Append(">}");

            return sb.ToString();
        }
    }


}