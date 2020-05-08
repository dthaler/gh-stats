// Copyright (c) 2020 Dave Thaler.  All Rights Reserved.
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;

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

    class GithubApi
    {
        public static Dictionary<string, UserStats> FetchStats(string repo, string stateLimit, int maxPages)
        {
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
                    return null;
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
                            UserStats userStats = UserStats.FindOrCreateStats(stats, user.login);
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
                        UserStats.PrintTally(stats); // REMOVE

                        string reviewUrl = "https://api.github.com/repos/openenclave/openenclave/pulls/" + pr.number + "/reviews";
                        wc.Headers.Add("User-Agent: Other");
                        string jsonString2 = wc.DownloadString(reviewUrl);
                        var reviewsObject = JsonSerializer.Deserialize<List<Review>>(jsonString2);

                        // For each review done...
                        foreach (Review review in reviewsObject)
                        {
                            UserStats userStats = UserStats.FindOrCreateStats(stats, review.user.login);
                            userStats.ReviewRequests++;
                            switch (review.state)
                            {
                                case "APPROVED": userStats.ApprovedCount++; break;
                                case "COMMENTED": userStats.CommentedCount++; break;
                                default: userStats.OtherCount++; break;
                            }
                            UserStats.PrintTally(stats); // REMOVE
                        }
                    }
                }
            }

            return stats;
        }
    }
}
