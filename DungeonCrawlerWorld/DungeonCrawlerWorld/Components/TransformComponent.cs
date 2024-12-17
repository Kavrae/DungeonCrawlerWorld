using System;

using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.Components
{
    public enum MapHeight : byte
    {
        UnderGround = 0,
        Ground = 1,
        Standing = 2,
        Riding = 3,
        Floating = 4,
        Flying = 5
    }

    public struct TransformComponent
    {
        public Guid EntityId { get; set; }
        public Vector3Int Position { get; set; }
        public Vector3Int Size { get; set; } //TODO the z component is assumed to always be 1 for now.

        public TransformComponent(Guid entityId, Vector3Int position, Vector3Int size)
        {
            EntityId = entityId;
            Position = position;
            Size = size;

            ComponentRepo.TransformComponents.Remove(entityId);
            ComponentRepo.TransformComponents.Add(entityId, this);
        }

        public override string ToString()
        {
            return $"Transform : {Size} {Position}";
        }
    }
}
