using DungeonCrawlerWorld.Data;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DungeonCrawlerWorld.EntityComponents
{
    public class Displayable : IEntityComponent
    {
        public string DisplayString { get; set; }
        public Color? ForegroundColor { get; set; }
        public Color? BackgroundColor { get; set; }
        public Point? DisplayStringOffset { get; set; }
        public int DisplayHierarchyLevel { get; set; }
    }
}
