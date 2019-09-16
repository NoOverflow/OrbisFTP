using System;
using System.Threading;
using OrbisFTP;

namespace OrbisTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Server ftpServer = new Server();
            ftpServer.Start();

            ftpServer.ClientJoined += FtpServer_ClientJoined;

            ConsoleKey pressedKey = ConsoleKey.A;

            while((pressedKey = Console.ReadKey().Key) != ConsoleKey.X)
            {

            }

            Environment.Exit(0);
        }

        private static void FtpServer_ClientJoined(object sender, ClientJoinedArgs e)
        {
            Console.Clear();

            Console.WriteLine("\nConnected users (" + ((Server)sender).Clients.Count + "): \n");

            foreach (var client in ((Server)sender).Clients)
            {
                Console.WriteLine(String.Format("    - IP : {0}  |  Username : {1}  |  Working Directory : {2}",
                    client.TcpClient.Client.RemoteEndPoint.ToString(),
                    client.IsLoggedIn ? client.Username : "Not Logged In",
                    client.WorkingDirectory.FullName)
                );
            }

            Console.WriteLine("\n\nPress X to Exit");
        }
    }
}
