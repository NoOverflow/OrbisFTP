using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OrbisFTP
{
    
    /// <summary>
    /// A class representing a FTP Client.
    /// </summary>
    public class Client
    {
        #region VARIABLES
        public TcpClient TcpClient;

        /// <summary>
        /// StreamReader used for client to server communication.
        /// </summary>
        private StreamReader FtpStreamReader;

        /// <summary>
        /// StreamWriter used for server to client communication.
        /// </summary>
        private StreamWriter FtpStreamWriter;

        /// <summary>
        /// Thread taking care of treating messages from client.
        /// </summary>
        private Thread MessageLoopThread;

        /// <summary>
        /// Reference to the FTP Server.
        /// </summary>
        private Server Server;

        /// <summary>
        /// Actual username of the client. Warning : May be different than the username being used for an ongoing transfer (refer to section 4.1.1)
        /// </summary>
        private string Username;

        private DirectoryInfo WorkingDirectory;

        private FtpTransferType TransferType;

        private (IPAddress, int) NextFileTransferEndPoint;

        private DataConnectionType ConnectionType;

        private bool IsLoggedIn {get; set;}
        #endregion

        public Client(Server Server, TcpClient TcpClient)
        {
            this.TcpClient = TcpClient;
            this.Server = Server;
            this.WorkingDirectory = Server.DirInfo;

            this.FtpStreamReader = new StreamReader(this.TcpClient.GetStream(), Encoding.ASCII);
            this.FtpStreamWriter = new StreamWriter(this.TcpClient.GetStream(), Encoding.ASCII);

            this.MessageLoopThread = new Thread(MessageLoop);
            this.MessageLoopThread.Name = "Message Loop Thread.";
            this.MessageLoopThread.Start();

            Log(String.Format("New client connected, IP : {0}", TcpClient.Client.RemoteEndPoint.ToString()), "Log");
        }

        public void MessageLoop()
        {
            SendCommand("220 Service Ready");

            while (TcpClient.Connected)
            {
                try
                {
                    string receivedCommand = FtpStreamReader.ReadLine();
                    string[] receivedCommandSplit = receivedCommand.Split(' ');

                    string commandHeader = receivedCommandSplit[0];
                    string commandArguments = receivedCommandSplit.Length > 1 ? receivedCommand.Substring(receivedCommandSplit[0].Length + 1) : null;

                    if (String.IsNullOrEmpty(receivedCommand))
                        Disconnect();

                    string response = "";

                    Log(String.Format("Got command from {0} : {1}", TcpClient.Client.RemoteEndPoint.ToString(), receivedCommand), "Log");

                    switch (commandHeader)
                    {
                        case "USER":
                            SendCommand(User(commandArguments));
                            break;
                        case "PASS":
                            SendCommand(Password(commandArguments));
                            break;
                        case "PWD":
                            SendCommand(PrintWorkingDirectory());
                            break;
                        case "SYST":
                            SendCommand(Syst());
                            break;
                        case "LIST":
                            SendCommand(List(commandArguments));
                            break;
                        case "TYPE":
                            SendCommand(Type(commandArguments));
                            break;
                        case "PORT":
                            SendCommand(Port(commandArguments));
                            break;
                        case "RETR":
                            SendCommand(Return(WorkingDirectory.FullName + "/" + commandArguments));
                            break;
                        default:
                            SendCommand("202 Command not implemented");
                               break;
                    }
                }
                catch (Exception)
                {
                    Log("Failed to parse command from " + TcpClient.Client.RemoteEndPoint.ToString() + ", closing connection.", "Error");                  
                    TcpClient.Close();
                }
            }
        }
 
        

        public void Disconnect()
        {

        }

        public void SendCommand(string command)
        {
            Log(String.Format("Sent Command : {0}", command), "FTP");

            FtpStreamWriter.WriteLine(command);
            FtpStreamWriter.Flush();
        }

        public void Log(string message, string header)
        {
            Console.WriteLine(String.Format("[{0}] {1}", header, message));
        }

        #region FTP_COMMANDS
        /// <summary>
        /// Set the local username of the client.
        /// </summary>
        /// <param name="username">The username provided by the client.</param>
        /// <returns>Answer to be sent to the client.</returns>
        public string User(string username)
        {
            this.Username = username;

            if (Server.Settings.NeedAuth)
            {
                IsLoggedIn = false; // We need to reset the IsLoggedIn state, if we don't, you could modify username while staying "logged in".
                return "331 Username ok, need password"; // From RFC958, Section 7.
            }
            else
            {
                return "230 User logged in"; // From RFC958, Section 7.
            }
        }

        /// <summary>
        /// List file(s) specified by directory. NOTE : RFC is poor on output, so used this : https://files.stairways.com/other/ftp-list-specs-info.txt
        /// </summary>
        /// <param name="specifiedDirectory"></param>
        /// <returns></returns>
        public string List(string specifiedDirectory = "")
        {
            string dir = String.IsNullOrEmpty(specifiedDirectory) ? WorkingDirectory.FullName : specifiedDirectory;

            string returnValues = "";

            FileAttributes attr = File.GetAttributes(dir);

            returnValues += "200 OK" + Environment.NewLine;

            if (attr.HasFlag(FileAttributes.Directory))
            {
                if (!Directory.Exists(dir))
                    return "550 Requested action not taken. File unavailable";

                foreach (var directory in Directory.GetDirectories(dir))
                {
                    returnValues += BuildListOutput(directory, true) + Environment.NewLine;
                }

                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    returnValues += BuildListOutput(file, false) + Environment.NewLine;
                }
            }
            else
            {
                if (!File.Exists(dir))
                    return "550 Requested action not taken. File unavailable";
                returnValues += BuildListOutput(dir, false) + Environment.NewLine;
            }

            // Any LIST command is preceded by a PORT or a PASV command.
            if (ConnectionType == DataConnectionType.Active)
            {
                TcpClient listSender = new TcpClient();
                listSender.Connect(NextFileTransferEndPoint.Item1, NextFileTransferEndPoint.Item2);
                using(StreamWriter writer = new StreamWriter(listSender.GetStream(), Encoding.ASCII))
                {
                    writer.Write(returnValues);
                    writer.Flush();
                }
                listSender.Close();
            }

            return "226 Transfer complete";
        } 

        /// <summary>
        /// Log in the user based on the password he sent.
        /// </summary>
        /// <param name="password">Password provided by the client.</param>
        /// <returns>Answer to be sent to the client.</returns>
        public string Password(string password)
        {
            string expectedPassword = "";

            try
            {
                expectedPassword = Server.Settings.UserPasswords[Username];
            }
            catch (Exception)
            {
                // Couldn't find matching username.
                return "430 Invalid username or password";
            }

            if(expectedPassword != password)
                return "430 Invalid username or password";

            return "230 User logged in";
        }

        /// <summary>
        /// Start Passive mode in which the server waits for the client to tcp-connect to a server provided endpoint.
        /// </summary>
        /// <param name="commandArgs"></param>
        /// <returns></returns>
        public string Pasv(string commandArgs)
        {
            if (String.IsNullOrEmpty(commandArgs))
                return "501 Syntax error in parameters or arguments";

            return "202";
        }

        /// <summary>
        /// Set the next endpoint to be used in a file transfer.
        /// </summary>
        /// <param name="commandArgs"></param>
        /// <returns></returns>
        public string Port(string commandArgs)
        {
            if (String.IsNullOrEmpty(commandArgs))
                return "501 Syntax error in parameters or arguments";

            string[] properties = commandArgs.Split(',');

            try
            {
                var ip = IPAddress.Parse(properties[0] + "." + properties[1] + "." + properties[2] + "." + properties[3]);
                var port = Convert.ToInt32(properties[4]) * 256 + Convert.ToInt32(properties[5]);

                NextFileTransferEndPoint = (ip, port);

                ConnectionType = DataConnectionType.Active;

                return "200 OK";
            }
            catch (Exception)
            {
                return "501 Syntax error in parameters or arguments";
            }            
        }

        /// <summary>
        /// Starts a file transfer.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public string Return(string filePath)
        {
            if (String.IsNullOrEmpty(filePath))
                return "501 Syntax error in parameters or arguments";

            if (!File.Exists(filePath) || filePath.Contains(".."))
                return "550 File Not Found";

            SendCommand("150 File status okay; about to open data connection"); 

            if (ConnectionType == DataConnectionType.Active)
            {
                TcpClient listSender = new TcpClient();
                listSender.Connect(NextFileTransferEndPoint.Item1, NextFileTransferEndPoint.Item2);
                
                if(TransferType == FtpTransferType.A)
                {
                    using (StreamWriter writer = new StreamWriter(listSender.GetStream(), Encoding.ASCII))
                    {
                        try
                        {
                            using (StreamReader reader = new StreamReader(filePath, Encoding.ASCII))
                            {
                                writer.Write(reader.ReadToEnd());
                            }
                        }
                        catch (Exception ex)
                        {
                            if (ex is NotSupportedException || ex is IOException)
                                return "452 Requested action not taken. Insufficient storage space in system.File unavailable";
                            else
                                return "426 Connection closed; transfer aborted";
                        }
                    }
                }
                else if(TransferType == FtpTransferType.I)
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(filePath);

                        listSender.GetStream().Write(bytes, 0, bytes.Length);
                        listSender.GetStream().Flush();
                        listSender.Close();
                    }
                    catch (Exception ex)
                    {
                        if(ex is NotSupportedException || ex is IOException)
                            return "452 Requested action not taken. Insufficient storage space in system.File unavailable";
                        else
                            return "426 Connection closed; transfer aborted";
                    }                    
                }

                listSender.Close();
            }

            return "226 Closing data connection. Requested file action successful";
        }

        /// <summary>
        /// A Command used to query information about the hosting OS, can be disabled in the FTP settings.
        /// </summary>
        /// <returns></returns>
        public string Syst()
        {
            return Server.Settings.AllowSYST ? "215 Windows_NT" : "202 Command not implemented";
        }

        public string PrintWorkingDirectory()
        {
            return String.Format("257 {0} is current directory", this.WorkingDirectory.FullName.Replace(Server.DirInfo.FullName, "") + "/");
        }

        /// <summary>
        /// Set the FTP Byte transfer type.
        /// </summary>
        /// <param name="requestedType">The requested type by the user.</param>
        /// <returns></returns>
        public string Type(string requestedType)
        {
            switch (requestedType)
            {
                case "I":
                    TransferType = FtpTransferType.I;
                    return "200 OK";
                case "A":
                    TransferType = FtpTransferType.A;
                    return "200 OK";
                default:
                    return "504 Command not implemented for that parameter";
            }
        }

        #endregion
        
        /// <summary>
        /// Function used to build the output for the LIST command.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="isDirectory"></param>
        /// <returns></returns>
        public string BuildListOutput(string path, bool isDirectory)
        {
            if (isDirectory)
            {
                var acl = Directory.GetAccessControl(path);
                var directoryInfo = new DirectoryInfo(path);
                string fileAccessDesc = "";
                WindowsIdentity currentuser = WindowsIdentity.GetCurrent();
                var domainAndUser = currentuser.Name;
                DirectorySecurity dirAC = directoryInfo.GetAccessControl(AccessControlSections.All);
                AuthorizationRuleCollection rules = dirAC.GetAccessRules(true, true, typeof(NTAccount));
                int attributesNumber = 0;

                foreach (AuthorizationRule rule in rules)
                {
                    if (rule.IdentityReference.Value.Equals(domainAndUser, StringComparison.CurrentCultureIgnoreCase))
                    {
                        attributesNumber |= (int)(((FileSystemAccessRule)rule).FileSystemRights);
                    }
                }


                return (isDirectory ? "d" : "-") + (fileAccessDesc).PadRight(9, '-') + " " + (attributesNumber) + " " + acl.GetOwner(typeof(System.Security.Principal.NTAccount)).ToString() + " " + "999" + " " + directoryInfo.CreationTime.ToString("MMM DD hh:mm") + " " + directoryInfo.Name + "/";
            }
            else
            {
                var acl = File.GetAccessControl(path);
                var attributes = File.GetAttributes(path);
                var fileInfo = new FileInfo(path);
                string fileAccessDesc = "";
                WindowsIdentity currentuser = WindowsIdentity.GetCurrent();
                var domainAndUser = currentuser.Name;

                
                int attributesNumber = 0;

                try
                {
                    FileSecurity dirAC = fileInfo.GetAccessControl(AccessControlSections.All);
                    AuthorizationRuleCollection rules = dirAC.GetAccessRules(true, true, typeof(NTAccount));
                    foreach (AuthorizationRule rule in rules)
                    {
                        if (rule.IdentityReference.Value.Equals(domainAndUser, StringComparison.CurrentCultureIgnoreCase))
                        {
                            attributesNumber |= (int)(((FileSystemAccessRule)rule).FileSystemRights);
                        }
                    }
                }
                catch (Exception)
                {
                }
                

                return (isDirectory ? "d" : "-") + (fileAccessDesc).PadRight(9, '-') + " " + (attributesNumber) + " " + acl.GetOwner(typeof(System.Security.Principal.NTAccount)).ToString() + " " + fileInfo.Length.ToString() + " " + fileInfo.CreationTime.ToString("MMM dd") + " " + fileInfo.Name;
            }
        }

        public enum FtpTransferType
        {
            I, // Image 
            A, // ASCII 
            E, // EBCDIC
            N, // Non-print
        }

        public enum DataConnectionType
        {
            Active,
            Passive
        }
    }
}
