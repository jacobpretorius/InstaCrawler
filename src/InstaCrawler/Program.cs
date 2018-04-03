using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using InstaCrawler.Data;
using Nest;

namespace InstaCrawler
{
    class Program
    {
        private static ElasticClient ElasticSearch;

        public static int FoundEmailCounter { get; set; }
        public static string UserUrl { get; } = "https://www.instagram.com/";
        public static string TagUrl { get; } = "https://www.instagram.com/explore/tags/";

        public static Queue<string> TagQueue { get; set; } = new Queue<string>();
        public static Queue<string> UserQueue { get; set; } = new Queue<string>();

        public static bool ReadUserMode { get; set; }
        //MODES
        // t - read user accounts
        // f - read hashtags

        static void Main(string[] args)
        {
            //setup ES connection, change as you see fit
            var settings = new ConnectionSettings(new Uri("http://localhost:9200"))
                .DefaultIndex("emailfrominstagram");

            ElasticSearch = new ElasticClient(settings);

            //lets start with #bnw shall we
            TagQueue.Enqueue("bnw");
            TagQueue.Enqueue("bnw_society");
            TagQueue.Enqueue("photooftheday");
            TagQueue.Enqueue("instamood");

            Console.WriteLine("STARTING...");
            try
            {
                //start up our 4 threads, with a sleep so that they dont race to be the first
                Task task1 = Task.Factory.StartNew(() => Work(1));
                Thread.Sleep(100);

                Task task2 = Task.Factory.StartNew(() => Work(2));
                Thread.Sleep(100);

                Task task3 = Task.Factory.StartNew(() => Work(3));
                Thread.Sleep(100);

                Task task4 = Task.Factory.StartNew(() => Work(4));

                Task.WaitAll(task1, task2, task3, task4);
                Console.WriteLine("All threads complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR, " + ex);
            }
        }

