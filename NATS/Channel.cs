using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NATS
{
    // This class mirrors GO channel functionality - it's basically a
    // a threadsafe blocking queue.
    internal sealed class Channel<T>
    {
        BlockingCollection<T> coll;

        internal Channel(int size)
        {
            coll = new BlockingCollection<T>(size);
        }

        internal Channel()
        {
            coll = new BlockingCollection<T>();
        }

        internal bool isComplete()
        {
            return coll.IsAddingCompleted;
        }

        internal void add(T val)
        {
            try
            {
                coll.Add(val);
            }
            catch (InvalidOperationException ioe)
            { 
                System.Console.WriteLine(ioe); 
            }
        }

        internal int Length
        {
            get
            {
                return coll.Count;
            }
        }

        internal T get()
        {
            return (T)coll.Take();
        }

        internal T get(int msTimeout)
        {
            T item;
            if (coll.TryTake(out item, msTimeout))
            {
                return item;
            }

            throw new NATSTimeoutException();
        }

        internal void close()
        {
            coll.CompleteAdding();
        }
    }
}