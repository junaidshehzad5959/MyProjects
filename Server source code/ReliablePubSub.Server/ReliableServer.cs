﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetMQ;
using NetMQ.Sockets;

namespace ReliablePubSub.Server
{
    class ReliableServer
    {
        private readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);
        private const string PublishMessageCommand = "P";
        private const string WelcomeMessage = "WM";
        private const string HeartbeatMessage = "HB";

        private readonly NetMQContext m_context;
        private string m_address;        
        private NetMQActor m_actor;                
        private XPublisherSocket m_publisherSocket;
        private NetMQTimer m_heartbeatTimer;
        private Poller m_poller;          

        public ReliableServer(NetMQContext context, string address)
        {
            m_context = context;
            m_address = address;            

            // actor is like thread with builtin pair sockets connect the user thread with the actor thread
            m_actor = NetMQActor.Create(context, Run);
        }

        public void Dispose()
        {
            m_actor.Dispose();
        }

        private void Run(PairSocket shim)
        {
            using (m_publisherSocket = m_context.CreateXPublisherSocket())
            {
                m_publisherSocket.SetWelcomeMessage(WelcomeMessage);
                m_publisherSocket.Bind(m_address);

                m_publisherSocket.ReceiveReady += DropPublisherSubscriptions;

                m_heartbeatTimer = new NetMQTimer(HeartbeatInterval);
                m_heartbeatTimer.Elapsed += OnHeartbeatTimerElapsed;

                shim.ReceiveReady += OnShimMessage;

                // signal the actor that the shim is ready to work
                shim.SignalOK();

                m_poller = new Poller(m_publisherSocket, shim);
                m_poller.AddTimer(m_heartbeatTimer);

                // Polling until poller is cancelled
                m_poller.PollTillCancelled();
            }                           
        }

        private void OnHeartbeatTimerElapsed(object sender, NetMQTimerEventArgs e)
        {
            // Heartbeat timer elapsed, let's send another heartbeat
            m_publisherSocket.SendFrame(HeartbeatMessage);
        }

        private void OnShimMessage(object sender, NetMQSocketEventArgs e)
        {
            string command = e.Socket.ReceiveFrameString();

            if (command == PublishMessageCommand)
            {
                // just forward the message to the publisher
                NetMQMessage message = e.Socket.ReceiveMultipartMessage();
                m_publisherSocket.SendMultipartMessage(message);
            }
            else if (command == NetMQActor.EndShimMessage)
            {
                // we got dispose command, we just stop the poller
                m_poller.Cancel();
            }
        }

        private void DropPublisherSubscriptions(object sender, NetMQSocketEventArgs e)
        {
            // just drop the subscription messages, we have to do that to Welcome message to work
            m_publisherSocket.SkipMultipartMessage();
        }


        public void Publish(NetMQMessage message)
        {
            // we can use actor like NetMQSocket
            m_actor.SendMoreFrame(PublishMessageCommand).SendMultipartMessage(message);
        }        
    }
}
