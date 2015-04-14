using CustomNetworking;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading.Tasks;

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

        /// <summary>
        ///     Tests sending 500 strings with BeginSends that are issued in parallel, and receiving the strings with 500 BeginReceives that are created in parallel.
        /// </summary>
        [TestMethod]
        public void StressTest01()
        {
            // These are declared here so that they are accessible in the 'finally' block.
            TcpListener server = null;
            TcpClient client = null;
            try
            {
                // Obtains a pair of sockets.
                const int port = 5000;
                server = new TcpListener(IPAddress.Any, port);
                server.Start();
                client = new TcpClient("localhost", port);
                Socket serverSocket = server.AcceptSocket();
                Socket clientSocket = client.Client;

                // Creates a pair of StringSockets from the serverSocket and clientSocket.
                StringSocket sendSocket = new StringSocket(clientSocket, new UTF8Encoding());
                StringSocket receiveSocket = new StringSocket(serverSocket, new UTF8Encoding());

                // Sync objects which will be used to lock access to the variables inside the Asserters.
                object receiveAsserterSync = new object();
                object sendAsserterSync = new object();

                // Sets up the Asserters, which contain SortedSets of strings.
                Asserter receiveAsserter = new Asserter
                {
                    Expected = new SortedSet<string>(),
                    Actual = new SortedSet<string>()
                };
                Asserter sendAsserter = new Asserter
                {
                    Expected = new SortedSet<string>(),
                    Actual = new SortedSet<string>()
                };

                // Creates 500 test strings.
                const int testStringsCount = 10;
                string[] testStrings = new string[testStringsCount];
                for (int i = 1; i < testStringsCount + 1; ++i)
                {
                    testStrings[i - 1] = i.ToString();
                }

                // Adds all 500 strings from the testStrings to the Expected set in the Asserters (and asserts that there are in fact 500 strings added to each).
                foreach (string testString in testStrings)
                {
                    ((SortedSet<string>)receiveAsserter.Expected).Add(testString);
                    ((SortedSet<string>)sendAsserter.Expected).Add(testString);
                }
                Assert.AreEqual(testStringsCount, ((SortedSet<string>)receiveAsserter.Expected).Count);
                Assert.AreEqual(testStringsCount, ((SortedSet<string>)sendAsserter.Expected).Count);

                // Fires 500 BeginReceive requests on many different threads. Each request's callback adds the received string to the Actual set in the receiveAsserter.
                Parallel.ForEach(Partitioner.Create(0, testStringsCount),
                    range =>
                    {
                        for (int i = range.Item1; i < range.Item2; ++i)
                        {
                            receiveSocket.BeginReceive((s, e, p) =>
                            {
                                lock (receiveAsserterSync)
                                {
                                    // Adds the received strings to the receiveAsserter's Actual set, which will test that each string was correctly received.
                                    ((SortedSet<string>)receiveAsserter.Actual).Add(s);

                                    // Once the receiveAsserter's set of Actual strings contains as many strings as testStrings, the receiveAsserter's Set method is called.
                                    if (((SortedSet<string>)receiveAsserter.Actual).Count == testStringsCount)
                                    {
                                        receiveAsserter.Set();
                                    }
                                }
                            }, null);
                        }
                    });

                // Fires 500 BeginSend requests on many different threads. Each request's callback adds the sent string to the Actual set in the sendAsserter.
                Parallel.ForEach(Partitioner.Create(0, testStringsCount),
                    range =>
                    {
                        for (int i = range.Item1; i < range.Item2; ++i)
                        {
                            sendSocket.BeginSend(testStrings[i] + "\n", (e, p) =>
                            {
                                lock (sendAsserterSync)
                                {
                                    // Adds the payload string to the sendAsserter's Actual set, which will test that the callbacks were executed properly.
                                    ((SortedSet<string>)sendAsserter.Actual).Add((string)p);

                                    // Once the sendAsserter's set of Actual strings contains as many strings as testStrings, the sendAsserter's Set method is called.
                                    if (((SortedSet<string>)sendAsserter.Actual).Count == testStringsCount)
                                    {
                                        sendAsserter.Set();
                                    }
                                }
                            }, testStrings[i]);
                        }
                    });

                // Asserts that the Expected and Actual sets in both Asserters are equal, and that the entire operation completed in under 5 seconds.
                sendAsserter.WaitAreEqualSortedStringSets(5000);
                receiveAsserter.WaitAreEqualSortedStringSets(5000);
            }
            finally
            {
                // Closes down the server and the client, assuming they were created.
                if (server != null)
                {
                    server.Stop();
                }
                if (client != null)
                {
                    client.Close();
                }
            }
        }

        /// <summary>
        /// Executes two seperate String Sockets simultaneously on seperate threads
        /// </summary>
        [TestMethod]
        public void MattsStressTest()
        {
            // Create and start a server and client.
            TcpListener server = null;
            TcpClient client = null;

            TcpListener server2 = null;
            TcpClient client2 = null;

            try
            {
                int port = 5502; //Matts favorite number
                server = new TcpListener(IPAddress.Any, port);
                server.Start();
                client = new TcpClient("localhost", port);

                int port2 = 5503; //Matts 2 favorite number
                server2 = new TcpListener(IPAddress.Any, port2);
                server2.Start();
                client2 = new TcpClient("localhost", port2);

                // Obtain the sockets from the two ends of the connection.  We are using the blocking AcceptSocket()
                // method here, which is OK for a test case.
                Socket serverSocket = server.AcceptSocket();
                Socket clientSocket = client.Client;

                Socket serverSocket2 = server2.AcceptSocket();
                Socket clientSocket2 = client2.Client;


                // Wrap the two ends of the connection into StringSockets
                StringSocket sendSocket = new StringSocket(serverSocket, new UTF8Encoding());
                StringSocket receiveSocket = new StringSocket(clientSocket, new UTF8Encoding());

                StringSocket sendSocket2 = new StringSocket(serverSocket2, new UTF8Encoding());
                StringSocket receiveSocket2 = new StringSocket(clientSocket2, new UTF8Encoding());

                //Expected Queue to be sent into Send methods
                Queue<Tuple<string, object>> expectedSent = new Queue<Tuple<string, object>>();
                Queue<Tuple<string, object>> expectedSent2 = new Queue<Tuple<string, object>>();


                Task first = Task.Factory.StartNew(() =>
                {
                    //Start 100 recieve requests
                    for (int i = 0; i < 100; i++)
                        Receieve(receiveSocket, i);

                    //Send 100 different lines
                    for (int i = 0; i < 100; i++)
                        Send(sendSocket, "Test" + i + "/n", i, expectedSent);

                    //Assert
                    for (int i = 0; i < 100; i++)
                    {
                        string test1 = "Test" + i;
                        Assert.AreEqual(expectedSent.Dequeue(), actualSent.Dequeue());
                        Assert.AreEqual(sc1.Dequeue(), i);
                    }

                });

                Task second = Task.Factory.StartNew(() =>
                {
                    //Start 100 recieve requests
                    for (int i = 0; i < 100; i++)
                        Receieve2(receiveSocket2, i);

                    //Send 100 different lines
                    for (int i = 0; i < 100; i++)
                        Send2(sendSocket2, "TestTwo" + i + "/n", i, expectedSent2);

                    //Assert
                    for (int i = 0; i < 100; i++)
                    {
                        string test2 = "TestTwo" + i;
                        Assert.AreEqual(expectedSent2.Dequeue(), actualSent2.Dequeue());
                        Assert.AreEqual(sc2.Dequeue(), i);
                    }
                });
                Thread.Sleep(4000);
            }
            finally
            {
                server.Stop();
                client.Close();
                server2.Stop();
                client2.Close();
            }
        }
        //Calls string socket BeginSend method and adds the string to the 
        //expected sent queue for testing
        private void Send(StringSocket socket, string s, object payload, Queue<Tuple<string, object>> expectedSent)
        {
            socket.BeginSend(s, sendCalback, payload);
            expectedSent.Enqueue(new Tuple<string, object>(s, payload));
        }


        //Populates the queue with expected payloads
        private Queue<object> sc1 = new Queue<object>();
        private void sendCalback(Exception e, Object payload)
        {
            sc1.Enqueue(payload);
        }

        //Calls string socket BeginSend method and adds the string to the 
        //expected sent queue for testing
        private void Send2(StringSocket socket, string s, object payload, Queue<Tuple<string, object>> expectedSent)
        {
            socket.BeginSend(s, sendCalback2, payload);
            expectedSent.Enqueue(new Tuple<string, object>(s, payload));
        }

        //Populates the queue with expected payloads
        private Queue<object> sc2 = new Queue<object>();
        private void sendCalback2(Exception e, Object payload)
        {
            sc2.Enqueue(payload);
        }

        //calls the socket BeginReceive method using the delegate actualCallback to populate
        //the actual sent strings
        private void Receieve(StringSocket receiveSocket, object payload)
        {
            receiveSocket.BeginReceive(actualCallback, payload);
        }

        private Queue<Tuple<string, object>> actualSent = new Queue<Tuple<string, object>>();
        //Delegate for Recieve method
        private void actualCallback(String s, Exception e, Object payload)
        {
            actualSent.Enqueue(new Tuple<string, object>(s, payload));
        }

        //calls the socket BeginReceive method using the delegate actualCallback to populate
        //the actual sent strings
        private void Receieve2(StringSocket receiveSocket, object payload)
        {
            receiveSocket.BeginReceive(actualCallback2, payload);
        }

        private Queue<Tuple<string, object>> actualSent2 = new Queue<Tuple<string, object>>();
        //Delegate for Recieve method
        private void actualCallback2(String s, Exception e, Object payload)
        {
            actualSent2.Enqueue(new Tuple<string, object>(s, payload));
        }

        [TestMethod]
        public void ATHBTest()
        {
            // Declare these here so they are accessible in
            // the finally block
            TcpListener server = null;
            TcpClient client = null;
            try
            {
                // Obtain a pair of sockets
                int port = 4003;
                server = new TcpListener(IPAddress.Any, port);
                server.Start();
                client = new TcpClient("localhost", port);
                Socket serverSocket = server.AcceptSocket();
                Socket clientSocket = client.Client;
                // Create a pair of StringSocket 
                StringSocket sendSocket = new StringSocket(clientSocket, new UTF8Encoding());
                StringSocket rcvSocket = new StringSocket(serverSocket, new UTF8Encoding());
                string String1 = MakeLongString("aaaa");
                string String2 = MakeLongString("bbbb");
                string String3 = MakeLongString("cccc");
                Asserter rcvAsserter = new Asserter();
                rcvSocket.BeginReceive((s, e, p) =>
                {
                    rcvAsserter.Expected = String1;
                    rcvAsserter.Actual = s;
                    rcvAsserter.Set();
                },
                "");
                Asserter rcvAsserterB = new Asserter();
                rcvSocket.BeginReceive((s, e, p) =>
                {
                    rcvAsserterB.Expected = String2;
                    rcvAsserterB.Actual = s;
                    rcvAsserterB.Set();
                },
                "");
                Asserter rcvAsserterC = new Asserter();
                rcvSocket.BeginReceive((s, e, p) =>
                {
                    rcvAsserterC.Expected = String3;
                    rcvAsserterC.Actual = s;
                    rcvAsserterC.Set();
                },
                "");
                // Send a "Hello" and make sure the callback is called
                Asserter sendAsserter = new Asserter();
                sendSocket.BeginSend(
                String1,
                (e, p) =>
                {
                    sendAsserter.Expected = "Payload_a";
                    sendAsserter.Actual = p;
                    sendAsserter.Set();
                },
                "Payload_a");
                Asserter sendAsserterB = new Asserter();
                sendSocket.BeginSend(
                String1,
                (e, p) =>
                {
                    sendAsserterB.Expected = "Payload_b";
                    sendAsserterB.Actual = p;
                    sendAsserterB.Set();
                },
                "Payload_b");
                Asserter sendAsserterC = new Asserter();
                sendSocket.BeginSend(
                String1,
                (e, p) =>
                {
                    sendAsserterC.Expected = "Payload_c";
                    sendAsserterC.Actual = p;
                    sendAsserterC.Set();
                },
                "Payload_c");

                // Perform the assertions in the main testing thread
                sendAsserter.WaitAreEqual(2000);
                sendAsserter.WaitAreEqual(2000);
                sendAsserter.WaitAreEqual(2000);
            }
            catch (Exception e)
            {
            }
        }
        private string MakeLongString(string p)
        {
            StringBuilder string1 = new StringBuilder();
            for (int i = 0; i < 300; i++)
            {
                string1.Append(p);
            }
            return string1.ToString();
        }

        [TestMethod]
        public void BSTest()
        {

            TcpListener[] servers = new TcpListener[100];//because 100 is an awesome number
            TcpClient[] clients = new TcpClient[100];
            try
            {
                for (int a = 0; a < 100; a++)
                {
                    int b = a;
                    servers[b] = new TcpListener(IPAddress.Any, 5000 + b);
                    servers[b].Start();
                    clients[b] = new TcpClient("localhost", 5000 + b);
                }
                Socket[] sSockets = new Socket[100];
                Socket[] cSockets = new Socket[100];
                for (int a = 0; a < 100; a++)
                {
                    int b = a;
                    sSockets[b] = servers[b].AcceptSocket();
                    cSockets[b] = clients[b].Client;

                }

                StringSocket[] sendSockets = new StringSocket[100];
                StringSocket[] receiveSockets = new StringSocket[100];
                for (int a = 0; a < 100; a++)
                {
                    int b = a;
                    sendSockets[b] = new StringSocket(sSockets[b], new UTF8Encoding());
                    receiveSockets[b] = new StringSocket(cSockets[b], new UTF8Encoding());
                }

                object[] receiveAsserterSync = new object[100];
                object[] sendAsserterSync = new object[100];
                for (int a = 0; a < 100; a++)
                {
                    int b = a;
                    receiveAsserterSync[b] = new object();
                    sendAsserterSync[b] = new object();
                }
                Asserter[] receiveAsserter = new Asserter[100];
                Asserter[] sendAsserter = new Asserter[100];
                for (int a = 0; a < 100; a++)
                {
                    int b = a;
                    receiveAsserter[b] = new Asserter
                    {
                        Expected = new SortedSet<string>(),
                        Actual = new SortedSet<string>()
                    };
                    sendAsserter[b] = new Asserter
                    {
                        Expected = new SortedSet<string>(),
                        Actual = new SortedSet<string>()
                    };
                }

                // Creates 5000 test strings.
                const int testStringsCount = 5000;
                string[] testStrings = new string[testStringsCount];
                for (int i = 1; i < testStringsCount + 1; ++i)
                {
                    testStrings[i - 1] = i.ToString();

                }
                for (int a = 0; a < 100; a++)
                {
                    int b = a;
                    for (int c = 0; c < 50; c++)
                    {
                        int d = c;
                        ((SortedSet<string>)receiveAsserter[b].Expected).Add(testStrings[50 * b + d]);
                        ((SortedSet<string>)sendAsserter[b].Expected).Add(testStrings[50 * b + d]);
                    }

                    Assert.AreEqual(50, ((SortedSet<string>)receiveAsserter[b].Expected).Count);
                    Assert.AreEqual(50, ((SortedSet<string>)sendAsserter[b].Expected).Count);
                }

                Task[] tasks = new Task[5000];
                // Fires 500 BeginReceive requests on many different threads. Each request's callback adds the received string to the Actual set in the receiveAsserter.
                for (int a = 0; a < 100; a++)
                {
                    int b = a;
                    for (int c = 0; c < 50; c++)
                    {
                        int d = c;
                        tasks[50 * b + d] = Task.Run(() =>
                        {
                            receiveSockets[b].BeginReceive((s, e, p) =>
                            {
                                lock (receiveAsserterSync[b])
                                {
                                    // Adds the received strings to the receiveAsserter's Actual set, which will test that each string was correctly received.
                                    ((SortedSet<string>)receiveAsserter[b].Actual).Add(s);

                                    // Once the receiveAsserter's set of Actual strings contains as many strings as testStrings, the receiveAsserter's Set method is called.
                                    if (((SortedSet<string>)receiveAsserter[b].Actual).Count == 5)
                                    {
                                        receiveAsserter[b].Set();
                                    }
                                }
                            }, null);
                        });
                    }

                }
                Task.WaitAll(tasks);
                Task[] tasks1 = new Task[5000];
                for (int a = 0; a < 100; a++)
                {
                    int b = a;
                    for (int c = 0; c < 50; c++)
                    {
                        int d = c;
                        tasks1[50 * b + d] = Task.Run(() =>
                        {
                            sendSockets[b].BeginSend(testStrings[50 * b + d] + "\n", (e, p) =>
                            {
                                lock (sendAsserterSync[b])
                                {
                                    ((SortedSet<string>)sendAsserter[b].Actual).Add((string)p);
                                    if (((SortedSet<string>)sendAsserter[b].Actual).Count == 5)
                                    {
                                        sendAsserter[b].Set();
                                    }
                                }
                            }, testStrings[50 * b + d]);
                        });
                    }
                }

                Task.WaitAll(tasks1);
                Thread.Sleep(10000);
                for (int a = 0; a < 100; a++)
                {
                    int b = a;
                    sendAsserter[b].WaitAreEqualSortedStringSets(5000);
                    receiveAsserter[b].WaitAreEqualSortedStringSets(5000);
                }
            }
            finally
            {
                foreach (TcpListener l in servers)
                {
                    l.Stop();
                }
                foreach (TcpClient c in clients)
                {
                    c.Close();
                }
            }
        }

        }

    
}
