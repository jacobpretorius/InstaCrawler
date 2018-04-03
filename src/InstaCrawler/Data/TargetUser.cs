using System;

namespace InstaCrawler.Data
{
    public class TargetUser
    {
        public string TargetHandle { get; set; }

        public DateTime LastCrawledAt { get; set; }
    }
}
