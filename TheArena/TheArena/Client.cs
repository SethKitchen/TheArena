using Logger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TheArena
{
    //Partial means -> Some of this class is defined in other files for readability
    public partial class Runner
    {
        private static void SetUpClientListener()
        {
            Log.TraceMessage(Log.Nav.NavIn, "Setting up client listener...", Log.LogType.Info);
            Thread clientListener = new Thread(ForeverQueueClients);
            clientListener.Start();
        }

        private static void ForeverQueueClients()
        {
            Log.TraceMessage(Log.Nav.NavIn, "Starting UDP Client on port " + UDP_CONFIRM_PORT, Log.LogType.Info);
            using (UdpClient listener = new UdpClient(UDP_CONFIRM_PORT))
            {
                Log.TraceMessage(Log.Nav.NavIn, "30 milliseconds before receive will timeout... ", Log.LogType.Info);
                listener.Client.ReceiveTimeout = 30;
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                while (true)
                {
                    try
                    {
                        byte[] bytes = listener.Receive(ref anyIP);
                        Log.TraceMessage(Log.Nav.NavIn, "Received " + (bytes != null ? bytes.Length : 0) + " bytes.", Log.LogType.Info);
                        if (bytes != null && bytes.Length == 1 && bytes[0] == 1) //Ping
                        {
                            Log.TraceMessage(Log.Nav.NavIn, "Hey someone pinged us! " + anyIP.Address, Log.LogType.Info);
                            IPAddress newClient = anyIP.Address;
                            if (!clients.Contains(newClient))
                            {
                                Console.WriteLine("Adding " + anyIP.Address);
                                Log.TraceMessage(Log.Nav.NavIn, "They were new adding them to the client queue.", Log.LogType.Info);
                                clients.Enqueue(newClient);
                            }
                        }
                        else //Game Finished
                        {
                            Log.TraceMessage(Log.Nav.NavIn, "Hey a client is saying they finished their assigned game...", Log.LogType.Info);
                            Log.TraceMessage(Log.Nav.NavIn, "CurrentlyRunningGames count=" + currentlyRunningGames.Count, Log.LogType.Info);
                            for (int i = 0; i < currentlyRunningGames.Count; i++)
                            {
                                if (currentlyRunningGames[i].ClientRunningGame.Equals(anyIP.Address))
                                {
                                    Log.TraceMessage(Log.Nav.NavIn, "Found a currently running game they should have been running...", Log.LogType.Info);
                                    Log.TraceMessage(Log.Nav.NavIn, "They sent us " + Encoding.ASCII.GetString(bytes), Log.LogType.Info);
                                    string[] str_data = Encoding.ASCII.GetString(bytes).Split(';');
                                    Game toCheck = currentlyRunningGames[i].GameRan;
                                    for (int j = 0; j < toCheck.Competitors.Count; j++)
                                    {
                                        if (toCheck.Competitors[j].Info.TeamName.Contains(str_data[0]))
                                        {
                                            Log.TraceMessage(Log.Nav.NavIn, "We found a complete match--setting winner." + Encoding.ASCII.GetString(bytes), Log.LogType.Info);
                                            toCheck.SetWinner(toCheck.Competitors[j], currentlyRunningTourney, toCheck.Competitors[j].Info.TeamName, str_data[1]);
                                            toCheck.IsComplete = true;
                                            toCheck.IsRunning = false;
                                        }
                                    }
                                }
                            }
                            Log.TraceMessage(Log.Nav.NavIn, "Sending Complete right back at em! " + Encoding.ASCII.GetString(bytes), Log.LogType.Info);
                            SendComplete(anyIP.Address);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!ex.Message.Contains("timed out"))
                        {
                            Log.TraceMessage(Log.Nav.NavIn, "Error=" + ex.Message, Log.LogType.Error);
                        }
                    }
                }
            }
        }









        static void RunClient()
        {
            Log.TraceMessage(Log.Nav.NavIn, "This Arena is Client.", Log.LogType.Info);
            Log.TraceMessage(Log.Nav.NavIn, "Starting Client FTP Server...", Log.LogType.Info);
            StartFTPServer(false);
            Log.TraceMessage(Log.Nav.NavIn, "Creating the Keep-Alive Ping that let's the host know we are here and ready to run games...", Log.LogType.Info);
            string resultStr = "";
            using (Socket resultsSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                using (Socket ping = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    using (UdpClient ask_for_game = new UdpClient(UDP_ASK_PORT))
                    {
                        ask_for_game.Client.ReceiveTimeout = 5000;
                        Log.TraceMessage(Log.Nav.NavIn, "Only allow 5 seconds for sending and receiving...", Log.LogType.Info);
                        while (true)
                        {
                            try
                            {
                                IPEndPoint remoteEP;
                                if (resultStr == "")
                                {
                                    Log.TraceMessage(Log.Nav.NavIn, "Sending Ping...", Log.LogType.Info);
                                    remoteEP = new IPEndPoint(IPAddress.Parse(HOST_ADDR), UDP_CONFIRM_PORT);
                                    ping.SendTo(new byte[] { 1 }, remoteEP); // Ping -- we are still here
                                }
                                else
                                {
                                    Log.TraceMessage(Log.Nav.NavIn, "Sending Results...", Log.LogType.Info);
                                    remoteEP = new IPEndPoint(IPAddress.Parse(HOST_ADDR), UDP_CONFIRM_PORT);
                                    byte[] toSendResults = Encoding.ASCII.GetBytes(resultStr);
                                    Log.TraceMessage(Log.Nav.NavIn, "Sending toSendResults: " + resultStr + " as bytes=" + toSendResults.Length, Log.LogType.Info);
                                    resultsSocket.SendTo(toSendResults, remoteEP); // Ping -- we are still here
                                }
                                Log.TraceMessage(Log.Nav.NavIn, "Waiting for game...", Log.LogType.Info);
                                byte[] data = ask_for_game.Receive(ref remoteEP);
                                string str_data = System.Text.Encoding.Default.GetString(data);
                                if (data != null && data.Length == 1 && data[0] == 1)
                                {
                                    Log.TraceMessage(Log.Nav.NavIn, "Host received results-clearing results.", Log.LogType.Info);
                                    resultStr = "";
                                    Directory.Delete(ARENA_FILES_PATH, true);
                                    Directory.CreateDirectory(ARENA_FILES_PATH);
                                }
                                if (data != null && data.Length == 1 && data[0] == 0)
                                {
                                    Log.TraceMessage(Log.Nav.NavIn, "We have been told to run game--LET'S GO!", Log.LogType.Info);
                                    List<string> results = BuildAndRunGame();
                                    Log.TraceMessage(Log.Nav.NavIn, "Results returned with size" + results.Count(), Log.LogType.Info);
                                    string status = "finished";
                                    string winReason = "";
                                    string loseReason = "";
                                    string winnerName = "";
                                    string winnerSubmissionNumber = "";
                                    string loserName = "";
                                    string loserSubmissionNumber = "";
                                    string logURL = "";
                                    foreach (string s in results)
                                    {
                                        string[] split = s.Split(Environment.NewLine);
                                        string[] splitLine = split[split.Length - 1].Split('_');
                                        splitLine[0] = splitLine[0].Substring(splitLine[0].LastIndexOf('/') + 1);
                                        string[] reversedSplitLine = splitLine.Reverse().ToArray();
                                        string teamName = "";
                                        for (int i = reversedSplitLine.Length - 1; i > 1; i--)
                                        {
                                            teamName += reversedSplitLine[i] + "_";
                                        }
                                        teamName = teamName.Substring(0, teamName.Length - 1);
                                        Log.TraceMessage(Log.Nav.NavIn, "team name" + teamName, Log.LogType.Info);
                                        string teamSubmissionNumber = reversedSplitLine[1];
                                        Log.TraceMessage(Log.Nav.NavIn, "sub num" + teamSubmissionNumber, Log.LogType.Info);
                                        bool won = false;
                                        foreach (string i in split)
                                        {
                                            if (i.ToUpper().Contains("WON"))
                                            {
                                                Log.TraceMessage(Log.Nav.NavIn, teamName + "won", Log.LogType.Info);
                                                won = true;
                                                winReason = i;
                                            }
                                            else if (i.ToUpper().Contains("ERROR"))
                                            {
                                                Log.TraceMessage(Log.Nav.NavIn, teamName + "error", Log.LogType.Info);
                                                loseReason = "error";
                                            }
                                            else if (i.ToUpper().Contains("LOST"))
                                            {
                                                Log.TraceMessage(Log.Nav.NavIn, teamName + "lost", Log.LogType.Info);
                                                loseReason = i;
                                            }
                                            else if (i.ToUpper().Contains("HTTP"))
                                            {
                                                logURL = i;
                                                Log.TraceMessage(Log.Nav.NavIn, teamName + "logurl" + logURL, Log.LogType.Info);
                                            }
                                        }
                                        if (won)
                                        {
                                            winnerName = teamName;
                                            winnerSubmissionNumber = teamSubmissionNumber;
                                        }
                                        else
                                        {
                                            loserName = teamName;
                                            loserSubmissionNumber = teamSubmissionNumber;
                                        }
                                    }
                                    Log.TraceMessage(Log.Nav.NavOut, status + " " + winReason + " " + loseReason + " " + logURL + " " + winnerName + " " + winnerSubmissionNumber + " " + loserName + " " + loserSubmissionNumber, Log.LogType.Info);
                                    HTTP.HTTPPost(status, winReason, loseReason, logURL, winnerName, winnerSubmissionNumber, loserName, loserSubmissionNumber);
                                    resultStr = winnerName + ";" + logURL;
                                }
                            }
                            catch
                            {
                                Log.TraceMessage(Log.Nav.NavIn, "5 second timeout on receiving game...", Log.LogType.Info);
                            }
                        }
                    }
                }
            }
        }

        public static void RunGame(object fileo, ref List<string> toReturn)
        {
            try
            {
                string file = fileo as string;
                Log.TraceMessage(Log.Nav.NavIn, "Thead for " + file + " started", Log.LogType.Info);
                if (Directory.Exists(file.Substring(0, file.LastIndexOf("."))))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Extraction Directory already existed for " + file + " ...deleting", Log.LogType.Info);
                    Directory.Delete(file.Substring(0, file.LastIndexOf(".")), true);
                }
                Log.TraceMessage(Log.Nav.NavIn, "Extracting ", Log.LogType.Info);
                ZipExtracter.ExtractZip(file, file.Substring(0, file.LastIndexOf(".")));
                if (file.ToLower().Contains("js"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building javascript ", Log.LogType.Info);
                    toReturn.Add(Javascript.BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.js/main.js"));
                }
                else if (file.ToLower().Contains("cpp"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Cpp ", Log.LogType.Info);
                    toReturn.Add(Cpp.BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.cpp/main.cpp"));
                }
                else if (file.ToLower().Contains("py"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Python ", Log.LogType.Info);
                    toReturn.Add(Python.BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.py/main.py"));
                }
                else if (file.ToLower().Contains("lua"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Lua ", Log.LogType.Info);
                    toReturn.Add(Lua.BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.lua/main.lua"));
                }
                else if (file.ToLower().Contains("java"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Java ", Log.LogType.Info);
                    toReturn.Add(Java.BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.java/main.java"));
                }
                else if (file.ToLower().Contains("cs"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Csharp ", Log.LogType.Info);
                    toReturn.Add(CSharp.BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.cs/main.cs"));
                }
            }
            catch (Exception ex)
            {
                Log.TraceMessage(Log.Nav.NavIn, ex);
            }
        }

        static List<string> BuildAndRunGame()
        {
            try
            {
                List<string> answers = new List<string>();
                Log.TraceMessage(Log.Nav.NavIn, "Building and Running Game ", Log.LogType.Info);
                if (!Directory.Exists(ARENA_FILES_PATH))
                {
                    Directory.CreateDirectory(ARENA_FILES_PATH);
                }
                var files = Directory.GetFiles(ARENA_FILES_PATH);
                Log.TraceMessage(Log.Nav.NavIn, "ARENA FILES Directory Contains " + files.Count() + " files.", Log.LogType.Info);
                List<Task> allGames = new List<Task>();
                foreach (var file in files)
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Creating Thread for file " + file, Log.LogType.Info);
                    allGames.Add(Task.Run(() => RunGame(file, ref answers)));
                }
                Log.TraceMessage(Log.Nav.NavIn, "Starting WaitAll ", Log.LogType.Info);
                Task.WaitAll(allGames.ToArray());
                Log.TraceMessage(Log.Nav.NavIn, "Finished WaitAll", Log.LogType.Info);
                return answers;
            }
            catch (Exception ex)
            {
                Log.TraceMessage(Log.Nav.NavIn, ex);
                return new List<string>();
            }
        }
    }
}
