using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.ComponentSystems
{
    /// <summary>
    /// Responsible for selecting the next MapNode to path the entity towards and then moving towards that node based 
    /// upon the set movement type.
    /// </summary>
    public class MovementSystem : ComponentSystem
    {
        public byte FramesPerUpdate => 15;

        private World world;

        private Random randomizer;

        public MovementSystem()
        {
            var dataAccessService = GameServices.GetService<DataAccessService>();
            world = dataAccessService.RetrieveWorld();

            randomizer = new Random();
        }

        public void Update(GameTime gameTime)
        {
            foreach (var keyValuePair in ComponentRepo.MovementComponents)
            {
                int entityId = keyValuePair.Key;
                var movementComponent = keyValuePair.Value;

                if (movementComponent.FramesToWait > 0)
                {
                    movementComponent.FramesToWait -= 1;
                    ComponentRepo.MovementComponents[entityId] = movementComponent;
                    continue;
                }

                if (!ComponentRepo.EnergyComponents.TryGetValue(entityId, out var actionEnergyComponent))
                {
                    continue;
                }

                if (actionEnergyComponent.CurrentEnergy < movementComponent.EnergyToMove)
                {
                    continue;
                }

                var transformComponentNullable = ComponentRepo.TransformComponents[entityId];
                if (transformComponentNullable == null)
                {
                    continue;
                }
                var transformComponent = transformComponentNullable.Value;

                SetNextMapPosition(entityId, movementComponent, transformComponent);
                TryMoveToNextMapPosition(entityId, movementComponent, actionEnergyComponent);
            }
        }

        /// <summary>
        /// Determine the next valid map node to move the entity to based upon the specified movement type
        /// </summary>
        public void SetNextMapPosition(int entityId, MovementComponent movementComponent, TransformComponent transformComponent)
        {
            if (movementComponent.MovementMode == MovementMode.Random)
            {
                SetRandomMapPosition(entityId, movementComponent, transformComponent);

            }
            else if (movementComponent.MovementMode == MovementMode.SeekTarget)
            {
                //TODO if has target, path to it.
            }
        }

        /// <summary>
        /// Attempt to move the entity to the selected mapNode.
        /// MoveEntity checks for collision in case other entities have already moved into the selected space.
        /// Energy from the EnergyComponent is consumed during movement, not during path selection.
        /// </summary>
        public void TryMoveToNextMapPosition(int entityId, MovementComponent movementComponent, EnergyComponent actionEnergyComponent)
        {
            if (movementComponent.NextMapPosition != null)
            {
                world.MoveEntity(entityId, movementComponent.NextMapPosition.Value);
                actionEnergyComponent.CurrentEnergy -= movementComponent.EnergyToMove;
                ComponentRepo.EnergyComponents[entityId] = actionEnergyComponent;
            }
        }

        /// <summary>
        /// Determines if an entity can move to a cube of selected mapNodes.
        /// Basic collision detection is run to determine if any of the mapNodes are already occupied.
        /// Each map node can contain a single entity.
        /// </summary>
        public bool CanMove(CubeInt newPosition, int entityId)
        {
            for (var x = newPosition.Position.X; x < newPosition.Position.X + newPosition.Size.X; x++)
            {
                for (var y = newPosition.Position.Y; y < newPosition.Position.Y + newPosition.Size.Y; y++)
                {
                    for (var z = newPosition.Position.Z; z < newPosition.Position.Z + newPosition.Size.Z; z++)
                    {
                        var mapNodeEntityId = world.Map.MapNodes[x, y, z].EntityId;
                        if (mapNodeEntityId != null && mapNodeEntityId != entityId)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        public void SetTarget()
        {

        }

        /// <summary>
        /// Select a random neighboring map node to move to.
        /// The map node must be on the map and not currently occupied.
        /// All valid options are equally likely to be chosen.
        /// </summary>
        public void SetRandomMapPosition(int entityId, MovementComponent movementComponent, TransformComponent transformComponent)
        {
            short framesToWaitIfNoOptions = 10;

            if (movementComponent.NextMapPosition == null || transformComponent.Position == movementComponent.NextMapPosition.Value)
            {
                var mapNode = world.Map.MapNodes[transformComponent.Position.X, transformComponent.Position.Y, transformComponent.Position.Z];

                var movementCandidates = new Vector3Int[4];
                var movementCandidateCount = 0;
                
                if (mapNode.NeighborNorth != null)
                {
                    var newPositionCube = new CubeInt(mapNode.NeighborNorth.Value, transformComponent.Size);
                    if (CanMove(newPositionCube, entityId))
                    {
                        movementCandidates[movementCandidateCount++] = newPositionCube.Position;
                    }
                }
                if (mapNode.NeighborEast != null)
                {
                    var newPositionCube = new CubeInt(mapNode.NeighborEast.Value, transformComponent.Size);
                    if (CanMove(newPositionCube, entityId))
                    {
                        movementCandidates[movementCandidateCount++] = newPositionCube.Position;
                    }
                }
                if (mapNode.NeighborSouth != null)
                {
                    var newPositionCube = new CubeInt(mapNode.NeighborSouth.Value, transformComponent.Size);
                    if (CanMove(newPositionCube, entityId))
                    {
                        movementCandidates[movementCandidateCount++] = newPositionCube.Position;
                    }
                }
                if (mapNode.NeighborWest != null)
                {
                    var newPositionCube = new CubeInt(mapNode.NeighborWest.Value, transformComponent.Size);
                    if (CanMove(newPositionCube, entityId))
                    {
                        movementCandidates[movementCandidateCount++] = newPositionCube.Position;
                    }
                }

                if (movementCandidateCount> 0)
                {
                    movementComponent.NextMapPosition = movementCandidates[randomizer.Next(movementCandidateCount)];
                    ComponentRepo.MovementComponents[entityId] = movementComponent;
                }
                else
                {
                    movementComponent.FramesToWait = framesToWaitIfNoOptions;
                }
            }
        }
    }
}
