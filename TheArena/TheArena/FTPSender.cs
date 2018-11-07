using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TheArena
{
    public class FTPSender
    {
        public static void Send_FTP(string path_to_file, string ip_to_send_to)
        {
            byte[] data = new byte[1024];
            string ARENA_HOST_IP = ip_to_send_to;
            const int ARENA_HOST_PORT = 21;
            string hostName = Dns.GetHostName();
            var myIP = Dns.GetHostEntry(hostName).AddressList;
            string WEB_SERVER_ZIP_FILE_IP = "127.0.0.1";        // This will not normally be the same as ARENA_HOST_IP - it will be where the web server is sending the zip file from.
            foreach (var x in myIP)
            {
                if (x.ToString().Split('.').Length == 4)
                {
                    WEB_SERVER_ZIP_FILE_IP = x.ToString();
                }
            }
            const int WEB_SERVER_ZIP_FILE_PORT = 300;          // Can be virtually any port
            string ZIP_FILE_NAME = path_to_file;
            byte[] zip_file_contents;
            using (StreamReader sr = new StreamReader(ZIP_FILE_NAME))
            {
                zip_file_contents = File.ReadAllBytes(ZIP_FILE_NAME);
            }
            IPEndPoint arenaEP = new IPEndPoint(IPAddress.Parse(ARENA_HOST_IP), ARENA_HOST_PORT);
            using (Socket serveZipSock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                IPEndPoint serveZipEP = new IPEndPoint(IPAddress.Parse(WEB_SERVER_ZIP_FILE_IP), WEB_SERVER_ZIP_FILE_PORT);
                serveZipSock.Bind(serveZipEP);
                try
                {
                    TcpClient client = new TcpClient();                 // Act as a TCP Client
                    client.Connect(arenaEP);                            // Connect to Arena FTP Server
                    using (NetworkStream ns = client.GetStream())       // Read/Write TCP transmissions as a stream of bytes
                    {
                        using (StreamWriter sw = new StreamWriter(ns))
                        {
                            using (StreamReader sr = new StreamReader(ns))
                            {
                                Console.WriteLine("Response: " + sr.ReadLine());    // Read initial contact from arena
                                Console.WriteLine("Sending Username Command");
                                sw.WriteLine("USER Guest");                         // Send the Web Server Username to the arena
                                sw.Flush();                                         // Push it out of the buffer
                                System.Threading.Thread.Sleep(10);                 // Wait for Processing
                                Console.WriteLine("Response: " + sr.ReadLine());    // Read response contact from arena.
                                Console.WriteLine("Sending Password Command");
                                sw.WriteLine("PASS ");                              // Send the Web Server Password to the arena -- currently is nothing
                                sw.Flush();                                         //Push it out of the buffer
                                System.Threading.Thread.Sleep(10);                 //Wait for Processing
                                Console.WriteLine("Response: " + sr.ReadLine());    //Read response contact from arena.
                                Console.WriteLine("Sending EPRT- Will Listen on port 300");
                                sw.WriteLine("EPRT |1|" + WEB_SERVER_ZIP_FILE_IP + "|300|");             //Tell arena we want to send a zip file to it. It should connect to our IP at port 300.
                                sw.Flush();                                         //Push it out of the buffer
                                System.Threading.Thread.Sleep(10);                 //Wait for Processing
                                Console.WriteLine("Response: " + sr.ReadLine());    //Read response contact from arena
                                Console.WriteLine("Sending TYPE I");
                                sw.WriteLine("TYPE I");                             //Tell arena we are sending bytes not text.
                                sw.Flush();                                         //Push it out of the buffer
                                System.Threading.Thread.Sleep(10);                 //Wait for Processing
                                Console.WriteLine("Response: " + sr.ReadLine());    //Read response contact from arena
                                Console.WriteLine("Sending STOR");
                                serveZipSock.Listen(2);                             //Listen for arena to connect to our address at port 300. Allow 2 connections.
                                sw.WriteLine("STOR " + ZIP_FILE_NAME.Substring(ZIP_FILE_NAME.LastIndexOf('/') + 1));              //Tell arena to store the incoming file as my_zip.zip
                                sw.Flush();
                                Socket newClient = serveZipSock.Accept();           //Accept the Arena's request to communicate on port 300 and save the endpoint.
                                IPEndPoint newClientIP = (IPEndPoint)newClient.RemoteEndPoint;
                                System.Threading.Thread.Sleep(10);                   //Wait for Processing
                                int sent = SendVarData(newClient, zip_file_contents); //Send Zip File
                                newClient.Shutdown(SocketShutdown.Both);              //Kill the communication on port 300.
                                newClient.Close();
                                System.Threading.Thread.Sleep(100);                 //Wait for processing
                                Console.WriteLine("Response: " + sr.ReadLine());    //Read Arena's response.
                                Console.WriteLine("Sending Quit: ");                //Disconnect from Arena.
                                sw.WriteLine("QUIT 221");
                                sw.Flush();                                         //Push it out of buffer.
                                System.Threading.Thread.Sleep(100);                 //Wait for processing.
                                Console.WriteLine("Response: " + sr.ReadLine());    //Read Arena's response.
                            }
                        }
                    }
                }
                catch (SocketException e)
                {
                    Console.WriteLine("Unable to connect to server.");
                    Console.WriteLine(e.ToString());
                    Console.ReadLine();
                }
            }
        }

        private static int SendVarData(Socket s, byte[] data)
        {
            int total = 0;
            int size = data.Length;
            int dataleft = size;
            int sent;

            byte[] datasize = new byte[4];
            datasize = BitConverter.GetBytes(size);

            while (total < size)
            {
                sent = s.Send(data, total, dataleft, SocketFlags.None);
                total += sent;
                dataleft -= sent;
            }
            return total;
        }
    }
}
