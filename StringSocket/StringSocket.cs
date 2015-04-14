using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;

namespace CustomNetworking
{
    /// <summary>
    /// Delegate used for send callback
    /// </summary>
    public delegate void SendCallback(Exception e, object payload);

    /// <summary>
    /// Delegate used for receive callback
    /// </summary>
    public delegate void ReceiveCallback(String s, Exception e, object payload);

    /// <summary> 
    /// A StringSocket is a wrapper around a Socket.  It provides methods that
    /// asynchronously read lines of text (strings terminated by newlines) and 
    /// write strings. (As opposed to Sockets, which read and write raw bytes.)  
    ///
    /// StringSockets are thread safe.  This means that two or more threads may
    /// invoke methods on a shared StringSocket without restriction.  The
    /// StringSocket takes care of the synchonization.
    /// 
    /// Each StringSocket contains a Socket object that is provided by the client.  
    /// A StringSocket will work properly only if the client refains from calling
    /// the contained Socket's read and write methods.
    /// 
    /// If we have an open Socket s, we can create a StringSocket by doing
    /// 
    ///    StringSocket ss = new StringSocket(s, new UTF8Encoding());
    /// 
    /// We can write a string to the StringSocket by doing
    /// 
    ///    ss.BeginSend("Hello world", callback, payload);
    ///    
    /// where callback is a SendCallback (see below) and payload is an arbitrary object.
    /// This is a non-blocking, asynchronous operation.  When the StringSocket has 
    /// successfully written the string to the underlying Socket, or failed in the 
    /// attempt, it invokes the callback.  The parameters to the callback are a
    /// (possibly null) Exception and the payload.  If the Exception is non-null, it is
    /// the Exception that caused the send attempt to fail.
    /// 
    /// We can read a string from the StringSocket by doing
    /// 
    ///     ss.BeginReceive(callback, payload)
    ///     
    /// where callback is a ReceiveCallback (see below) and payload is an arbitrary object.
    /// This is non-blocking, asynchronous operation.  When the StringSocket has read a
    /// string of text terminated by a newline character from the underlying Socket, or
    /// failed in the attempt, it invokes the callback.  The parameters to the callback are
    /// a (possibly null) string, a (possibly null) Exception, and the payload.  Either the
    /// string or the Exception will be non-null, but nor both.  If the string is non-null, 
    /// it is the requested string (with the newline removed).  If the Exception is non-null, 
    /// it is the Exception that caused the send attempt to fail.

    /// </summary>
    public class StringSocket
    {
        // Incoming/outgoing is UTF8-encoded.  This is a multi-byte encoding.  The first 128 Unicode characters
        // (which corresponds to the old ASCII character set and contains the common keyboard characters) are
        // encoded into a single byte.  The rest of the Unicode characters can take from 2 to 4 bytes to encode.
        // private static System.Text.UTF8Encoding encoding = new System.Text.UTF8Encoding();
        private static System.Text.Encoding encoding;

        // Buffer size for reading incoming bytes
        private const int BUFFER_SIZE = 1024;

        // The socket through which we communicate with the remote client
        private Socket socket;

        // Text that has been received from the client but not yet dealt with
        private StringBuilder incoming;

        // Text that needs to be sent to the client but which we have not yet started sending
        private StringBuilder outgoing;

        // For decoding incoming UTF8-encoded byte streams.
        //  private Decoder decoder = encoding.GetDecoder();
        private Decoder decoder;

        // Buffers that will contain incoming bytes and characters
        private byte[] incomingBytes = new byte[BUFFER_SIZE];
        private char[] incomingChars = new char[BUFFER_SIZE];

        // For synchronizing sends
        private static readonly object sendSync = new object();

        // For synchronizing receives 
        private static readonly object receiveSync = new object();


        // Bytes that we are actively trying to send, along with the
        // index of the leftmost byte whose send has not yet been completed
        private byte[] pendingBytes = new byte[0];
        private int pendingIndex = 0;

        private static Queue<ReceiveObject> awaitingReceive = new Queue<ReceiveObject>();

        private static Queue<SendObject> awaitingSend = new Queue<SendObject>();

        private bool isReceiving = false;

        /// <summary>
        /// Creates a StringSocket from a regular Socket, which should already be connected.  
        /// The read and write methods of the regular Socket must not be called after the
        /// StringSocket is created.  Otherwise, the StringSocket will not behave properly.  
        /// The encoding to use to convert between raw bytes and strings is also provided.
        /// </summary>
        public StringSocket(Socket s, Encoding e)
        {
            socket = s;
            encoding = e;

            decoder = e.GetDecoder();

            incoming = new StringBuilder();
            outgoing = new StringBuilder();
        }

