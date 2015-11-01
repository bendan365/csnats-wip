using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;


namespace NATS.Client
{
    public class NATSException : Exception
    {
        internal NATSException() : base() { }
        internal NATSException(string err) : base (err) {}
        internal NATSException(string err, Exception innerEx) : base(err, innerEx) { }
    }

    public class NATSConnectionException : NATSException
    {
        internal NATSConnectionException(string err) : base(err) { }
        internal NATSConnectionException(string err, Exception innerEx) : base(err, innerEx) { }
    }

    public class NATSProtocolException : NATSException
    {
        internal NATSProtocolException(string err) : base(err) { }
    }

    public class NATSNoServersException : NATSException
    {
        internal NATSNoServersException(string err) : base(err) { }
    }

    public class NATSInvalidParameterException : NATSException
    {
        internal NATSInvalidParameterException(string err) : base(err) { }
    }

    public class NATSSecureConnWantedException : NATSException
    {
        internal NATSSecureConnWantedException() : base("A secure connection is requested.") { }
    }

    public class NATSSecureConnRequiredException : NATSException
    {
        internal NATSSecureConnRequiredException() : base("A secure connection is required.") { }
        internal NATSSecureConnRequiredException(String s) : base(s) { }
    }

    public class NATSConnectionClosedException : NATSException
    {
        internal NATSConnectionClosedException() : base("Connection is closed.") { }
    }

    public class NATSSlowConsumerException : NATSException
    {
        internal NATSSlowConsumerException() : base("Consumer is too slow.") { }
    }

    public class NATSStaleConnectionException : NATSException
    {
        internal NATSStaleConnectionException() : base("Connection is stale.") { }
    }

    public class NATSMaxPayloadException : NATSException
    {
        internal NATSMaxPayloadException() : base("Maximum payload size has been exceeded") { }
        internal NATSMaxPayloadException(string err) : base(err) { }
    }
    
    public class NATSBadSubscriptionException : NATSException
    {
        internal NATSBadSubscriptionException() : base("Subcription is not valid.") { }
    }

    public class NATSTimeoutException : NATSException
    {
        internal NATSTimeoutException() : base("Timeout occurred.") { }
    }
}