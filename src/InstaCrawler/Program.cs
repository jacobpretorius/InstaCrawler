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
        private static ElasticClient ElasticSearchTarget;

        public static int FoundEmailCounter { get; set; }
        public static string UserUrl { get; } = "https://www.instagram.com/";
        public static string TagUrl { get; } = "https://www.instagram.com/explore/tags/";

        public static Queue<string> TagQueue { get; set; } = new Queue<string>();
        public static Queue<string> UserQueue { get; set; } = new Queue<string>();
        
        static void Main(string[] args)
        {
            //setup ES connections, change as you see fit
            var settings = new ConnectionSettings(new Uri("http://localhost:9200")).DefaultIndex("emailfrominstagram");
            ElasticSearch = new ElasticClient(settings);

            settings = new ConnectionSettings(new Uri("http://localhost:9200")).DefaultIndex("emailfrominstagramtargets");
            ElasticSearchTarget = new ElasticClient(settings);

            //lets start with #bnw shall we
            TagQueue.Enqueue("bnw");
            TagQueue.Enqueue("bnw_society");
            TagQueue.Enqueue("photooftheday");
            TagQueue.Enqueue("instamood");
            TagQueue.Enqueue("instagood");
            TagQueue.Enqueue("mood");
            TagQueue.Enqueue("style");
            TagQueue.Enqueue("loveit");
            TagQueue.Enqueue("dayshots");
            TagQueue.Enqueue("artwork");
            TagQueue.Enqueue("naturelover");
            TagQueue.Enqueue("detail");
            TagQueue.Enqueue("igmasters");
            TagQueue.Enqueue("igmood");
            TagQueue.Enqueue("instamood");
            TagQueue.Enqueue("instatravel");
            TagQueue.Enqueue("moodstagram");
            TagQueue.Enqueue("picoftheday");

            Console.WriteLine("STARTING...");
            try
            {
                //start up threads
                List<Thread> threads = new List<Thread>();
                for (int i = 0; i < 4; i++)
                {
                    Thread t = new Thread(() => Work(i));
                    t.Start();
                    threads.Add(t);
                }

                // Await threads
                foreach (Thread thread in threads)
                {
                    thread.Join();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR, " + ex);
            }
        }

        static void Work(int t)
        {
            while (true)
            {
                try
                {
                    ReadPage(t).Wait();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERROR, " + ex);
                }
            }
        }

        static async Task ReadPage(int t)
        {
            //check if we have any users to scan
            //MODES
            // t - read user accounts
            // f - read hashtags
            var readUserMode = UserQueue.Any();

            //t1 preempts running out of targets
            if (t == 1 && UserQueue.Any() && UserQueue.Count < 100)
                readUserMode = false;

            string page = "";
            string target = "";
            
            //make the target url and dequeue in a locked state
            if (readUserMode)
            {
                lock (UserQueue)
                {
                    UserQueue.TryDequeue(out target);
                }
                page = UserUrl + target;
            }
            else
            {
                lock (TagQueue)
                {
                    TagQueue.TryDequeue(out target);
                }
                page = TagUrl + target;
            }

            try
            {
                //check for locking error
                if (!string.IsNullOrWhiteSpace(target))
                {
                    using (HttpClient client = new HttpClient())
                    {
                        using (HttpResponseMessage response = await client.GetAsync(page))
                        {
                            using (HttpContent content = response.Content)
                            {
                                // Read the string.
                                string result = await content.ReadAsStringAsync();
                                
                                if (result != null)
                                {
                                    await ProcessPage(target, result, readUserMode);

                                    //display update
                                    Console.WriteLine($"[t{t}] UserQueue {(UserQueue.Any() ? UserQueue.Count : 0)} | TagQueue {(TagQueue.Any() ? TagQueue.Count : 0)} || {(readUserMode ? "/" : "#")}{target}");
                                    Thread.Sleep(1500);
                                }
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("THREAD LOCKING ERROR");
                    Thread.Sleep(5000);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"ERROR WITH READING PAGE ON THREAD {t}");
            }
        }

        //process the page json
        static async Task ProcessPage(string target, string input, bool readUserMode)
        {
            //actually a 404 page
            if (input.Contains("Sorry, this page isn&#39;t available."))
                return;

            //rate limited
            if (input.Contains("Please wait a few minutes before you try again"))
            {
                Console.WriteLine("xx ratelimited");
                Thread.Sleep(10000);
                return;
            }

            //check if it has the json feed we latch on to
            if (!input.Contains("_sharedData = "))
                return;

            //format the json string
            var startCut = input.Substring(input.IndexOf("_sharedData = ") + 14);
            var jsonText = startCut.Substring(0, startCut.IndexOf(";</script>"));
            
            dynamic json = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonText);
            if (json != null)
            {
                //searching user page
                if (readUserMode)
                {
                    //look for emails in user disc
                    try
                    {
                        var userBio = json?.entry_data?.ProfilePage[0]?.graphql?.user?.biography;
                        if (userBio != null)
                        {
                            await ExtractEmails((string) userBio, target);
                            await ExtractUserTargets((string) userBio);
                            ExtractTagTargets((string) userBio);
                        }
                    }
                    //safe to ignore as it means we cant do anything
                    catch {}

                    //and all nodes aka uploads
                    var media = json?.entry_data?.ProfilePage[0]?.graphql?.user?.edge_owner_to_timeline_media?.edges;
                    if (media != null)
                    {
                        foreach (var upload in media)
                        {
                            
                            try
                            {
                                var desc = upload?.node?.edge_media_to_caption?.edges[0]?.node?.text;
                                if (desc != null)
                                {
                                    await ExtractEmails((string) desc, target);
                                    await ExtractUserTargets((string) desc);
                                    ExtractTagTargets((string) desc);
                                }
                            }
                            //we cant scrape anything if the node (upload) has no caption
                            catch { }
                        }
                    }
                }

                //searching tag page
                if (!readUserMode)
                {
                    var topPosts = json?.entry_data?.TagPage[0]?.graphql?.hashtag?.edge_hashtag_to_top_posts?.edges;
                    if (topPosts != null)
                    {
                        foreach (var post in topPosts)
                        {
                            try
                            {
                                var desc = post?.node?.edge_media_to_caption?.edges[0]?.node?.text;
                                if (desc != null)
                                {
                                    await ExtractEmails((string) desc, target);
                                    await ExtractUserTargets((string) desc);

                                    if (TagQueue != null && TagQueue.Count < 1000)
                                    {
                                        ExtractTagTargets((string) desc);
                                    }
                                }
                            }
                            //same as above, safe to ignore as this happens when no text available to parse
                            catch {}
                        }
                    }
                }
            }
        }

        //get the email from the page content
        static async Task ExtractEmails(string input, string target)
        {
            if (string.IsNullOrWhiteSpace(input))
                return;

            //parse for our finders
            input = input.Replace("\\n", " ");
            input = input.Replace("\n", " ");

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
                    email.EndsWith(".me") || email.EndsWith(".info") || email.EndsWith(".co"))
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
                                Account = target,
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
        static void ExtractTagTargets(string input)
        {
            //parse for our finders
            input = input.Replace("\\n", " ");
            input = input.Replace("\n", " ");

            Regex tagRegex = new Regex(@"(?<=#)\w+", RegexOptions.IgnoreCase);
            MatchCollection tagMatches = tagRegex.Matches(input);

            if (TagQueue != null && TagQueue.Count < 1000)
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
            //dont care for more than 5000 in the queue
            if (UserQueue.Count < 5000)
            {
                //parse for our finders
                input = input.Replace("\\n", " ");
                input = input.Replace("\n", " ");

                Regex userRegex = new Regex(@"(?<=@)\w+", RegexOptions.IgnoreCase);
                MatchCollection userMatches = userRegex.Matches(input);

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
                        var searchResponse = await ElasticSearchTarget.SearchAsync<TargetUser>(s => s
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
                            var _index = await ElasticSearchTarget.IndexAsync(new TargetUser
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
                                var _index = await ElasticSearchTarget.IndexAsync(new TargetUser
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
