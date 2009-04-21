//-----------------------------------------------------------------------------
// Filename: SIPTCPChannel.cs
//
// Description: SIP transport for TCP.
// 
// History:
// 19 Apr 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{  
    public class SIPTCPChannel : SIPChannel
	{
        private const int MAX_TCP_CONNECTIONS = 1000;   // Maximum number of connections for the TCP listener.

        private ILog logger = AssemblyState.logger;

        private TcpListener m_tcpServerListener;
        private bool m_closed = false;
        private Dictionary<IPEndPoint, SIPConnection> m_connectedSockets = new Dictionary<IPEndPoint, SIPConnection>();

        public SIPTCPChannel(IPEndPoint endPoint) {
            m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.tcp, endPoint);
            base.Name = "t" + Crypto.GetRandomInt(4);
            m_isReliable = true;
            Initialise();
        }

        public SIPTCPChannel(IPEndPoint endPoint, string name) {
            m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.tcp, endPoint);
            base.Name = name;
            m_isReliable = true;
            Initialise();
        }

        private void Initialise() {
            try {
                m_tcpServerListener = new TcpListener(m_localSIPEndPoint.SocketEndPoint);
                m_tcpServerListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                Thread listenThread = new Thread(new ThreadStart(AcceptConnections));
                listenThread.Start();

                logger.Debug("SIP TCP Channel listener created " + m_localSIPEndPoint.SocketEndPoint + ".");
            }
            catch (Exception excp) {
                logger.Error("Exception SIPTCPChannel Initialise. " + excp.Message);
                throw excp;
            }
        }
        				
		private void AcceptConnections()
		{
            try
            {
                //m_sipConn.Listen(MAX_TCP_CONNECTIONS);
                m_tcpServerListener.Start(MAX_TCP_CONNECTIONS);

                logger.Debug("SIPTCPChannel socket on " + m_localSIPEndPoint + " listening started.");

                while (!m_closed)
                {
                    //Socket clientSocket = m_sipConn.Accept();
                    TcpClient tcpClient = m_tcpServerListener.AcceptTcpClient();
                    tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    //clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    
                    //IPEndPoint remoteEndPoint = (IPEndPoint)clientSocket.RemoteEndPoint;
                    IPEndPoint remoteEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
                    logger.Debug("SIP TCP Channel connection accepted from " + remoteEndPoint + ".");

                    //SIPTCPConnection sipTCPClient = new SIPTCPConnection(this, clientSocket, remoteEndPoint, SIPTCPConnectionsEnum.Listener);
                    SIPConnection sipTCPClient = new SIPConnection(this, tcpClient.GetStream(), remoteEndPoint, SIPProtocolsEnum.tcp, SIPConnectionsEnum.Listener);
                    m_connectedSockets.Add(remoteEndPoint, sipTCPClient);

                    sipTCPClient.SIPSocketDisconnected += SIPTCPSocketDisconnected;
                    sipTCPClient.SIPMessageReceived += SIPTCPMessageReceived;
                   // clientSocket.BeginReceive(sipTCPClient.SocketBuffer, 0, SIPTCPConnection.MaxSIPTCPMessageSize, SocketFlags.None, new AsyncCallback(sipTCPClient.ReceiveCallback), null);
                    tcpClient.GetStream().BeginRead(sipTCPClient.SocketBuffer, 0, SIPConnection.MaxSIPTCPMessageSize, new AsyncCallback(sipTCPClient.ReceiveCallback), null);
                }

                logger.Debug("SIPTCPChannel socket on " + m_localSIPEndPoint + " listening halted.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTCPChannel Listen. " + excp.Message);
                //throw excp;
            }
		}

        private void SIPTCPSocketDisconnected(IPEndPoint remoteEndPoint)
        {
            try
            {
                logger.Debug("TCP socket from " + remoteEndPoint + " disconnected.");
                m_connectedSockets.Remove(remoteEndPoint);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTCPClientDisconnected. " + excp);
            }
        }

        private void SIPTCPMessageReceived(SIPChannel channel, SIPEndPoint remoteEndPoint, byte[] buffer)
        {
            if (SIPMessageReceived != null)
            {
                SIPMessageReceived(channel, remoteEndPoint, buffer);
            }
        }

        public override void Send(IPEndPoint destinationEndPoint, string message)
        {
            byte[] messageBuffer = Encoding.UTF8.GetBytes(message);
            Send(destinationEndPoint, messageBuffer);
        }

        public override void Send(IPEndPoint dstEndPoint, byte[] buffer)
        {
            try
            {
                if (buffer == null)
                {
                    throw new ApplicationException("An empty buffer was specified to Send in SIPTCPChannel.");
                }
                else
                {
                    bool sent = false;

                    // Lookup a client socket that is connected to the destination.
                    //m_sipConn(buffer, buffer.Length, destinationEndPoint);
                    if (m_connectedSockets.ContainsKey(dstEndPoint))
                    {
                        SIPConnection sipTCPClient = m_connectedSockets[dstEndPoint];

                        try
                        {
                            sipTCPClient.SIPStream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(EndSend), sipTCPClient);
                            sent = true;
                        }
                        catch (SocketException)
                        {
                            logger.Warn("Could not send to TCP socket " + dstEndPoint + ", closing and removing.");
                            sipTCPClient.SIPStream.Close();
                            m_connectedSockets.Remove(dstEndPoint);
                        }
                    }

                    if(!sent)
                    {
                        logger.Debug("Attempting to establish TCP connection to " + dstEndPoint + ".");
                        TcpClient tcpClient = new TcpClient();
                        tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        tcpClient.Client.Bind(m_localSIPEndPoint.SocketEndPoint);
                       
                        tcpClient.BeginConnect(dstEndPoint.Address, dstEndPoint.Port, EndConnect, new object[] { tcpClient, dstEndPoint, buffer });
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception (" + excp.GetType().ToString() + ") SIPTCPChannel Send (sendto=>" + dstEndPoint + "). " + excp.Message);
                throw excp;
            }
        }

        private void EndSend(IAsyncResult ar)
        {
            try
            {
                SIPConnection sipTCPConnection = (SIPConnection)ar.AsyncState;
                sipTCPConnection.SIPStream.EndWrite(ar);

                //logger.Debug(bytesSent + " successfully sent to TCP " + sipTCPConnection.TCPSocket.RemoteEndPoint + ".");
            }
            catch (Exception excp)
            {
                logger.Error("Exception EndSend. " + excp);
            }
        }

        public override void Send(IPEndPoint dstEndPoint, byte[] buffer, string serverCN) {
            throw new ApplicationException("This Send method is not available in the SIP TCP channel, please use an alternative overload.");
        }

        private void EndConnect(IAsyncResult ar)
        {
            try
            {
                object[] stateObj = (object[])ar.AsyncState;
                TcpClient tcpClient = (TcpClient)stateObj[0];
                IPEndPoint dstEndPoint = (IPEndPoint)stateObj[1];
                byte[] buffer = (byte[])stateObj[2];

                tcpClient.EndConnect(ar);

                if (tcpClient != null && tcpClient.Connected)
                {
                    logger.Debug("Established TCP connection to " + dstEndPoint + ".");

                    SIPConnection callerConnection = new SIPConnection(this, tcpClient.GetStream(), dstEndPoint, SIPProtocolsEnum.tcp, SIPConnectionsEnum.Caller);
                    m_connectedSockets.Add(dstEndPoint, callerConnection);
                    
                    callerConnection.SIPSocketDisconnected += SIPTCPSocketDisconnected;
                    callerConnection.SIPMessageReceived += SIPTCPMessageReceived;
                    callerConnection.SIPStream.BeginRead(callerConnection.SocketBuffer, 0, SIPConnection.MaxSIPTCPMessageSize, new AsyncCallback(callerConnection.ReceiveCallback), null);
                    callerConnection.SIPStream.BeginWrite(buffer, 0, buffer.Length, EndSend, callerConnection);
                }
                else
                {
                    logger.Warn("Could not establish TCP connection to " + dstEndPoint + ".");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTCPChannel EndConnect. " + excp);
                throw;
            }
        }

        public override void Close()
        {
            logger.Debug("Closing SIP TCP Channel " + SIPChannelEndPoint + ".");

            m_closed = true;

            try
            {
                m_tcpServerListener.Stop();
            }
            catch (Exception listenerCloseExcp)
            {
                logger.Warn("Exception SIPTCPChannel Close (shutting down listener). " + listenerCloseExcp.Message);
            }

            foreach (SIPConnection tcpConnection in m_connectedSockets.Values)
            {
                try
                {
                    tcpConnection.SIPStream.Close();
                }
                catch (Exception connectionCloseExcp)
                {
                    logger.Warn("Exception SIPTCPChannel Close (shutting down connection to " + tcpConnection.RemoteEndPoint + "). " + connectionCloseExcp.Message);
                }
            }
        }

        private void Dispose(bool disposing)
        {
            try
            {
                this.Close();
            }
            catch (Exception excp)
            {
                logger.Error("Exception Disposing SIPTCPChannel. " + excp.Message);
            }
        }

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPRequestUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");
			}
		}

		#endif

		#endregion
	}
}
 