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
                                    HTTP.HTTPPostSendToWeb(status, winReason, loseReason, logURL, winnerName, winnerSubmissionNumber, loserName, loserSubmissionNumber);
                                    resultStr = winnerName + ";" + logURL;
				    Thread.Sleep(8000);, Log.LogType.Info
				    Directory.Delete(ARENA_FILES_PATH, true);
                                    Directory.CreateDirectory(ARENA_FILES_PATH);
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
                    toReturn.Add(Javascript.BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.js/main.js",DA_GAME));
                }
                else if (file.ToLower().Contains("cpp"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Cpp ", Log.LogType.Info);
                    toReturn.Add(Cpp.BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.cpp/main.cpp", DA_GAME));
                }
                else if (file.ToLower().Contains("py"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Python ", Log.LogType.Info);
                    toReturn.Add(Python.BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.py/main.py", DA_GAME));
                }
                else if (file.ToLower().Contains("lua"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Lua ", Log.LogType.Info);
                    toReturn.Add(Lua.BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.lua/main.lua",DA_GAME));
                }
                else if (file.ToLower().Contains("java"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Java ", Log.LogType.Info);
                    toReturn.Add(Java.BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.java/main.java",DA_GAME));
                }
                else if (file.ToLower().Contains("cs"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Csharp ", Log.LogType.Info);
                    toReturn.Add(CSharp.BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.cs/main.cs",DA_GAME));
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
