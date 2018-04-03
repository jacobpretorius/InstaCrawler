using System;

namespace InstaCrawler.Data
{
    public class EmailItem
    {
        public string EmailAddress { get; set; }

        public bool Contacted { get; set; }

        public DateTime? EmailedAt { get; set; }
    }
}
