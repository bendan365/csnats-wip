using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NATS.Client;

namespace NATSUnitTests
{
    /// <summary>
    /// Run these tests with the gnatsd auth.conf configuration file.
    /// </summary>
    [TestClass]
    public class TestAuthorization
    {
        int hitDisconnect;

        private void connectAndFail(String url)
        {
            try
            {
                System.Console.WriteLine("Trying: " + url);

                hitDisconnect = 0;
                Options opts = ConnectionFactory.GetDefaultOptions();
                opts.Url = url;
                opts.DisconnectedEventHandler += handleDisconnect;
                Connection c = new ConnectionFactory().Connect(url);

                Assert.Fail("Expected a failure; did not receive one");
                
                c.Close();
            }
            catch (Exception e)
            {
                System.Console.WriteLine("Success with expected failure: " + e.Message);
                return;
            }
            finally
            {
                if (hitDisconnect > 0)
                    Assert.Fail("The disconnect event handler was incorrectly invoked.");
            }
        }

        private void handleDisconnect(object sender, ConnEventArgs e)
        {
            hitDisconnect++;
        }

        [TestMethod]
        public void TestAuthSuccess()
        {
            Connection c = new ConnectionFactory().Connect("nats://username:password@localhost:8232");
            c.Close();
        }

        [TestMethod]
        public void TestAuthFailure()
        {
            try
            {
                //connectAndFail("nats://username@localhost:8232");
                connectAndFail("nats://username:badpass@localhost:8232");
                connectAndFail("nats://localhost:8232");

                // FIXME - this one fails.
                connectAndFail("nats://badname:password@localhost:8232");

            }
            catch (Exception e)
            {
                System.Console.WriteLine(e);
                throw e;
            }
        }
    }
}