        /// <summary>
        /// We can write a string to a StringSocket ss by doing
        /// 
        ///    ss.BeginSend("Hello world", callback, payload);
        ///    
        /// where callback is a SendCallback (see below) and payload is an arbitrary object.
        /// This is a non-blocking, asynchronous operation.  When the StringSocket has 
        /// successfully written the string to the underlying Socket, or failed in the 
        /// attempt, it invokes the callback.  The parameters to the callback are a
        /// (possibly null) Exception and the payload.  If the Exception is non-null, it is
        /// the Exception that caused the send attempt to fail. 
        /// 
        /// This method is non-blocking.  This means that it does not wait until the string
        /// has been sent before returning.  Instead, it arranges for the string to be sent
        /// and then returns.  When the send is completed (at some time in the future), the
        /// callback is called on another thread.
        /// 
        /// This method is thread safe.  This means that multiple threads can call BeginSend
        /// on a shared socket without worrying around synchronization.  The implementation of
        /// BeginSend must take care of synchronization instead.  On a given StringSocket, each
        /// string arriving via a BeginSend method call must be sent (in its entirety) before
        /// a later arriving string can be sent.
        /// </summary>
        public void BeginSend(String s, SendCallback callback, object payload)
        {
            SendObject send = new SendObject(callback, payload);
           

            // Lock?
            lock (sendSync)
            {
                awaitingSend.Enqueue(send);
                // Lock?

                pendingBytes = encoding.GetBytes(s);
                pendingIndex = 0;
                try
                {
                    socket.BeginSend(pendingBytes, pendingIndex, pendingBytes.Length - pendingIndex, SocketFlags.None, MessageSent, null);
                }
                catch (Exception e)
                {
                    awaitingSend.Dequeue();
                    ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(x => callback(e, payload)));
                }
            }
        }

