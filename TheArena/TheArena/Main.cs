using Logger;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static TheArena.ClientConnection;

namespace TheArena
{
    public enum Languages
    {
        Cpp,
        CSharp,
        Java,
        Javascript,
        Python,
        Lua,
        None
    }

    public partial class Runner
    {
        const string HOST_ADDR = "146.148.77.205";
        const string ARENA_FILES_PATH = @"/home/siggame/ArenaFiles/";
        const string DA_GAME = "Spiders";
        const int HOST_PORT = 21;
        const int UDP_ASK_PORT = 234;
        const int UDP_CONFIRM_PORT = 1100;
        
        static FtpServer server;
        static List<PlayerInfo> eligible_players = new List<PlayerInfo>();
        static ConcurrentQueue<IPAddress> clients = new ConcurrentQueue<IPAddress>();
        static List<RunGameInfo> currentlyRunningGames = new List<RunGameInfo>();
        static Tournament currentlyRunningTourney;

        static void Main(string[] args)
        {
            try
            {
                Log.TraceMessage(Log.Nav.NavIn, "START", Log.LogType.Info);
                Log.TraceMessage(Log.Nav.NavIn, "HOST ADDRESS is " + HOST_ADDR, Log.LogType.Info);
                Log.TraceMessage(Log.Nav.NavIn, "Arena File Directory is " + ARENA_FILES_PATH, Log.LogType.Info);
                Log.TraceMessage(Log.Nav.NavIn, "HOST PORT " + HOST_PORT, Log.LogType.Info);
                Log.TraceMessage(Log.Nav.NavIn, "ASK PORT " + UDP_ASK_PORT, Log.LogType.Info);
                Log.TraceMessage(Log.Nav.NavIn, "CONFIRM PORT " + UDP_CONFIRM_PORT, Log.LogType.Info);
                string hostName = Dns.GetHostName(); // Retrive the Name of HOST  
                Log.TraceMessage(Log.Nav.NavIn, "HOST NAME: " + hostName, Log.LogType.Info);
                var myIP = Dns.GetHostEntry(hostName).AddressList;
                IPAddress arena_host_address = IPAddress.Parse(HOST_ADDR);
                if (myIP.ToList().Contains(arena_host_address))
                {
                    Log.TraceMessage(Log.Nav.NavIn, "My IP matches Host IP", Log.LogType.Info);
                    RunHost();
                }
                else
                {
                    Log.TraceMessage(Log.Nav.NavIn, "My IP does NOT match Host IP", Log.LogType.Info);
                    RunClient();
                }
            }
            catch (Exception ex)
            {
                Log.TraceMessage(Log.Nav.NavIn, "Exception in Main Thread: " + ex.Message, Log.LogType.Error);
            }
        }
    }
}
