﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OrbisFTP
{
    public class Server
    {
        #region VARIABLES
        /// <summary>
        /// The client list, containing each object for every client connected.
        /// </summary>
        public List<Client> Clients { get; set; }

        /// <summary>
        /// TcpListener listening for every incoming connection.
        /// </summary>
        private static TcpListener TcpListener = null;

        /// <summary>
        /// A thread taking care of every incoming connection.
        /// </summary>
        private Thread ListenerThread = null;

        /// <summary>
        /// The server settings.
        /// </summary>
        public Settings Settings { get; set; }

        public DirectoryInfo DirInfo { get; set; }

        #endregion

        #region EVENTS
        public event EventHandler<ClientJoinedArgs> ClientJoined;

        protected virtual void OnClientJoin(ClientJoinedArgs e)
        {
            EventHandler<ClientJoinedArgs> handler = ClientJoined;

            if (handler != null)
            {
                handler(this, e);
            }
        }
        #endregion

        public Server(int port = 21, string configFilePath = "ftp.conf")
        {
            TcpListener = new TcpListener(IPAddress.Parse("127.0.0.1"), port);

            if (!File.Exists("ftp.conf"))
            {
                Settings = new Settings();

                if (!Directory.Exists(Settings.BaseDirectory))
                {
                    DirInfo = Directory.CreateDirectory(Settings.BaseDirectory);
                }
                else
                {
                    DirInfo = new DirectoryInfo(Settings.BaseDirectory);
                }
            }
        }

        /// <summary>
        /// Calling this function starts the TCP listener, and allows FTP Connections to be made.
        /// </summary>
        public void Start()
        {
            // Init clients list.
            Clients = new List<Client>();

            TcpListener.Start();

            ListenerThread = new Thread(() =>
            {
                Log("Started FTP Server, listening for incoming connections.", "Log");

                while (true)
                {
                    var newClient = new Client(this, TcpListener.AcceptTcpClient());

                    Clients.Add(newClient);

                    ClientJoinedArgs args = new ClientJoinedArgs()
                    {
                        NewClient = newClient
                    };

                    OnClientJoin(args);
                }
            });

            ListenerThread.Name = "TCP Listener thread.";
            ListenerThread.Start();

            Log("Started TCP Listener thread.", "Log");
        }

        private void Log(string message, string header)
        {
            Debug.WriteLine(String.Format("[{0}] {1}", header, message));
        }
    }

    public class ClientJoinedArgs : EventArgs
    {
        public Client NewClient { get; set; }
    }
}
