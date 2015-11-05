
# NATS - C# Client
A [C# .NET](https://msdn.microsoft.com/en-us/vstudio/aa496123.aspx) client for the [NATS messaging system](https://nats.io).

This is an alpha release, based on the [NATS GO Client](https://github.com/nats-io/nats).

[![License MIT](https://img.shields.io/npm/l/express.svg)](http://opensource.org/licenses/MIT)

## Installation

First, download the source code:
```
git clone git@github.com:nats-io/csnats.git .
```
Ensure you have installed the .NET Framework 4.0 or greater.  Set your path to include csc.exe, e.g.
```
set PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319;%PATH%
```
Then, build the assembly.  There are two ways to do this.  To quickly get started, there is a simple batch file to build, requiring only the .NET framework SDK to be installed.

```
build.bat
```
The "quick start" batch file will create a bin directory, and copy all binary files, including samples, into it.

The recommended alternative is to load NATS.sln into Visual Studio 2013 Express or better.  Later versions of Visual Studio should automatically upgrade the solution and project files for you.  XML documenation is generated, so code completion, context help, etc, will be available in the editor.

### Project files

The NATS Visual Studio Solution contains several projects, listed below.

* NATS - The NATS.Client assembly
* NATSUnitTests - Visual Studio Unit Tests
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

1)  First, reference the NATS.Client assembly so you can use it in your code.  Be sure to add a reference in your project or if compiling via command line, compile with the /r:NATS.Client.DLL parameter.
```C#
using NATS.Client;
```

2) Create a connection factory
```C#
ConnectionFactory cf = new ConnectionFactory();
```

3) Setup your options by modifying a default set obtained from the factory.
```C#
Options opts = ConnectionFactory.GetDefaultOptions();
```

4) Create a connection.
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

The Connection and Subscriber interfaces implement IDisposable, allowing one to use them in a using statement.  Here is a simple application that receives and prints

```go

// Flush connection to server, returns when all messages have been processed.
nc.Flush()
fmt.Println("All clear!")

// FlushTimeout specifies a timeout value as well.
err := nc.FlushTimeout(1*time.Second)
if err != nil {
    fmt.Println("All clear!")
} else {
    fmt.Println("Flushed timed out!")
}

// Auto-unsubscribe after MAX_WANTED messages received
const MAX_WANTED = 10
sub, err := nc.Subscribe("foo")
sub.AutoUnsubscribe(MAX_WANTED)

// Multiple connections
nc1 := nats.Connect("nats://host1:4222")
nc2 := nats.Connect("nats://host2:4222")

nc1.Subscribe("foo", func(m *Msg) {
    fmt.Printf("Received a message: %s\n", string(m.Data))
})

nc2.Publish("foo", []byte("Hello World!"));

```

## Clustered Usage

```go

var servers = []string{
	"nats://localhost:1222",
	"nats://localhost:1223",
	"nats://localhost:1224",
}

// Setup options to include all servers in the cluster
opts := nats.DefaultOptions
opts.Servers = servers

// Optionally set ReconnectWait and MaxReconnect attempts.
// This example means 10 seconds total per backend.
opts.MaxReconnect = 5
opts.ReconnectWait = (2 * time.Second)

// Optionally disable randomization of the server pool
opts.NoRandomize = true

nc, err := opts.Connect()

// Setup callbacks to be notified on disconnects and reconnects
nc.Opts.DisconnectedCB = func(_ *Conn) {
    fmt.Printf("Got disconnected!\n")
}

// See who we are connected to on reconnect.
nc.Opts.ReconnectedCB = func(nc *Conn) {
    fmt.Printf("Got reconnected to %v!\n", nc.ConnectedUrl())
}

```

Known Issues
* There can be an issue with a flush hanging in some situtions.  I'm looking into it.
* Some unit tests are failing due to long connect times in the .NET TCPClient.


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


