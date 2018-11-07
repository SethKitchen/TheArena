using Logger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TheArena
{
    public static class TournamentExtensions
    {
        public static void Shuffle<T>(this IList<T> list)
        {
            RNGCryptoServiceProvider provider = new RNGCryptoServiceProvider();
            int n = list.Count;
            while (n > 1)
            {
                byte[] box = new byte[1];
                do provider.GetBytes(box);
                while (!(box[0] < n * (Byte.MaxValue / n)));
                int k = (box[0] % n);
                n--;
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
    }

    public class Tournament : GenericTree<Player>
    {

        static List<PlayerInfo> unfilledCompetitors;
        static List<Game> games = new List<Game>();
        int competitor_slots_in_bracket = 0;
        static int Rounds;
        Player champion;
        public bool IsDone { get; set; }

        public void GetNextNonRunningGame(out Game toReturn)
        {
            Log.TraceMessage(Log.Nav.NavIn, "Getting Next NonRunning Game ", Log.LogType.Info);
            champion.Traverse(PrettyPrint);
            bool allGamesComplete = true;
            for (int i = 0; i < games.Count; i++)
            {
                if (!games[i].IsComplete)
                {
                    allGamesComplete = false;
                    if (!games[i].IsRunning && games[i].Competitors.Count > 1)
                    {
                        toReturn = games[i];
                        return;
                    }
                    else if (games[i].Competitors.Count == 1 && games[i].Competitors[0].Info.TeamName != "")
                    {
                        Console.WriteLine("Winner: " + games[i].Competitors[0].Info.TeamName);
                        Log.TraceMessage(Log.Nav.NavIn, "Winner decided: " + games[i].Competitors[0].Info.TeamName, Log.LogType.Info);
                        allGamesComplete = true;
                    }
                }
            }
            if (allGamesComplete)
            {
                IsDone = true;
            }
            toReturn = null;
        }

        public Tournament(List<PlayerInfo> competitors, int players_per_game)
        {
            IsDone = false;
            while (competitors.Count % players_per_game != 0)
            {
                competitors.Add(new PlayerInfo() { TeamName = "BYE", Submission = "-1", Lang = Languages.None });
            }
            int number_of_rounds = (int)Math.Ceiling(Math.Log(competitors.Count) / Math.Log(players_per_game));
            TournamentExtensions.Shuffle(competitors);
            champion = new Player(new PlayerInfo() { Lang = Languages.None, Submission = "0", TeamName = "" }) { ParentNode = null };
            Rounds = number_of_rounds;
            unfilledCompetitors = competitors;
            AddLowerLevelRoundGame(champion, number_of_rounds, players_per_game);
            champion.Traverse(AddCompetitorsToBottomRound);
            champion.Traverse(PrettyPrint);
            champion.Traverse(GetGames);
        }

        public void AddLowerLevelRoundGame(Player parent, int round_number, int players_per_game)
        {
            if (round_number <= 0)
            {
                return;
            }
            else
            {
                for (int i = 0; i < players_per_game && competitor_slots_in_bracket < unfilledCompetitors.Count; i++)
                {
                    Player g = new Player(new PlayerInfo() { Lang = Languages.None, Submission = "0", TeamName = "" });
                    AddLowerLevelRoundGame(g, round_number - 1, players_per_game);
                    g.ParentNode = parent;
                    parent.AddChild(g);
                    if (round_number == 1)
                    {
                        competitor_slots_in_bracket++;
                    }
                }
            }
        }

        public void GetGames(int depth, Player node)
        {
            bool handled = false;
            for (int i = 0; i < games.Count; i++)
            {
                if (games[i].GetParentOfPlayersInThisGame() == node.ParentNode)
                {
                    if (!games[i].ContainsPlayer(node))
                    {
                        games[i].AddPlayer(node);
                    }
                    handled = true;
                    break;
                }
            }
            if (!handled)
            {
                Game g = new Game();
                g.AddPlayer(node);
                games.Add(g);
                g.RoundNumber = Rounds - depth + 1;
            }
        }

        static void AddCompetitorsToBottomRound(int depth, Player node)
        {
            if (depth == Rounds)
            {
                node.Info = unfilledCompetitors[0];
                unfilledCompetitors.RemoveAt(0);
            }
        }

        static void PrettyPrint(int depth, Player node)
        {                                // a little one-line string-concatenation (n-times)
            Console.WriteLine("{0}{1}: {2}", String.Join("   ", new string[depth + 1]), depth, node.Info.TeamName);
        }
    }
}