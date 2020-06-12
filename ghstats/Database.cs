using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace ghstats
{
    [Serializable]
    class DatabasePullRequest
    {
        public int Number { get; set; }
        public string State { get; set; } // "open" or "closed"
        public string UpdatedAt { get; set; }
        public bool FetchedReviews { get; set; }
        public Dictionary<string, string> ReviewerState { get; set; }

        public DatabasePullRequest()
        {
            ReviewerState = new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// This class is responsible for persisting a cache of data so we only have to query
    /// for information we don't already have.
    /// </summary>
    [Serializable]
    class Database
    {
        public Dictionary<int, DatabasePullRequest> PullRequests { get; set; }
        public string Repository { get; set; }
        public bool SnapshotComplete { get; set; }
        public int LastSnapshotPageRead { get; set; }

        Database(string repo)
        {
            Repository = repo;
            PullRequests = new Dictionary<int, DatabasePullRequest>();
        }

        private DatabasePullRequest FindOrCreatePullRequestState(int number)
        {
            DatabasePullRequest pr;
            if (!PullRequests.ContainsKey(number))
            {
                pr = new DatabasePullRequest();
                pr.Number = number;
                PullRequests[number] = pr;
            }
            return PullRequests[number];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="number"></param>
        /// <param name="state"></param>
        /// <param name="updatedAt"></param>
        /// <returns>true if changed, false if unchanged</returns>
        public bool UpdatePullRequestState(int number, string state, string updatedAt)
        {
            DatabasePullRequest pr = FindOrCreatePullRequestState(number);
            pr.State = state;
            if (pr.UpdatedAt == updatedAt)
            {
                return false;
            }
            pr.UpdatedAt = updatedAt;
            pr.FetchedReviews = false;
            return true;
        }

        public void SetFetchedReviews(int number)
        {
            DatabasePullRequest pr = FindOrCreatePullRequestState(number);
            pr.FetchedReviews = true;
        }

        public void UpdateReviewState(int number, string login, string state)
        {
            DatabasePullRequest pr = FindOrCreatePullRequestState(number);
            pr.ReviewerState[login] = state;
        }

        public string GetPullRequestUpdatedAt(int number)
        {
            DatabasePullRequest pr = FindOrCreatePullRequestState(number);
            return pr.UpdatedAt;
        }

        public static Database Load(string repo)
        {
            string fileName = "./" + repo + ".data";
            if (!File.Exists(fileName))
            {
                return new Database(repo);
            }

            var iser = new BinaryFormatter();
            using (FileStream istream = new FileStream(fileName, FileMode.Open))
            {
                Database db = (Database)(iser.Deserialize(istream));
                return db;
            }
        }

        public void Save()
        {
            string fileName = "./" + this.Repository + ".data";
            string directoryName = System.IO.Path.GetDirectoryName(fileName);
            if (!Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            BinaryFormatter oser = new BinaryFormatter();
            FileStream ostream = new FileStream(fileName, FileMode.Create);
            oser.Serialize(ostream, this);
            ostream.Close();
        }

        public Dictionary<string, UserStats> ComputeStats(string stateLimit, DateTime updatedSince)
        {
            var stats = new Dictionary<string, UserStats>();
            foreach (var entry in this.PullRequests)
            {
                int number = entry.Key;
                DatabasePullRequest pr = entry.Value;
                if ((stateLimit != "all") && (pr.State != stateLimit))
                {
                    continue;
                }
                DateTime updatedAt = DateTime.Parse(pr.UpdatedAt);
                if (DateTime.Compare(updatedAt, updatedSince) < 0)
                {
                    continue;
                }
                foreach (var review in pr.ReviewerState)
                {
                    string login = review.Key;
                    string reviewState = review.Value;
                    UserStats userStats = UserStats.FindOrCreateStats(stats, login);
                    userStats.ReviewRequests++;
                    switch (reviewState)
                    {
                        case "Waiting": userStats.WaitingCount++; break;
                        case "Unreviewed": userStats.UnreviewedCount++; break;
                        case "APPROVED": userStats.ApprovedCount++; break;
                        case "COMMENTED": userStats.CommentedCount++; break;
                        case "CHANGES_REQUESTED": userStats.OtherCount++; break;
                        case "DISMISSED": userStats.UnreviewedCount++; break;
                        default: userStats.OtherCount++; break;
                    }
                }
            }
            return stats;
        }

        public bool UpdateDatabase(int maxPages)
        {
            // In the first stage, fetch new pages of pull requests.
            if (this.SnapshotComplete)
            {
                if (!GithubApi.GetGithubDeltas(this, maxPages))
                {
                    return false;
                }
            }
            else
            {
                if (!GithubApi.GetGithubSnapshot(this, maxPages))
                {
                    return false;
                }
            }

            // In the second stage, fetch review data for each updated PR.
            foreach (var entry in this.PullRequests.Where(entry => !entry.Value.FetchedReviews))
            {
                DatabasePullRequest pr = entry.Value;
                if (!GithubApi.GetGithubReview(this, pr.Number))
                {
                    Save();
                    return false;
                }
            }
            Save();
            return true;
        }
    }
}