        /// <summary>
        /// Called when a message has been successfully sent
        /// </summary>
        private void MessageSent(IAsyncResult result)
        {
            // Find out how many bytes were actually sent

            try
            {
                


                // Get exclusive access to send mechanism
                lock (sendSync)
                {
                    int bytesSent = socket.EndSend(result);

                    if (bytesSent == pendingBytes.Length)
                    {
                        SendObject send = awaitingSend.Dequeue();
                        SendCallback callback = send.call;
                        object payload = send.pay;

                        ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(x => callback(null, payload)));
                    }

                    // The socket has been closed
                    else if (bytesSent == 0)
                    {
                        socket.Close();
                        Console.WriteLine("Socket closed");
                    }

                    // Update the pendingIndex and keep trying
                    else
                    {
                        pendingIndex += bytesSent;
                        SendBytes();
                    }
                }
            }
            catch (Exception e)
            {
                SendObject send = awaitingSend.Dequeue();
                SendCallback callback = send.call;
                object payload = send.pay;

                ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(x => callback(e, payload)));
                //return;
            }
        }

        /// <summary>
        /// Attempts to send the entire outgoing string.
        /// This method should not be called unless sendSync has been acquired.
        /// </summary>
        private void SendBytes()
        {
            lock (sendSync)
            {


                while (awaitingSend.Count > 0)
                {
                    try
                    {
                        socket.BeginSend(pendingBytes, pendingIndex, pendingBytes.Length - pendingIndex, SocketFlags.None, MessageSent, null);
                    }
                    catch (Exception e)
                    {
                        SendObject send = awaitingSend.Dequeue();
                        SendCallback callback = send.call;
                        object payload = send.pay;

                        ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(x => callback(e, payload)));
                        // return;
                    }
                }
            }


            // If we're in the middle of the process of sending out a block of bytes,
            // keep doing that.
            /*  if (pendingIndex < pendingBytes.Length)
              {
                  socket.BeginSend(pendingBytes, pendingIndex, pendingBytes.Length - pendingIndex,
                                   SocketFlags.None, MessageSent, null);
              }
            
              // If we're not currently dealing with a block of bytes, make a new block of bytes
              // out of outgoing and start sending that.
              else if (outgoing.Length > 0)
              {
                  pendingBytes = encoding.GetBytes(outgoing.ToString());
                  pendingIndex = 0;
                  outgoing.Clear();
                  socket.BeginSend(pendingBytes, 0, pendingBytes.Length,
                                   SocketFlags.None, MessageSent, null);
              }

              // If there's nothing to send, shut down for the time being.
              else
              {
                  sendIsOngoing = false;
              }*/
        }



        /// <summary>
        /// We can read a string from the StringSocket by doing
        /// 
        ///     ss.BeginReceive(callback, payload)
        ///     
        /// where callback is a ReceiveCallback (see below) and payload is an arbitrary object.
        /// This is non-blocking, asynchronous operation.  When the StringSocket has read a
        /// string of text terminated by a newline character from the underlying Socket, or
        /// failed in the attempt, it invokes the callback.  The parameters to the callback are
        /// a (possibly null) string, a (possibly null) Exception, and the payload.  Either the
        /// string or the Exception will be non-null, but nor both.  If the string is non-null, 
        /// it is the requested string (with the newline removed).  If the Exception is non-null, 
        /// it is the Exception that caused the send attempt to fail.
        /// 
        /// This method is non-blocking.  This means that it does not wait until a line of text
        /// has been received before returning.  Instead, it arranges for a line to be received
        /// and then returns.  When the line is actually received (at some time in the future), the
        /// callback is called on another thread.
        /// 
        /// This method is thread safe.  This means that multiple threads can call BeginReceive
        /// on a shared socket without worrying around synchronization.  The implementation of
        /// BeginReceive must take care of synchronization instead.  On a given StringSocket, each
        /// arriving line of text must be passed to callbacks in the order in which the corresponding
        /// BeginReceive call arrived.
        /// 
        /// Note that it is possible for there to be incoming bytes arriving at the underlying Socket
        /// even when there are no pending callbacks.  StringSocket implementations should refrain
        /// from buffering an unbounded number of incoming bytes beyond what is required to service
        /// the pending callbacks.
        /// </summary>
        public void BeginReceive(ReceiveCallback callback, object payload)
        {
            ReceiveObject receive = new ReceiveObject(null, callback, payload);

             lock (receiveSync)
            {
                awaitingReceive.Enqueue(receive);

                 try
                {
                    if (isReceiving == false)
                    {
                        isReceiving = true;
                        socket.BeginReceive(incomingBytes, 0, incomingBytes.Length, SocketFlags.None, MessageReceived, null);
                    }
                    else
                    {
                        return;
                    }
                }
                catch (Exception e)
                {
                    ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(x => callback(null, e, payload)));
                }
            }
        }


        /// <summary>
        /// Called when some data has been received.
        /// </summary>
        private void MessageReceived(IAsyncResult result)
        {
            lock (receiveSync)
            {
                try
                {
                    // Figure out how many bytes have come in
                    int bytesRead = socket.EndReceive(result);

                    // If no bytes were received, it means the client closed its side of the socket.
                    // Report that to the console and close our socket.
                    if (bytesRead == 0)
                    {
                        Console.WriteLine("Socket closed");
                        socket.Close();
                    }

                    // Otherwise, decode and display the incoming bytes.  Then request more bytes.
                    else
                    {
                        // Convert the bytes into characters and appending to incoming
                        int charsRead = decoder.GetChars(incomingBytes, 0, bytesRead, incomingChars, 0, false);
                        incoming.Append(incomingChars, 0, charsRead);

                        // Echo any complete lines, after capitalizing them
                        for (int i = 0; i < incoming.Length; i++)
                        {
                            if (incoming[i] == '\n')
                            {
                                String lines = incoming.ToString(0, i);
                                incoming.Remove(0, i + 1);

                                ReceiveObject received = awaitingReceive.Dequeue();
                                ReceiveCallback callback = received.call;
                                object payload = received.pay;


                                ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(x => callback(lines, null, payload)));
                            }
                        }

                        // Ask for some more data
                        try
                        {
                            if (awaitingReceive.Count != 0)
                            {
                                socket.BeginReceive(incomingBytes, 0, incomingBytes.Length,
                                    SocketFlags.None, MessageReceived, null);
                            }
                            else
                            {
                                isReceiving = false;
                            }
                        }
                        catch (Exception e)
                        {
                            ReceiveObject receive = awaitingReceive.Dequeue();
                            ReceiveCallback callback = receive.call;
                            object payload = receive.pay;

                            ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(x => callback(null, e, payload)));
                        }
                    }
                }
                catch (NullReferenceException e)
                {
                }
            }
        }


        private class SendObject
        {
            public SendCallback call { get; set; }
            public object pay { get; set; }

            public SendObject(SendCallback callback, object payload)
            {
                call = callback;
                pay = payload;
            }
        }

        private class ReceiveObject
        {
            public string toBeSent { get; set; }
            public ReceiveCallback call { get; set; }
            public object pay { get; set; }

            public ReceiveObject(String s, ReceiveCallback callback, object payload)
            {
                toBeSent = s;
                call = callback;
                pay = payload;
            }
        }
    }
}