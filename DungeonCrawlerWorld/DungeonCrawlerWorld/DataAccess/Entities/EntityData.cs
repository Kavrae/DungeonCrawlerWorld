using System;
using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Data
{
    public class EntityData
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        //TODO TransformComponent
        public Point? MapPosition { get; set; }

        //TODO abstract out to displayable component.
        public string DisplayString { get; set; }
        public Color? ForegroundColor { get; set; }
        public Color? BackgroundColor { get; set; }
        public Point? DisplayStringOffset { get; set; }
        public int DisplayHierarchyLevel { get; set; }
        public bool IsSelected { get; set; }

        //TODO find way to move this into the Actionable module and easily find/reference it when it needs used. Use Movable as example.
        public int CurrentEnergy { get; set; }
    }
}
