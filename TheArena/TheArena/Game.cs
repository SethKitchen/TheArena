using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace TheArena
{

    public class RunGameInfo
    {
        public IPAddress ClientRunningGame { get; set; }
        public Game GameRan { get; set; }
        public long StartTimeTicks { get; set; }
    }

    public class Game
    {
        public List<Player> Competitors;
        public bool IsRunning { get; set; }
        public bool IsComplete { get; set; }
        public int RoundNumber;
        public string Game_URL;

        public Game()
        {
            Competitors = new List<Player>();
            IsRunning = false;
            IsComplete = false;
        }

        public Player GetParentOfPlayersInThisGame()
        {
            if (Competitors.Count > 0)
            {
                return Competitors[0].ParentNode;
            }
            return null;
        }

        public void SetWinner(Player champion, Tournament t, string name, string url)
        {
            IsComplete = true;
            IsRunning = false;
            Game_URL = url;
            if (Competitors.Count > 0)
            {
                Competitors[0].ParentNode.Info.TeamName = name;
            }
            champion.Traverse(t.GetGames);
        }

        public bool ContainsPlayer(Player p)
        {
            return Competitors.Contains(p);
        }

        public void AddPlayer(Player p)
        {
            Competitors.Add(p);
        }
    }
}
