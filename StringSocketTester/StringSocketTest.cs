using CustomNetworking;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;

namespace StringSocketTester
{
    /// <summary>
    ///This is a test class for StringSocketTest and is intended
    ///to contain all StringSocketTest Unit Tests
    ///</summary>
    [TestClass()]
    public class StringSocketTest
    {
        /// <summary>
        ///A simple test for BeginSend and BeginReceive
        ///</summary>
        [TestMethod()]
        public void Test1()
        {
            new Test1Class().run(4001);
        }

        public class Test1Class
        {
            // Data that is shared across threads
            private ManualResetEvent mre1;
            private ManualResetEvent mre2;
            private String s1;
            private object p1;
            private String s2;
            private object p2;

            // Timeout used in test case
            private static int timeout = 2000;

            public void run(int port)
            {
                // Create and start a server and client.
                TcpListener server = null;
                TcpClient client = null;

                try
                {
                    server = new TcpListener(IPAddress.Any, port);
                    server.Start();
                    client = new TcpClient("localhost", port);

                    // Obtain the sockets from the two ends of the connection.  We are using the blocking AcceptSocket()
                    // method here, which is OK for a test case.
                    Socket serverSocket = server.AcceptSocket();
                    Socket clientSocket = client.Client;

                    // Wrap the two ends of the connection into StringSockets
                    StringSocket sendSocket = new StringSocket(serverSocket, new UTF8Encoding());
                    StringSocket receiveSocket = new StringSocket(clientSocket, new UTF8Encoding());

                    // This will coordinate communication between the threads of the test cases
                    mre1 = new ManualResetEvent(false);
                    mre2 = new ManualResetEvent(false);

                    // Make two receive requests
                    receiveSocket.BeginReceive(CompletedReceive1, 1);
                    receiveSocket.BeginReceive(CompletedReceive2, 2);

                    // Now send the data.  Hope those receive requests didn't block this thread!
                    String msg = "Hello world\nThis is a test\n";
                    foreach (char c in msg)
                    {
                        sendSocket.BeginSend(c.ToString(), (e, o) => { }, null);
                    }


                    // Make sure the lines were received properly.
                    Assert.AreEqual(true, mre1.WaitOne(timeout), "Timed out waiting 1");
                    Assert.AreEqual("Hello world", s1);
                    Assert.AreEqual(1, p1);

                   Assert.AreEqual(true, mre2.WaitOne(timeout), "Timed out waiting 2");
                   Assert.AreEqual("This is a test", s2);
                   Assert.AreEqual(2, p2);
                }
                finally
                {
                    server.Stop();
                    client.Close();
                }
            }

            // This is the callback for the first receive request.  We can't make assertions anywhere
            // but the main thread, so we write the values to member variables so they can be tested
            // on the main thread.
            private void CompletedReceive1(String s, Exception o, object payload)
            {
                s1 = s;
                p1 = payload;
                mre1.Set();
            }

            // This is the callback for the second receive request.
            private void CompletedReceive2(String s, Exception o, object payload)
            {
                s2 = s;
                p2 = payload;
                mre2.Set();
            }
        }

        [TestMethod]
        public void SendTest()
        {
            TcpListener server = null;
            TcpClient client = null;
            int port = 4002;
            byte[] incomingBytes = new byte[1024];
            char[] incomingChars = new char[1024];
            UTF8Encoding encoding = new UTF8Encoding();
            try
            {
                server = new TcpListener(IPAddress.Any, port);
                server.Start();
                client = new TcpClient("localhost", port);

                // Obtain the sockets from the two ends of the connection. We are using the blocking AcceptSocket()
                // method here, which is OK for a test case.
                Socket serverSocket = server.AcceptSocket();
                Socket clientSocket = client.Client;

                // Wrap the two ends of the connection into StringSockets
                StringSocket sendSocket = new StringSocket(serverSocket, encoding);
                // String to send and recieve
                string testString = "Hello world\nThis is a test\n";

                // Tell the client socket to begin listening for a message to recieve
                clientSocket.BeginReceive(incomingBytes, 0, incomingBytes.Length,
                SocketFlags.None, (result) =>
                {
                    int bytesRead = clientSocket.EndReceive(result);
                    // Convert the bytes into characters and appending to incoming
                    encoding.GetDecoder().GetChars(incomingBytes, 0, bytesRead, incomingChars, 0, false);

                    // Turn the char[] into a string
                    StringBuilder incomingString = new StringBuilder();
                    incomingString.Append(incomingChars);

                    // assert that the string recieved is the same as the one sent
                    Assert.IsTrue(testString == incomingString.ToString(), incomingString.ToString());

                }, null);

                // send the test message
                sendSocket.BeginSend(testString, (e, o) => { }, null);
            }
            finally
            {
                server.Stop();
                client.Close();
            }
        }
    }
}
