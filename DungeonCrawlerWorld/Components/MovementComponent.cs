using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// Determines the momement target selection type used by the movementSystem
    /// </summary>
    public enum MovementMode : byte
    {
        Random,
        SeekTarget
    }

    /// <summary>
    /// A core component that specifies an entity's ability to move through the map.
    /// </summary>
    public struct MovementComponent(MovementMode movementMode, short energyToMove, Vector3Int? targetMapPosition, Vector3Int? nextMapPosition) : IEntityComponent
    {
        public MovementMode MovementMode { get; set; } = movementMode;
        public short EnergyToMove { get; set; } = energyToMove;
        public short FramesToWait { get; set; } = 0;

        /// <summary>
        /// The 3d position on the map that the entity is pathing towards
        /// </summary>
        public Vector3Int? TargetMapPosition { get; set; } = targetMapPosition;

        /// <summary>
        /// The mapNode to attempt to move to as the next step in pathing towards the TargetMapPosition
        /// This is separated from the movement itself to allow for delayed or recalculated movement
        /// </summary>
        public Vector3Int? NextMapPosition { get; set; } = nextMapPosition;

        public override string ToString()
        {
            return $"Movement : {MovementMode}";
        }
    }
}
