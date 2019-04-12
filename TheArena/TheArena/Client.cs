using Logger;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
	    Log.TraceMessage(Log.Nav.NavIn, "Clearing arena files for start client.", Log.LogType.Info);
	    if (Directory.Exists(ARENA_FILES_PATH))
            {
              Directory.Delete(ARENA_FILES_PATH, true);  //RESET by deleting all files in the arena directories.
	      Directory.CreateDirectory(ARENA_FILES_PATH);
	    }
            Log.TraceMessage(Log.Nav.NavIn, "Starting Client FTP Server...", Log.LogType.Info); //FTP server receives files from host to build and run games. If this fails you will not receive files.
            StartFTPServer(false);
            Log.TraceMessage(Log.Nav.NavIn, "Creating the Keep-Alive Ping that let's the host know we are here and ready to run games...", Log.LogType.Info);
            string resultStr = "";
            using (Socket resultsSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)) //Create a SENDER UDP connection where we can send/receive results from games to host
            {
                using (Socket ping = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)) //Create a SENDER UDP connection where we can tell the host we are available to run games.
                {
                    using (UdpClient ask_for_game = new UdpClient(UDP_ASK_PORT)) //Create a RECEIVER UDP connection where we hear from the host to run games, or that our results have been recorded.
                    {
                        ask_for_game.Client.ReceiveTimeout = 5000;
                        Log.TraceMessage(Log.Nav.NavIn, "Only allow 5 seconds for sending and receiving...", Log.LogType.Info);
                        while (true) //Forever wait for a trigger from host to run games or run games.
                        {
                            try
                            {
                                IPEndPoint remoteEP;
                                if (resultStr == "") //Not told to run game yet, so just send ping that we are here
                                {
                                    Log.TraceMessage(Log.Nav.NavIn, "Sending Ping...", Log.LogType.Info);
                                    remoteEP = new IPEndPoint(IPAddress.Parse(HOST_ADDR), UDP_CONFIRM_PORT);
                                    ping.SendTo(new byte[] { 1 }, remoteEP); // Ping -- we are still here
                                }
                                else //We finished running a game, send the host the results
                                {
                                    Log.TraceMessage(Log.Nav.NavIn, "Sending Results...", Log.LogType.Info);
                                    remoteEP = new IPEndPoint(IPAddress.Parse(HOST_ADDR), UDP_CONFIRM_PORT);
                                    byte[] toSendResults = Encoding.ASCII.GetBytes(resultStr);
                                    Log.TraceMessage(Log.Nav.NavIn, "Sending toSendResults: " + resultStr + " as bytes=" + toSendResults.Length, Log.LogType.Info);
                                    resultsSocket.SendTo(toSendResults, remoteEP); // Send Results
                                }
                                Log.TraceMessage(Log.Nav.NavIn, "Waiting for game...", Log.LogType.Info);
                                byte[] data = ask_for_game.Receive(ref remoteEP); //Will wait for 5 seconds for data from host before returning to the top of the while loop because of try{}catch{TIMEOUTEXCEPTION}
                                string str_data = System.Text.Encoding.Default.GetString(data); //If we do get data within 5 seconds it's first byte will either be 1 or 0
                                if (data != null && data.Length == 1 && data[0] == 1) //If it's a 1, our results have been recorded and we can reset/move on to the next game
                                {
                                    Log.TraceMessage(Log.Nav.NavIn, "Host received results-clearing results.", Log.LogType.Info);
                                    resultStr = "";
                                    Directory.Delete(ARENA_FILES_PATH, true);  //RESET by deleting all files in the arena directories.
                                    Directory.CreateDirectory(ARENA_FILES_PATH);
                                }
                                if (data != null && data.Length == 1 && data[0] == 0) //If it's a 0, the host has finished sending us files and wants us to run them
                                {
                                    Log.TraceMessage(Log.Nav.NavIn, "We have been told to run game--LET'S GO!", Log.LogType.Info);
                                    List<string> results = BuildAndRunGame(); //All of the hard stuff happens in this function all of the winning info is returned as a string array
                                    Log.TraceMessage(Log.Nav.NavIn, "Results returned with size" + results.Count(), Log.LogType.Info); 
                                    string status = "finished";
                                    string winReason = results[0];
                                    string loseReason = results[1];
                                    string winnerName = results[2];
                                    string winnerSubmissionNumber = results[3];
                                    string loserName = results[4];
                                    string loserSubmissionNumber = results[5];
                                    string logURL = results[6];
                                    Log.TraceMessage(Log.Nav.NavOut, status + " " + winReason + " " + loseReason + " " + logURL + " " + winnerName + " " + winnerSubmissionNumber + " " + loserName + " " + loserSubmissionNumber, Log.LogType.Info);
                                    HTTP.HTTPPostSendToWeb(status, winReason, loseReason, logURL, winnerName, winnerSubmissionNumber, loserName, loserSubmissionNumber);
                                    resultStr = winnerName + ";" + logURL;
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

      
        public static bool RunGame(object fileo, string gameSession)
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
                    return BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.js/main.js", DA_GAME, gameSession);
                }
                else if (file.ToLower().Contains("cpp"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Cpp ", Log.LogType.Info);
                    return BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.cpp/main.cpp", DA_GAME, gameSession);
                }
                else if (file.ToLower().Contains("py"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Python ", Log.LogType.Info);
                    return BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.py/main.py", DA_GAME, gameSession);
                }
                else if (file.ToLower().Contains("lua"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Lua ", Log.LogType.Info);
                    return BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.lua/main.lua", DA_GAME, gameSession);
                }
                else if (file.ToLower().Contains("java"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Java ", Log.LogType.Info);
                    return BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.java/main.java", DA_GAME, gameSession);
                }
                else if (file.ToLower().Contains("cs"))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Building Csharp ", Log.LogType.Info);
                    return BuildAndRun(file.Substring(0, file.LastIndexOf(".")) + "/Joueur.cs/main.cs", DA_GAME, gameSession);
                }
            }
            catch (Exception ex)
            {
                Log.TraceMessage(Log.Nav.NavIn, ex);
            }
            return false;
        }

        /// <summary>
        /// Given the file path, compile the AI and run it using C++ -- run until the results file shows a win, loss, or error
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static bool BuildAndRun(string file, string DA_GAME, string gameSession)
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            if (isWindows)
            {
                Log.TraceMessage(Log.Nav.NavIn, "Is Windows...", Log.LogType.Info);
                Log.TraceMessage(Log.Nav.NavIn, "Starting Background Process...", Log.LogType.Info);
                //Unimplemented
            }
            else if (isLinux)
            {
                Log.TraceMessage(Log.Nav.NavIn, "Is Linux.", Log.LogType.Info);
		Stopwatch stopWatch = new Stopwatch();
        	stopWatch.Start();
                using (Process process = new Process())
                {
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardError = false;
                    process.StartInfo.RedirectStandardInput = true;
                    process.StartInfo.RedirectStandardOutput = false;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.FileName = Environment.GetEnvironmentVariable("SHELL");
                    Log.TraceMessage(Log.Nav.NavIn, "Grabbing Shell Process...", Log.LogType.Info);
                    if (process.Start())
                    {
                        process.StandardInput.WriteLine("cd " + file.Substring(0, file.LastIndexOf('/')));

                        if (File.Exists(file.Substring(0, file.LastIndexOf('/') + 1) + "testRun"))
                        {
                            File.Delete(file.Substring(0, file.LastIndexOf('/') + 1) + "testRun");
                        }
                        using (StreamWriter sw = new StreamWriter(file.Substring(0, file.LastIndexOf('/') + 1) + "testRun"))
                        {
                            sw.AutoFlush = true;
                            sw.WriteLine("#!/bin/bash");
                            sw.WriteLine("if [ -z \"$1\" ]");
                            sw.WriteLine("  then");
                            sw.WriteLine("    echo \"No argument(s) supplied. Please specify game session you want to join or make.\"");
                            sw.WriteLine("  else");
                            sw.WriteLine("    ./run " + DA_GAME + " -s 127.0.0.1 -r \"$@\"");
                            sw.WriteLine("fi");
                        }
                        Log.TraceMessage(Log.Nav.NavIn, "Rewrote script-- running", Log.LogType.Info);
                        process.StandardInput.WriteLine("sudo chmod 777 testRun && sudo chmod 777 run && sudo make clean && sudo make >> results.txt 2>&1 && sudo ./testRun "+gameSession); //Make the testRun file executable, clean compilation, compile, and output any bad compilation to text file.
			Log.TraceMessage(Log.Nav.NavIn, "Wrote Commands to process...", Log.LogType.Info);

                        //If compile fails, return false;
			if(stopWatch.Elapsed.Minutes>15)
			{
			    process.Kill();
    			    return false;
			}
			    
                        bool done = true;
                        string results = "";
                        do
                        {
			    Log.TraceMessage(Log.Nav.NavIn, "Calling HTTPPostGetStatus with "+DA_GAME+" and "+gameSession, Log.LogType.Info);
                            //Call Status GameServer API until status == "over"
                            var x = HTTP.HTTPPostGetStatus(DA_GAME, gameSession);
			    Log.TraceMessage(Log.Nav.NavIn, "x returned as "+x,Log.LogType.Info);
                            if(x!=null && x.status == "over")
                                done = true;
                            else
                                done = false;

                            Thread.Sleep(1000 * 10); //Wait 10 seconds for game to finish

                            if (done)
                            {
                                return true;
                            }

                            //Check for compile errors
		            Log.TraceMessage(Log.Nav.NavIn, "Checking for compile errors...", Log.LogType.Info);
			    int index=file.LastIndexOf('/') + 1;
			    Log.TraceMessage(Log.Nav.NavIn, "Last index of slash is "+index, Log.LogType.Info);
                            string resultsFile = file.Substring(0, index) + "results.txt";
                            Log.TraceMessage(Log.Nav.NavIn, "Results File=" + resultsFile, Log.LogType.Info);
                            if (File.Exists(resultsFile))
                            {
                                Log.TraceMessage(Log.Nav.NavIn, "Results file exists reading...", Log.LogType.Info);
                                using (StreamReader sr = new StreamReader(resultsFile))
                                {
                                    results = sr.ReadToEnd() + Environment.NewLine + file;
                                }
                                Log.TraceMessage(Log.Nav.NavIn, "Results=" + results, Log.LogType.Info);
                                if (results.ToUpper().Contains(" ERROR ") && !results.ToUpper().Contains("0 ERROR")) //C# says build succeeded 0 Errors
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                Log.TraceMessage(Log.Nav.NavIn, "Results file does not exist...", Log.LogType.Info);
                            }
                        } while (true);
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Given the file path, compile the AI and run it using C++ -- run until the results file shows a win, loss, or error
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static string BuildAndRun(string file, string DA_GAME, string gameSession)
        {

            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            if (isWindows)
            {
                Log.TraceMessage(Log.Nav.NavIn, "Is Windows...", Log.LogType.Info);
                Log.TraceMessage(Log.Nav.NavIn, "Starting Background Process...", Log.LogType.Info);
                //Unimplemented
            }
            else if (isLinux)
            {
                Log.TraceMessage(Log.Nav.NavIn, "Is Linux.", Log.LogType.Info);
                using (Process process = new Process())
                {
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardError = false;
                    process.StartInfo.RedirectStandardInput = true;
                    process.StartInfo.RedirectStandardOutput = false;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.FileName = Environment.GetEnvironmentVariable("SHELL");
                    Log.TraceMessage(Log.Nav.NavIn, "Grabbing Shell Process...", Log.LogType.Info);
                    if (process.Start())
                    {
                        process.StandardInput.WriteLine("cd " + file.Substring(0, file.LastIndexOf('/')));

                        if (File.Exists(file.Substring(0, file.LastIndexOf('/') + 1) + "testRun"))
                        {
                            File.Delete(file.Substring(0, file.LastIndexOf('/') + 1) + "testRun");
                        }
                        using (StreamWriter sw = new StreamWriter(file.Substring(0, file.LastIndexOf('/') + 1) + "testRun"))
                        {
                            sw.AutoFlush = true;
                            sw.WriteLine("#!/bin/bash");
                            sw.WriteLine("if [ -z \"$1\" ]");
                            sw.WriteLine("  then");
                            sw.WriteLine("    echo \"No argument(s) supplied. Please specify game session you want to join or make.\"");
                            sw.WriteLine("  else");
                            sw.WriteLine("    ./run " + DA_GAME + " -s 127.0.0.1 -r \"$@\"");
                            sw.WriteLine("fi");
                        }
                        Log.TraceMessage(Log.Nav.NavIn, "Rewrote script-- running", Log.LogType.Info);
                        process.StandardInput.WriteLine("sudo chmod 777 testRun && sudo chmod 777 run && sudo make clean && sudo make && ./testRun " + gameSession + " >>results.txt 2>&1");
                        string result = "";
                        do
                        {
                            Log.TraceMessage(Log.Nav.NavIn, "Results file not done waiting 1 min...", Log.LogType.Info);
                            Thread.Sleep(1000 * 60); //Wait 1 min for game to finish
                            string resultsFile = file.Substring(0, file.LastIndexOf('/') + 1) + "results.txt";
                            Log.TraceMessage(Log.Nav.NavIn, "Results File=" + resultsFile, Log.LogType.Info);
                            if (File.Exists(resultsFile))
                            {
                                Log.TraceMessage(Log.Nav.NavIn, "Results file exists reading...", Log.LogType.Info);
                                using (StreamReader sr = new StreamReader(resultsFile))
                                {
                                    result = sr.ReadToEnd() + Environment.NewLine + file;
                                }
                                Log.TraceMessage(Log.Nav.NavIn, "Results=" + result, Log.LogType.Info);
                            }
                            else
                            {
                                Log.TraceMessage(Log.Nav.NavIn, "Results file does not exist...", Log.LogType.Info);
                            }
                        } while (!result.ToLower().Contains("won") && !result.ToLower().Contains("lost") && !(result.ToLower().Contains(" error ") && !result.ToLower().Contains("0 error")));
                        Log.TraceMessage(Log.Nav.NavIn, "Results contains win or lose or com error--returning...", Log.LogType.Info);
                        return result;
                    }
                }
            }
            return "";
        }


        static List<string> BuildAndRunGame()
        {
            try
            {
                List<string> answers = new List<string>();
                string gameSession = "seth" + DateTime.Now.Ticks;
                Log.TraceMessage(Log.Nav.NavIn, "Building and Running Game ", Log.LogType.Info);
                if (!Directory.Exists(ARENA_FILES_PATH))
                {
                    Directory.CreateDirectory(ARENA_FILES_PATH);
                }
                var files = Directory.GetFiles(ARENA_FILES_PATH);
                Log.TraceMessage(Log.Nav.NavIn, "ARENA FILES Directory Contains " + files.Count() + " files.", Log.LogType.Info);
                List<Tuple<Task<bool>, string>> allGames = new List<Tuple<Task<bool>, string>>(); //First parameter is the thread, second parameter is the file being run (which has the player name)

                List<string> playerNames = new List<string>();
                //Call SETUP GameServer API and create random game session
                foreach (var f in files)
                {
                    var fs = f.Substring(f.LastIndexOf('/') + 1); // From C:\Users\Me\Documents\team1_1_csharp.zip to team_one_1_cs.zip
                    var withoutZip = fs.Substring(0, fs.LastIndexOf(".zip")); //To team_one_1_cs
                    string[] split = withoutZip.Split('_'); // ["team","one","1","cs"]
                    var reversed = split.Reverse().ToArray(); // ["cs","1","one","team"]
                    string lang = reversed[0]; //"cs"
                    string submission = reversed[1]; //"1"
                    string teamName = "";
                    for (int j = reversed.Length - 1; j > 1; j--)
                    {
                        teamName += reversed[j] + "_"; // "team_one_"
                    }
                    teamName = teamName.Substring(0, teamName.Length - 1); //"team_one"
                    playerNames.Add(teamName);
                }
                string gameSession = "seth" + DateTime.Now.Ticks;
                gameSettings gSettings = new gameSettings() {playerNames = playerNames.ToArray()};

                while(true)
                {
                    if (HTTP.HTTPPostStartGame(DA_GAME, gameSession, gSettings))
                    {
                        Log.TraceMessage(Log.Nav.NavIn, "Posted.", Log.LogType.Info);
                        break;
                    }
                    Thread.Sleep(100);
                    Log.TraceMessage(Log.Nav.NavIn, "Connecting...", Log.LogType.Info);
                }

                foreach (var file in files)
                {
                    Log.TraceMessage(Log.Nav.NavIn, "Creating Thread for file " + file, Log.LogType.Info);
                    Task<bool> t = Task.Run(() => RunGame(file, gameSession));
                    var fs = file.Substring(file.LastIndexOf('/') + 1); // From C:\Users\Me\Documents\team1_1_csharp.zip to team_one_1_cs.zip
                    var withoutZip = fs.Substring(0, fs.LastIndexOf(".zip")); //To team_one_1_cs
                    allGames.Add(new Tuple<Task<bool>, string>(t,withoutZip));
                }
                Log.TraceMessage(Log.Nav.NavIn, "Starting WaitAny ", Log.LogType.Info);
                Task.WaitAny(allGames.Select(_ => _.Item1).ToArray()); //Wait for all the threads to finish
                Log.TraceMessage(Log.Nav.NavIn, "Finished WaitAny", Log.LogType.Info);

                for(int i=0;i<allGames.Count; i++)
                {
                    if(allGames[i].Item1.IsCompleted && allGames[i].Item1.Result==false) //Error on compilation
                    {
			for(int j=0; j<allGames.Count; j++)
			{
			   try
		 	   {
			      allGames[j].Item1.Dispose();
			   }
			   catch(Exception exA)
			   {
				Log.TraceMessage(Log.Nav.NavOut, "Failed to abort thread..."+exA.Message, Log.LogType.Error);   
			   }
			}
                        Log.TraceMessage(Log.Nav.NavIn, "At least one client failed to compile.", Log.LogType.Info);
                        answers.Add("Other player did not compile"); //winReason
                        answers.Add("You did not compile"); // loseReason
                        
                        if(i==0) //If the first thread errored, set the winner as the second thread
                        {
                            Log.TraceMessage(Log.Nav.NavIn, "First Thread did not compile.", Log.LogType.Info);
                            string[] reversed=allGames[1].Item2.Split('_').Reverse().ToArray(); // ["cs","1","one","team"]
                            string teamName = "";
                            for (int j = reversed.Length - 1; j > 1; j--)
                            {
                                teamName += reversed[j] + "_"; // "team_one_"
                            }
                            teamName = teamName.Substring(0, teamName.Length - 1); //"team_one"
                            answers.Add(teamName); //winnerName --- May not be 0 -- its in the file somewhere
                            answers.Add(reversed[1]); //winnerSubmissionNumber --- May not be 1 -- its in the file somewhere

                            reversed = allGames[0].Item2.Split('_').Reverse().ToArray(); // ["cs","1","one","team"]
                            teamName = "";
                            for (int j = reversed.Length - 1; j > 1; j--)
                            {
                                teamName += reversed[j] + "_"; // "team_one_"
                            }
                            teamName = teamName.Substring(0, teamName.Length - 1); //"team_one"
                            answers.Add(teamName); //loserName --- May not be 0 -- its in the file somewhere
                            answers.Add(reversed[1]); //loserSubmissionNumber --- May not be 1 -- its in the file somewhere
                        }
                        else //If any other thread errored, set the winner as the first thread
                        {
                            Log.TraceMessage(Log.Nav.NavIn, "Other Thread did not compile.", Log.LogType.Info);
                            string[] reversed = allGames[0].Item2.Split('_').Reverse().ToArray(); // ["cs","1","one","team"]
                            string teamName = "";
                            for (int j = reversed.Length - 1; j > 1; j--)
                            {
                                teamName += reversed[j] + "_"; // "team_one_"
                            }
                            teamName = teamName.Substring(0, teamName.Length - 1); //"team_one"
                            answers.Add(teamName); //winnerName --- May not be 0 -- its in the file somewhere
                            answers.Add(reversed[1]); //winnerSubmissionNumber --- May not be 1 -- its in the file somewhere

                            reversed = allGames[1].Item2.Split('_').Reverse().ToArray(); // ["cs","1","one","team"]
                            teamName = "";
                            for (int j = reversed.Length - 1; j > 1; j--)
                            {
                                teamName += reversed[j] + "_"; // "team_one_"
                            }
                            teamName = teamName.Substring(0, teamName.Length - 1); //"team_one"
                            answers.Add(teamName); //loserName --- May not be 0 -- its in the file somewhere
                            answers.Add(reversed[1]); //loserSubmissionNumber --- May not be 1 -- its in the file somewhere
                        }

                        answers.Add("not created -- compilation failed"); //logURL
                        return answers; //return early
                    }
                }
                Log.TraceMessage(Log.Nav.NavIn, "All threads successfully compiled.", Log.LogType.Info);

                //Get Final Status from GameServer API
                Result status = HTTP.HTTPPostGetStatus(DA_GAME, gameSession);

                string winReason = status.clients[0].won ? status.clients[0].reason : status.clients[1].reason;
                answers.Add(winReason);
                string loseReason = status.clients[0].lost ? status.clients[0].reason : status.clients[1].reason;
                answers.Add(loseReason);
                string winnerName = status.clients[0].won ? status.clients[0].name : status.clients[1].name;
                string loserName = status.clients[0].lost ? status.clients[0].name : status.clients[1].name;
                answers.Add(winnerName);
                string winnerSubmissionNumber = "";
                string loserSubmissionNumber = "";
                foreach (var file in files)
                {
                    Log.TraceMessage(Log.Nav.NavIn, "checking file - "+file, Log.LogType.Info);
                    if (file.Contains(winnerName))
                    {
                        winnerSubmissionNumber = file.Split('_').Reverse().ToArray()[1]; //Maybe not 1 -- its in the file somewhere
                    }
                    if(file.Contains(loserName))
                    {
                        loserSubmissionNumber = file.Split('_').Reverse().ToArray()[1]; //Maybe not 1 -- its in the file somewhere
                    }
                }
                answers.Add(winnerSubmissionNumber);
                answers.Add(loserName);
                answers.Add(loserSubmissionNumber);
                string logURL = status.gamelogFilename;
                answers.Add(logURL);
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
