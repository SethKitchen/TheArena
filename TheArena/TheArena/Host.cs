using Logger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TheArena
{
    //Partial means -> Some of this class is defined in other files for readability
    public partial class Runner
    {
        // Tells Client to Run the game with the AIs we sent them through FTP.
        static void SendRunGame(IPAddress toRun)
        {
            Log.TraceMessage(Log.Nav.NavIn, "Sending Run Game! ", Log.LogType.Info);
            using (Socket start_game = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                start_game.SendTo(new byte[1] { 0 }, new IPEndPoint(toRun, UDP_ASK_PORT));
            }
        }

        // Tells Client that game has been recorded and they can destroy all relevant info.
        static void SendComplete(IPAddress toComplete)
        {
            Log.TraceMessage(Log.Nav.NavIn, "Sending Complete! ", Log.LogType.Info);
            using (Socket start_game = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                start_game.SendTo(new byte[1] { 1 }, new IPEndPoint(toComplete, UDP_ASK_PORT));
            }
        }

        static void StartTourney(int people_per_game)
        {
            Log.TraceMessage(Log.Nav.NavOut, "Starting Tourney with " + people_per_game + " per game.", Log.LogType.Info);
            eligible_players = new List<PlayerInfo>();
            FillEligiblePlayers();
            Tournament t = new Tournament(eligible_players, people_per_game);
            currentlyRunningTourney = t;
            while (!t.IsDone)
            {
                Log.TraceMessage(Log.Nav.NavIn, "Tourney not done yet ", Log.LogType.Info);
                for (int i = 0; i < currentlyRunningGames.Count; i++)
                {
                    if ((new TimeSpan(DateTime.Now.Ticks - currentlyRunningGames[i].StartTimeTicks)).TotalMinutes > 20)
                    {
                        Log.TraceMessage(Log.Nav.NavIn, "It's been 5 minutes and game as not returned -- giving it to another client ", Log.LogType.Info);
                        currentlyRunningGames[i].GameRan.IsRunning = false;
                        currentlyRunningGames[i].GameRan.IsComplete = false;
                        currentlyRunningGames.RemoveAt(i);
                        i--;
                    }
                }
                t.GetNextNonRunningGame(out Game toAssign);
                if (toAssign != null)
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Game to assign is not null ", Log.LogType.Info);
                    bool dequeuedSuccess = clients.TryDequeue(out IPAddress clientToRun);
                    if (dequeuedSuccess)
                    {
                        Log.TraceMessage(Log.Nav.NavIn, "Dequeued Client ", Log.LogType.Info);
                        var files = Directory.GetFiles(ARENA_FILES_PATH);
                        for (int i = 0; i < toAssign.Competitors.Count; i++)
                        {
                            int maxSubmissionNumber = int.MinValue;
                            string maxSubmission = "";
                            for (int j = 0; j < files.Length; j++)
                            {
                                if (files[j].Contains(toAssign.Competitors[i].Info.TeamName))
                                {
                                    var split = files[j].Split('_');
                                    var reversed = split.Reverse().ToArray();
                                    Log.TraceMessage(Log.Nav.NavIn, reversed[1], Log.LogType.Info);
                                    if (int.Parse(reversed[1]) > maxSubmissionNumber)
                                    {
                                        maxSubmissionNumber = int.Parse(reversed[1]);
                                        maxSubmission = files[j];
                                        Log.TraceMessage(Log.Nav.NavIn, "Max submission number is now " + maxSubmissionNumber + " and " + maxSubmission, Log.LogType.Info);
                                    }
                                }
                            }
                            Log.TraceMessage(Log.Nav.NavIn, "Sending it over FTP", Log.LogType.Info);
                            FTPSender.Send_FTP(maxSubmission, clientToRun.ToString());
                        }
                        Log.TraceMessage(Log.Nav.NavIn, "Sending run game", Log.LogType.Info);
                        SendRunGame(clientToRun);
                        toAssign.IsRunning = true;
                        currentlyRunningGames.Add(new RunGameInfo { ClientRunningGame = clientToRun, GameRan = toAssign, StartTimeTicks = DateTime.Now.Ticks });
                    }
                }
                else
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Sleeping 5 seconds ", Log.LogType.Info);
                    Thread.Sleep(5000);
                }
            }
        }

        static void FillEligiblePlayers()
        {
            Log.TraceMessage(Log.Nav.NavIn, "Checking in Arena Directory for files to create eligible players...", Log.LogType.Info);
            if (!Directory.Exists(ARENA_FILES_PATH))
            {
                Log.TraceMessage(Log.Nav.NavOut, "Arena File Directory Didn't Exist..Creating it now.", Log.LogType.Info);
                Directory.CreateDirectory(ARENA_FILES_PATH);
            }
            var files = Directory.GetFiles(ARENA_FILES_PATH);
            if (files != null && files.Length > 0)
            {
                Log.TraceMessage(Log.Nav.NavOut, files.Length + " Files existed in Arena Files Directory.", Log.LogType.Info);
                foreach (string f in files)
                {
                    var fs = f.Substring(f.LastIndexOf('\\') + 1); // From C:\Users\Me\Documents\team1_1_csharp.zip to team_one_1_cs.zip
                    var withoutZip = fs.Substring(0, fs.LastIndexOf(".zip")); //To team_one_1_cs
                    string[] split = withoutZip.Split('_'); // ["team","one","1","cs"]
                    var reversed = split.Reverse().ToArray(); // ["cs","1","one","team"]
                    string lang = reversed[0]; //"cs"
                    string submission = reversed[1]; //"1"
                    string teamName = "";
                    for (int i = reversed.Length - 1; i > 1; i--)
                    {
                        teamName += reversed[i] + "_"; // "team_one_"
                    }
                    teamName = teamName.Substring(0, teamName.Length - 1); //"team_one"
                    Log.TraceMessage(Log.Nav.NavIn, "Adding team: " + teamName + " with lang" + lang + " and submissionNum=" + submission, Log.LogType.Info);
                    AddPlayerToArena(teamName, submission, lang);
                }
            }
        }

        static void RunHost()
        {
            Log.TraceMessage(Log.Nav.NavIn, "This Arena is Host.", Log.LogType.Info);
            Log.TraceMessage(Log.Nav.NavOut, "Setting Up Client Listener.", Log.LogType.Info);
            SetUpClientListener();
            Log.TraceMessage(Log.Nav.NavOut, "Setting Up FTP Server.", Log.LogType.Info);
            StartFTPServer(true);
        }
    }
}
