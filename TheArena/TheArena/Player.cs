using Logger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TheArena
{

    public class PlayerInfo
    {
        public Languages Lang { get; set; }
        public string TeamName { get; set; }
        public string Submission { get; set; }
    }

    public class Player : GenericTree<Player> // concrete derivation 
    {
        public PlayerInfo Info { get; set; }

        public Player ParentNode { get; set; }


        public Player(PlayerInfo info)
        {
            Info = info;
        }
    }

    //Partial means -> Some of this class is defined in other files for readability
    public partial class Runner
    {
        private static void ConvertNewFileToPlayerInfo(object sender, FileSystemEventArgs e)
        {
            Log.TraceMessage(Log.Nav.NavIn, "Directory changed!! Changed file: " + e.FullPath, Log.LogType.Info);
            var fs = e.FullPath.Substring(e.FullPath.LastIndexOf('\\') + 1);
            string[] split = fs.Split('_');
            if (split.Length == 3)
            {
                Log.TraceMessage(Log.Nav.NavIn, "Adding Player To Arena " + e.FullPath, Log.LogType.Info);
                AddPlayerToArena(split[0], split[1], split[2]);
            }
        }

        public static void AddPlayerToArena(string TeamName, string Submission, string lang)
        {
            PlayerInfo to_add = new PlayerInfo() { TeamName = TeamName, Submission = Submission };
            if (lang.ToLower().Contains("cpp"))
            {
                Log.TraceMessage(Log.Nav.NavIn, "Cpp ", Log.LogType.Info);
                to_add.Lang = Languages.Cpp;
            }
            else if (lang.ToLower().Contains("javascript"))
            {
                Log.TraceMessage(Log.Nav.NavIn, "Js ", Log.LogType.Info);
                to_add.Lang = Languages.Javascript;
            }
            else if (lang.ToLower().Contains("csharp"))
            {
                Log.TraceMessage(Log.Nav.NavIn, "Csharp ", Log.LogType.Info);
                to_add.Lang = Languages.CSharp;
            }
            else if (lang.Contains("java"))
            {
                Log.TraceMessage(Log.Nav.NavIn, "Java ", Log.LogType.Info);
                to_add.Lang = Languages.Java;
            }
            else if (lang.Contains("lua"))
            {
                Log.TraceMessage(Log.Nav.NavIn, "Lua ", Log.LogType.Info);
                to_add.Lang = Languages.Lua;
            }
            else if (lang.Contains("python"))
            {
                Log.TraceMessage(Log.Nav.NavIn, "Python ", Log.LogType.Info);
                to_add.Lang = Languages.Python;
            }
            if (!eligible_players.Contains(to_add))
            {
                Log.TraceMessage(Log.Nav.NavOut, "Didn't exist adding " + to_add.TeamName + " " + to_add.Submission + " " + to_add.Lang.ToString(), Log.LogType.Info);
                eligible_players.Add(to_add);
            }
            else
            {
                Log.TraceMessage(Log.Nav.NavOut, "Already existed -- didn't add again.", Log.LogType.Info);
            }
        }
    }
}
