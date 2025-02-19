using DungeonCrawlerWorld.Utilities;
using System;
using System.Collections.Generic;

namespace DungeonCrawlerWorld.Actions
{
    //TODO Unused. Sort out later.
    public interface IAction
    {
        public int Priority { get; set; }

        public List<Guid> TargetEntityIds { get; set; }
        public List<Vector3Int> TargetMapNodes { get; set; }

        public List<Guid> InteractableEntityComponentGuids { get; set; }

        public void Perform();
    }
}