        static void Work(int t)
        {
            try
            {
                while (true)
                {
                    ReadPage(t).Wait();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR, " + ex);
            }
        }

        static async Task ReadPage(int t)
        {
            //check if we have any users to scan
            ReadUserMode = UserQueue.Any();

            string page = "";
            string target = "";
            
            //make the target url and dequeue in a locked state
            if (ReadUserMode)
            {
                lock (UserQueue)
                {
                    target = UserQueue.Dequeue();
                }
                page = UserUrl + target;
            }
            else
            {
                lock (TagQueue)
                {
                    target = TagQueue.Dequeue();
                }
                page = TagUrl + target;
            }

            //check for locking error
            if (!string.IsNullOrWhiteSpace(target))
            {
                using (HttpClient client = new HttpClient())
                {
                    using (HttpResponseMessage response = await client.GetAsync(page))
                    {
                        using (HttpContent content = response.Content)
                        {
                            // ... Read the string.
                            string result = await content.ReadAsStringAsync();

                            // ... Display the result.
                            if (result != null)
                            {
                                //parse for our finders
                                result = result.Replace("\\n", " ");
                                result = result.Replace("\n", " ");

                                ExtractTagTargets(ref result);
                                await ExtractUserTargets(result);

                                //only look for emails on user page, less false positives
                                if (ReadUserMode)
                                {
                                    await ExtractEmails(result);
                                }

                                //display update
                                Console.WriteLine($"[t{t}] UserQueue {(UserQueue.Any() ? UserQueue.Count : 0)} | TagQueue {(TagQueue.Any() ? TagQueue.Count : 0)} || {target}");
                            }
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("THREAD LOCKING ERROR");
            }
        }

        //get the email from the page content
        static async Task ExtractEmails(string input)
        {
            Regex emailRegex = new Regex(@"(([\w-]+\.)+[\w-]+|([a-zA-Z]{1}|[\w-]{2,}))@"
                                         + @"((([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])\.([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])\."
                                         + @"([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])\.([0-1]?[0-9]{1,2}|25[0-5]|2[0-4][0-9])){1}|"
                                         + @"([a-zA-Z]+[\w-]+\.)+[a-zA-Z]{2,4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            MatchCollection emailMatches = emailRegex.Matches(input);

            foreach (Match foundEmail in emailMatches)
            {
                //we have an email
                var email = foundEmail.ToString().ToLower();

                //check for some approved domains
                if (email.EndsWith(".com") || email.EndsWith(".net") || email.EndsWith(".org") ||
                    email.EndsWith(".me") || email.EndsWith(".info") || email.EndsWith(".co")
                    )
                {
                    //check if we have this email in ES already
                    try
                    {
                        var searchResponse = await ElasticSearch.SearchAsync<EmailItem>(s => s
                            .AllIndices()
                            .AllTypes()
                            .Query(q => q
                                .Match(m => m
                                    .Field(f => f.EmailAddress)
                                    .Query(email)
                                )
                            )
                        );

                        if (searchResponse != null && searchResponse?.Documents?.FirstOrDefault() == null)
                        {
                            //none found, jay for spam, index
                            var _index = await ElasticSearch.IndexAsync(new EmailItem
                            {
                                Contacted = false,
                                EmailAddress = foundEmail.ToString(),
                                EmailedAt = null
                            });

                            //update the display
                            Console.WriteLine($"---[{++FoundEmailCounter}] - {email}");
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("ES INDEX ERROR");
                    }
                }
            }
        }

        //find tags to look at
        static void ExtractTagTargets(ref string input)
        {
            Regex tagRegex = new Regex(@"(?<=#)\w+", RegexOptions.IgnoreCase);
            MatchCollection tagMatches = tagRegex.Matches(input);

            if (TagQueue.Count < 20)
            {
                foreach (Match foundTag in tagMatches)
                {
                    lock (TagQueue)
                    {
                        if (!TagQueue.Contains(foundTag.ToString()))
                        {
                            TagQueue.Enqueue(foundTag.ToString());
                        }
                    }
                }
            }
        }

        //find users to look at
        static async Task ExtractUserTargets(string input)
        {
            Regex userRegex = new Regex(@"(?<=@)\w+", RegexOptions.IgnoreCase);
            MatchCollection userMatches = userRegex.Matches(input);

            //dont care for more than 999 in the queue
            if (UserQueue.Count < 999)
            {
                foreach (Match foundUser in userMatches)
                {
                    var user = foundUser.ToString().ToLower();

                    //shitty users accounts filtering, these seem pretty common
                    if (string.IsNullOrWhiteSpace(user) || user == "2x" || user == "media" || user == "generated" || user == "gmail")
                        continue;

                    //check they arent in the queue from a different thread
                    if (UserQueue.Contains(user))
                        continue;

                    //check we havent seen them recently
                    try
                    {
                        var searchResponse = await ElasticSearch.SearchAsync<TargetUser>(s => s
                            .AllIndices()
                            .AllTypes()
                            .Query(q => q
                                .Match(m => m
                                    .Field(f => f.TargetHandle)
                                    .Query(user)
                                )
                            )
                        );

                        if (searchResponse != null && searchResponse?.Documents?.FirstOrDefault() == null)
                        {
                            //never been found, jay for spam, index
                            var _index = await ElasticSearch.IndexAsync(new TargetUser
                            {
                                TargetHandle = user,
                                LastCrawledAt = DateTime.UtcNow
                            });

                            lock (UserQueue)
                            {
                                UserQueue.Enqueue(user);
                            }
                        }
                        else if (searchResponse != null && searchResponse?.Documents?.FirstOrDefault() != null)
                        {
                            var res = searchResponse?.Documents?.FirstOrDefault();
                            if (res.LastCrawledAt <= DateTime.UtcNow.AddMonths(-3))
                            {
                                //havent seen you in 3 months, have an index
                                var _index = await ElasticSearch.IndexAsync(new TargetUser
                                {
                                    TargetHandle = user,
                                    LastCrawledAt = DateTime.UtcNow
                                });
                            }

                            lock (UserQueue)
                            {
                                UserQueue.Enqueue(user);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("ES TAG INDEX ERROR");
                    }
                }
            }
        }
    }
}
