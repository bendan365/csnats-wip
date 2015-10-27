using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NATS.Client
{
    public interface ISyncSubscription : ISubscription, IDisposable
    {
        /// <summary>
        /// This method will return the next message available to a synchronous subscriber
        /// or block until one is available.
        /// </summary>
        /// <returns></returns>
        Msg NextMessage();

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
        Msg NextMessage(int timeout);
    }
}
