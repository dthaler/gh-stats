// Copyright (c) 2020 Dave Thaler.  All Rights Reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

/*
 * Docs: https://developer.github.com/v3/pulls/reviews/

Example showing requested reviews: https://api.github.com/repos/openenclave/openenclave/pulls/2937
Example showing completed reviews: https://api.github.com/repos/openenclave/openenclave/pulls/2937/reviews
 */

namespace ghstats
{
    class UserStats
    {
        public int ReviewRequests;  // Total number of PRs that mention the user in any way.
        public int WaitingCount;    // Number of open PRs waiting for review.
        public int UnreviewedCount; // Number of unreviewed closed PRs.
        public int ApprovedCount;   // Number of approvals (open or closed).
        public int CommentedCount;  // Number of comments (open or closed).
        public int OtherCount;      // Number of anything other than the above.

        public static UserStats FindOrCreateStats(Dictionary<string, UserStats> stats, string login)
        {
            if (!stats.ContainsKey(login))
            {
                stats[login] = new UserStats();
            }
            return stats[login];
        }

        public static void PrintTally(Dictionary<string, UserStats> stats)
        {
            Console.WriteLine("---------------------------");
            Console.WriteLine("ID,Approved,Commented,Unreviewed,Waiting,Other,Total,Reviewed");
            foreach (var entry in stats)
            {
                string login = entry.Key;
                UserStats userStats = entry.Value;
                int total = userStats.ApprovedCount + userStats.CommentedCount + userStats.UnreviewedCount
                    + userStats.WaitingCount + userStats.OtherCount;
                int reviewed = 100 * (total - userStats.UnreviewedCount) / total;
                Console.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7}%",
                    login,
                    userStats.ApprovedCount,
                    userStats.CommentedCount,
                    userStats.UnreviewedCount,
                    userStats.WaitingCount,
                    userStats.OtherCount,
                    total,
                    reviewed);
            }
        }
    }

    class Program
    {
        static void ShowUsage()
        {
            Console.WriteLine("Usage: ghstats [options] organization/repository\n");
            Console.WriteLine("Options:");
            Console.WriteLine(" -c, --cached-only          Don't query github, only used cached data.");
            Console.WriteLine(" -h, --help                 Show help.");
            Console.WriteLine(" --pages=<count>            Fetch at most this many pages of pull requests (default=1).");
            Console.WriteLine(" --state=(all|closed|open)  Count pull requests in this state (default=closed).");
            Console.WriteLine(" --version                  Show version.");
        }

        static void Main(string[] args)
        {
            string stateLimit = "closed";
            int maxPages = 1;
            string repo = null;
            bool cachedOnly = false;

            foreach (string arg in args)
            {
                if (arg == "--version")
                {
                    Console.WriteLine("ghstats version " + Assembly.GetExecutingAssembly().GetName().Version);
                    return;
                }

                if (arg == "--help" || arg == "-h")
                {
                    ShowUsage();
                    return;
                }

                if (arg == "--cached-only" || arg == "-c")
                {
                    cachedOnly = true;
                    continue;
                }

                if (arg.StartsWith("--pages="))
                {
                    maxPages = Convert.ToInt32(arg.Substring(8));
                    continue;
                }

                if (arg.StartsWith("--state="))
                {
                    stateLimit = arg.Substring(8);
                    continue;
                }

                repo = arg;
            }

            if (!repo.Contains('/'))
            {
                Console.WriteLine("Error: you must specify a github repository\n");
                ShowUsage();
                return;
            }

            // Get the last known database for the specified repository.
            Database db = Database.Load(repo);

            if (!cachedOnly)
            {
                // Try to fetch updated data from github.
                GithubApi.UpdateDatabase(db, stateLimit, maxPages);
            }

            Dictionary<string, UserStats> stats = db.ComputeStats();

            // Finally, print the tally.
            if (stats != null)
            {
                UserStats.PrintTally(stats);
            }
        }
    }
}
