
# NATS - C# Client
A [C# .NET](https://msdn.microsoft.com/en-us/vstudio/aa496123.aspx) client for the [NATS messaging system](https://nats.io).

This is an alpha release, based on the [NATS GO Client](https://github.com/nats-io/nats).

[![License MIT](https://img.shields.io/npm/l/express.svg)](http://opensource.org/licenses/MIT)

## Installation

First, download the source code:
```
git clone git@github.com:nats-io/csnats.git .
```

### Quick Start

Ensure you have installed the .NET Framework 4.0 or greater.  Set your path to include csc.exe, e.g.
```
set PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319;%PATH%
```
Then, build the assembly.  There is a simple batch file to build, requiring only the .NET framework SDK to be installed.

```
build.bat
```
The batch file will create a bin directory, and copy all binary files, including samples, into it.

### Visual Studio

The recommended alternative is to load NATS.sln into Visual Studio 2013 Express or better.  Later versions of Visual Studio should automatically upgrade the solution and project files for you.  XML documenation is generated, so code completion, context help, etc, will be available in the editor.

#### Project files

The NATS Visual Studio Solution contains several projects, listed below.

* NATS - The NATS.Client assembly
* NATSUnitTests - Visual Studio Unit Tests (ensure you have gnatds.exe in your path for these).
* Publish Subscribe
  * Publish - A sample publisher.
  * Subscribe - A sample subscriber.
* QueueGroup - An example queue group subscriber.
* Request Reply
  * Requestor - A requestor sample.
  * Replier - A sample replier for the Requestor application.

All examples provide statistics for benchmarking.

## Basic Usage

NATS .NET uses interfaces to reference most NATS client objects, and delegates for all event.


The steps to create a NATS application are:

First, reference the NATS.Client assembly so you can use it in your code.  Be sure to add a reference in your project or if compiling via command line, compile with the /r:NATS.Client.DLL parameter.
```C#
using NATS.Client;
```

Next create a connection factory
```C#
ConnectionFactory cf = new ConnectionFactory();
```

One can setup options by modifying a default set obtained from the factory.
```C#
Options opts = ConnectionFactory.GetDefaultOptions();
```

Create a connection.
```C#
IConnection c = cf.Connect(opts);
```

If using the default options, you can use the Connect() API instead.
```C#
IConnection c = cf.Connect();
```

To publish, call the IConnection.Publish(...) API.
```C#
byte[] data = Encoding.UTF8.GetBytes("hello");
c.Publish("foo", data);
```

There are two types of subscribers, synchronous and asynchronous.
To synchronously subscribe, then wait for a message:
```C#
ISyncSubscription s = c.SubscribeSync("foo");
Msg m = s.NextMessage();
Console.WriteLine("Received: " + m);
```

To asychronously receive, create an asychronous subscription and add
a message handler.
```C#
IAsyncSubscription s = c.SubscribeAsync(subject))

s.MessageHandler += (sender, args) =>
{
    Console.WriteLine("Received: " + args.Message);
};

s.Start();

// Sleep for a minute and let NATS process messages.
Thread.Sleep(60000);
```
Note the Start() method - Start() MUST be called to start receiving messages.  Adding a step to start the subscriber allows one to multicast delegates and ensure they will all be invoked when process messages.

## Wildcard Subscriptions

The NATS .NET client supports full wildcard subscriptions.

## Queue Groups

Queue groups are created by creating a synchronous or asychronous subsciption using the API that provides a queue group name.

```C#
ISyncSubscription s1 = c.SubscribeSync("foo", "group");
```

or

```C#
IAsyncSubscription s = c.SubscribeAsync("foo", "group");
```

To unsubscribe, call the ISubscriber Unsubscribe method:
```C#
s.Unsubscribe();
```

When finished with NATS, clean up and free resources.
```C#
s.Close();
c.Close();
```


## Advanced Usage

Connection and Subscriber objects implement IDisposable and can be created in a using statement.  Here is all the code required to connect to a default server, receive ten messages, and clean up, unsubcribing and closing the connection when finished.

```C#
            using (IConnection c = new ConnectionFactory().Connect())
            {
                using (ISyncSubscription s = c.SubscribeSync("foo"))
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Msg m = s.NextMessage();
                        System.Console.WriteLine("Received: " + m);
                    }
                }  
            }
```

Or to publish ten messages:

```C#
            using (IConnection c = new ConnectionFactory().Connect())
            {
                for (int i = 0; i < 10; i++)
                {
                    c.Publish("foo", Encoding.UTF8.GetBytes("hello"));
                }
            }
```

Flush a connection to the server - this call returns when all messages have been processed.  Optionally, a timeout in milliseconds can be passed.

```C#
c.Flush();

c.Flush(1000);
```

Setup a subscriber to auto-unsubscribe after ten messsages.
```C#
        IAsyncSubscription s = c.SubscribeAsync("foo");
        s.MessageHandler += (sender, args) =>
        {
           Console.WriteLine("Received: " + args.Message);
        };
                
        s.Start();
        s.AutoUnsubscribe(10);
```

Note that an anonymous function was used.  This is for brevity here - in practice, delegate functions can be used as well.  

Other events can be assigned delegate methods through the options object.
```C#
            Options opts = ConnectionFactory.GetDefaultOptions();

            opts.AsyncErrorEventHandler += (sender, args) =>
            {
                Console.WriteLine("Error: ");
                Console.WriteLine("   Server: " + args.Conn.ConnectedUrl);
                Console.WriteLine("   Message: " + args.Error);
                Console.WriteLine("   Subject: " + args.Subscription.Subject);
            };

            opts.ClosedEventHandler += (sender, args) =>
            {
                Console.WriteLine("Connection Closed: ");
                Console.WriteLine("   Server: " + args.Conn.ConnectedUrl);
            };

            opts.DisconnectedEventHandler += (sender, args) =>
            {
                Console.WriteLine("Connection Disconnected: ");
                Console.WriteLine("   Server: " + args.Conn.ConnectedUrl);
            };

            IConnection c = new ConnectionFactory().Connect(opts);
```



## Clustered Usage

```C#
            string[] servers = new string[] {
                "nats://localhost:1222",
		"nats://localhost:1224"
            };

            Options opts = ConnectionFactory.GetDefaultOptions();
            opts.MaxReconnect = 2;
            opts.ReconnectWait = 1000;
            opts.NoRandomize = true;
            opts.Servers = servers;
            
            IConnection c = new ConnectionFactory().Connect(opts);
```

Known Issues
* There can be an issue with a flush hanging in some situtions.  I'm looking into it.
* Some unit tests are failing due to long connect times due to the underlying .NET TCPClient API.

TODO
* API documentation
* WCF bindings
* Strong name the assembly


## License

(The MIT License)

Copyright (c) 2012-2015 Apcera Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to
deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
IN THE SOFTWARE.


