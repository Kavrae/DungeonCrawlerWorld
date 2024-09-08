using DungeonCrawlerWorld.Data;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace DungeonCrawlerWorld.Actions
{
    public interface IAction
    {
        public int Priority { get; set; }

        public List<Entity> TargetEntities { get; set; }
        public List<Point> TargetMapNodes { get; set; }

        public List<Guid> InteractableEntityComponentGuids { get; set; }

        public void Perform();
    }
}
