using System;

using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.Components
{
    public enum MovementMode : byte
    {
        Random,
        SeekTarget
    }

    public struct MovementComponent : IEntityComponent
    {
        public int EntityId { get; set; }
        public MovementMode MovementMode { get; set; }
        public short EnergyToMove { get; set; }
        public short FramesToWait { get; set; }

        public Vector3Int? TargetMapPosition { get; set; }
        public Vector3Int? NextMapPosition { get; set; }

        public MovementComponent(int entityId, MovementMode movementMode, short energyToMove) : this( entityId, movementMode, energyToMove, null, null) { }
        public MovementComponent(int entityId, MovementMode movementMode, short energyToMove, Vector3Int? targetMapPosition, Vector3Int? nextMapPosition)
        {
            EntityId = entityId;
            MovementMode = movementMode;
            EnergyToMove = energyToMove;
            TargetMapPosition = targetMapPosition;
            NextMapPosition = nextMapPosition;
            FramesToWait = 0;

            ComponentRepo.MovementComponents[entityId] = this;
        }

        public override string ToString()
        {
            return $"Movement : {MovementMode}";
        }
    }
}
