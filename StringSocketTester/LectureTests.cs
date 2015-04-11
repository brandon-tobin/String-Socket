using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using System.Text;

namespace CustomNetworking
{
    /// <summary>
    /// Helper class for making assertions in a multi-threaded test program.
    /// </summary>
    public class Asserter
    {
        // Helps synchronize between threads
        private ManualResetEvent mre;

        /// <summary>
        /// Create a new Asserter, which must be shared between two threads
        /// </summary>
        public Asserter()
        {
            mre = new ManualResetEvent(false);
        }

        /// <summary>
        /// The non-main thread should call this to allow an assertion to proceed
        /// on the main thread.
        /// </summary>
        public void Set()
        {
            mre.Set();
        }

        /// <summary>
        /// The expected value about which an assertion needs to be made
        /// </summary>
        public object Expected { get; set; }

        /// <summary>
        /// The actual value about which an assertion needs to be made
        /// </summary>
        public object Actual { get; set; }

        /// <summary>
        /// Called by the main testing thread.  Blocks until the Set method
        /// is called or until the specified number of milliseconds has
        /// passed.  (If no timeout is supplied, no timeout will occur.)
        /// If a timeout happens, executes an Assert.Fail().  Otherwise,
        /// asserts equality between Expected and Actual.
        /// </summary>
        /// <param name="timeout"></param>
        public void WaitAreEqual(int timeout = -1)
        {
            if (mre.WaitOne(timeout))
            {
                Assert.AreEqual(Expected, Actual);
            }
            else
            {
                Assert.Fail("Timed out");
            }
        }
    }


    [TestClass]
    public class LectureTests
    {
        /// <summary>
        /// Is the callback called when doing a send?
        /// Is the right string sent?
        /// </summary>
        [TestMethod]
        public void SimpleTest()
        {
            // Declare these here so they are accessible in
            // the finally block
            TcpListener server = null;
            TcpClient client = null;
            try
            {
                // Obtain a pair of sockets
                int port = 4002;
                server = new TcpListener(IPAddress.Any, port);
                server.Start();
                client = new TcpClient("localhost", port);
                Socket serverSocket = server.AcceptSocket();
                Socket clientSocket = client.Client;

                // Create a pair of StringSocket 
                StringSocket sendSocket = new StringSocket(clientSocket, new UTF8Encoding());
                StringSocket rcvSocket = new StringSocket(serverSocket, new UTF8Encoding());

                // Make sure that we receive the string "Hello"
                Asserter rcvAsserter = new Asserter();
                rcvSocket.BeginReceive((s, e, p) =>
                {
                    rcvAsserter.Expected = "Hello";
                    rcvAsserter.Actual = s;
                    rcvAsserter.Set();
                },
                    "");

                // Send a "Hello" and make sure the callback is called
                Asserter sendAsserter = new Asserter();
                sendSocket.BeginSend(
                    "Hello\n",
                    (e, p) =>
                    {
                        sendAsserter.Expected = "Payload";
                        sendAsserter.Actual = p;
                        sendAsserter.Set();
                    },
                "Payload");

                // Perform the assertions in the main testing thread
                sendAsserter.WaitAreEqual(2000);
                rcvAsserter.WaitAreEqual(2000);
            }
            finally
            {
                // Close everything down
                server.Stop();
                client.Close();
            }
        }
    }
}
