using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using System.Reflection;
using System.Text.Json;

/*
 * Docs: https://developer.github.com/v3/pulls/reviews/

Example showing requested reviews: https://api.github.com/repos/openenclave/openenclave/pulls/2937
Example showing completed reviews: https://api.github.com/repos/openenclave/openenclave/pulls/2937/reviews
 */

namespace ghstats
{
    [Serializable]
    class GithubUser
    {
        public string login { get; set; }
        // Other fields.
    }

    [Serializable]
    class PullRequest
    {
        public string url { get; set; }
        public int number { get; set; }
        public string state { get; set; } // "open" or "closed"
        public List<GithubUser> requested_reviewers { get; set; }
        // Other fields.
    }

    [Serializable]
    class Review
    {
        public GithubUser user { get; set; }
        public string state { get; set; }
    }

    [Serializable]
    class RateLimitStatus
    {
        public RateLimitRate rate { get; set; }
    }

    [Serializable]
    class RateLimitRate
    {
        public int limit { get; set; }
        public int remaining { get; set; }
        public int reset { get; set; }
        public DateTimeOffset ResetDateTime
        { 
            get
            {
                DateTimeOffset localDateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(reset).DateTime.ToLocalTime();
                return localDateTimeOffset;
            } 
        }
    }

    class UserStats
    {
        public int ReviewRequests;  // Total number of PRs that mention the user in any way.
        public int WaitingCount;    // Number of open PRs waiting for review.
        public int UnreviewedCount; // Number of unreviewed closed PRs.
        public int ApprovedCount;   // Number of approvals (open or closed).
        public int CommentedCount;  // Number of comments (open or closed).
        public int OtherCount;      // Number of anything other than the above.
    }

    class Program
    {
        static UserStats FindOrCreateStats(Dictionary<string, UserStats> stats, string login)
        {
            if (!stats.ContainsKey(login))
            {
                stats[login] = new UserStats();
            }
            return stats[login];
        }

        static void PrintTally(Dictionary<string, UserStats> stats)
        {
            Console.WriteLine("---------------------------");
            Console.WriteLine("ID,Approved,Commented,Unreviewed,Waiting,Other");
            foreach (var entry in stats)
            {
                string login = entry.Key;
                UserStats userStats = entry.Value;
                Console.WriteLine("{0},{1},{2},{3},{4},{5}",
                    login,
                    userStats.ApprovedCount,
                    userStats.CommentedCount,
                    userStats.UnreviewedCount,
                    userStats.WaitingCount,
                    userStats.OtherCount);
            }
        }

        static void ShowUsage()
        {
            Console.WriteLine("Usage: ghstats [options] organization/repository\n");
            Console.WriteLine("Options:");
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

            var stats = new Dictionary<string, UserStats>();

            using (var wc = new WebClient())
            {
                // First check rate limit.
                string url = "https://api.github.com/rate_limit";
                wc.Headers.Add("User-Agent: Other");
                string rateLimitString = wc.DownloadString(url);
                RateLimitStatus rateLimitStatus = JsonSerializer.Deserialize<RateLimitStatus>(rateLimitString);
                if (rateLimitStatus.rate.remaining == 0)
                {
                    Console.WriteLine("Rate limit will reset at {0}", rateLimitStatus.rate.ResetDateTime);
                    return;
                }

                for (int page = 1; page <= maxPages; page++)
                {
                    url = "https://api.github.com/repos/" + repo + "/pulls?state=" + stateLimit + "&page=" + page;
                    wc.Headers.Add("User-Agent: Other");
                    string jsonString = wc.DownloadString(url);
                    var pageObject = JsonSerializer.Deserialize<List<PullRequest>>(jsonString);

                    // For each PR...
                    foreach (PullRequest pr in pageObject)
                    {
                        // For each review not done...
                        foreach (GithubUser user in pr.requested_reviewers)
                        {
                            UserStats userStats = FindOrCreateStats(stats, user.login);
                            userStats.ReviewRequests++;
                            if (pr.state == "open")
                            {
                                userStats.WaitingCount++;
                            }
                            else
                            {
                                userStats.UnreviewedCount++;
                            }
                        }
                        PrintTally(stats); // REMOVE

                        string reviewUrl = "https://api.github.com/repos/openenclave/openenclave/pulls/" + pr.number + "/reviews";
                        wc.Headers.Add("User-Agent: Other");
                        string jsonString2 = wc.DownloadString(reviewUrl);
                        var reviewsObject = JsonSerializer.Deserialize<List<Review>>(jsonString2);

                        // For each review done...
                        foreach (Review review in reviewsObject)
                        {
                            UserStats userStats = FindOrCreateStats(stats, review.user.login);
                            userStats.ReviewRequests++;
                            switch (review.state)
                            {
                                case "APPROVED": userStats.ApprovedCount++; break;
                                case "COMMENTED": userStats.CommentedCount++; break;
                                default: userStats.OtherCount++; break;
                            }
                            PrintTally(stats); // REMOVE
                        }
                    }
                }

                // Finally, print the tally.
                PrintTally(stats);
            }
        }
    }
}
