using System.Collections.Generic;

namespace DungeonCrawlerWorld.Utilities
{
    public class DisplayText
    {
        public List<string> FormattedTextLines { get; set; }
        public bool IsTruncated { get; set; }
        public string OriginalText { get; set; }
    }
}