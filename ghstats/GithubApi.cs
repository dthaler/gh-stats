// Copyright (c) 2020 Dave Thaler.  All Rights Reserved.
#undef USE_MOCK_DATA
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
    class GithubPullRequest
    {
        public string url { get; set; }
        public int number { get; set; }
        public string state { get; set; } // "open" or "closed"
        public List<GithubUser> requested_reviewers { get; set; }
        public string updated_at { get; set; }
        // Other fields.
    }

    [Serializable]
    class GithubReview
    {
        public GithubUser user { get; set; }
        public string state { get; set; }
    }

    [Serializable]
    class GithubRateLimitStatus
    {
        public GithubRateLimitRate rate { get; set; }
    }

    [Serializable]
    class GithubRateLimitRate
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
        public static bool UpdateDatabase(Database db, string stateLimit, int maxPages)
        {
            string repo = db.Repository;
            bool succeeded = true;
            bool changed = true;

            using (var wc = new WebClient())
            {
                // First check rate limit.
                string url = "https://api.github.com/rate_limit";
                wc.Headers.Add("User-Agent: Other");
                string rateLimitString;
#if !USE_MOCK_DATA
                try
                {
                    rateLimitString = wc.DownloadString(url);
                }
                catch (WebException)
                {
                    Console.Error.WriteLine("Warning: github is unreachable, so using only cached data");
                    return false;
                }
#else
                rateLimitString = "{\"rate\":{\"limit\":60,\"remaining\":60,\"reset\":0}}";
#endif
                GithubRateLimitStatus rateLimitStatus = JsonSerializer.Deserialize<GithubRateLimitStatus>(rateLimitString);
                if (rateLimitStatus.rate.remaining == 0)
                {
                    Console.Error.WriteLine("Rate limit will reset at {0}", rateLimitStatus.rate.ResetDateTime);
                    return false;
                }

                for (int page = 1; (page <= maxPages) && succeeded && changed; page++)
                {
                    url = "https://api.github.com/repos/" + repo + "/pulls?state=" + stateLimit + "&page=" + page;
                    wc.Headers.Add("User-Agent: Other");
                    string jsonString;
                    int lastPage;
#if !USE_MOCK_DATA
                    try
                    {
                        jsonString = wc.DownloadString(url);
                    }
                    catch (WebException ex)
                    {
                        Console.Error.WriteLine("Error: " + ex.Message);
                        succeeded = false;
                        break;
                    }

                    // The Link header will tell us how many total pages there are.  It will look like this:
                    // <https://api.github.com/repositories/101804230/pulls?state=closed&page=2>; rel="next", <https://api.github.com/repositories/101804230/pulls?state=closed&page=60>; rel="last"
                    // In the above example, there are 60 pages.
                    string linkHeaderValue = wc.ResponseHeaders.Get("Link");
                    int endIndex = linkHeaderValue.LastIndexOf(">; rel=\"last\"");
                    int pageStringIndex = linkHeaderValue.LastIndexOf("page=", endIndex);
                    string lastPageString = linkHeaderValue.Substring(pageStringIndex + 5, endIndex - pageStringIndex - 5);
                    lastPage = Convert.ToInt32(lastPageString);
#else
                    jsonString = "[{\"number\":13,\"state\":\"" + stateLimit + "\",\"requested_reviewers\":[{\"login\":\"user3\"},{\"login\":\"user4\"}],\"updated_at\":\"TBD\"}]";
                    lastPage = 1;
#endif
                    if (maxPages > lastPage)
                    {
                        maxPages = lastPage;
                    }
                    var pageObject = JsonSerializer.Deserialize<List<GithubPullRequest>>(jsonString);

                    // For each PR...
                    changed = false;
                    foreach (GithubPullRequest pr in pageObject)
                    {
                        // TODO: if we fetch the PR by itself then we can also get the "additions" and "deletions" line counts

                        // For each review not done...
                        foreach (GithubUser user in pr.requested_reviewers)
                        {
                            string reviewState = (pr.state == "open") ? "Waiting" : "Unreviewed";
                            db.UpdateReviewState(pr.number, user.login, reviewState);
                        }

                        // Only ask for more information if the PR was modified since we last got the data.
                        string lastUpdatedAt = db.GetPullRequestUpdatedAt(pr.number);
                        if ((pr.updated_at != lastUpdatedAt) && succeeded)
                        {
                            changed = true;
                            string reviewUrl = "https://api.github.com/repos/" + repo + "/pulls/" + pr.number + "/reviews";
                            wc.Headers.Add("User-Agent: Other");
                            try
                            {
#if !USE_MOCK_DATA
                                string jsonString2 = wc.DownloadString(reviewUrl);
#else
                                string jsonString2 = "[{\"user\":{\"login\":\"user1\"}, \"state\":\"APPROVED\"},{\"user\":{\"login\":\"user2\"}, \"state\":\"REQUESTED_CHANGES\"}]";
#endif
                                var reviewsObject = JsonSerializer.Deserialize<List<GithubReview>>(jsonString2);

                                // For each review done...
                                foreach (GithubReview review in reviewsObject)
                                {
                                    db.UpdateReviewState(pr.number, review.user.login, review.state);
                                }

                                // Finally, save the updated_at timestamp.
                                db.UpdatePullRequestState(pr.number, pr.state, pr.updated_at);
                            }
                            catch (WebException ex)
                            {
                                Console.Error.WriteLine("Error: " + ex.Message);

                                // Failed to get a response, stop reading pages.
                                succeeded = false;
                            }
                        }
                    }
                }
            }

            db.Save();

            return succeeded;
        }
    }
}
