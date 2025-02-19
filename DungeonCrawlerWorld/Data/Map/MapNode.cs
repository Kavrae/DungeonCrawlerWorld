using System;
using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.Data
{
    public struct MapNode
    {
        public Guid Id { get; set; }

        public Vector3Int Position { get; set; }
        public Vector3Int? NeighborNorth { get; set; }
        public Vector3Int? NeighborEast { get; set; }
        public Vector3Int? NeighborSouth { get; set; }
        public Vector3Int? NeighborWest { get; set; }
        public Vector3Int? NeighborUp { get; set; }
        public Vector3Int? NeighborDown { get; set; }

        public bool HasChanged { get; set; }

        public Guid? EntityId { get; set; }

        public MapNode(Vector3Int mapCoordinate) 
        {
            Id = Guid.NewGuid();
            Position = mapCoordinate;
            NeighborNorth = null;
            NeighborEast = null;
            NeighborSouth = null;
            NeighborWest = null;
            NeighborUp = null;
            NeighborDown = null;

            HasChanged = false;
            EntityId = null;
        }
    }
}
