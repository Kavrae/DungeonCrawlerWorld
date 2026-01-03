using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Utilities;
using Microsoft.Xna.Framework;
using System;

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


        short framesToWaitIfNoOptions = 10;

        //Working Variables
        MapNode currentMapNode;
        MapNode candidateMapNode;
        int xCoordinate;
        int yCoordinate;
        int zCoordinate;

        TransformComponent movementCandidate;
        byte movementCandidateCount;
        private Vector3Int[] _movementCandidates;

        public MovementSystem()
        {
            var dataAccessService = GameServices.GetService<DataAccessService>();
            world = dataAccessService.RetrieveWorld();

            randomizer = new Random();
            movementCandidate = new TransformComponent();
            _movementCandidates = new Vector3Int[4];
        }

        public void Update(GameTime gameTime)
        {
            var movementComponentSet = ComponentRepo.MovementComponents;
            var entityIds = movementComponentSet.EntityIds;
            var components = movementComponentSet.Components;
            var count = movementComponentSet.Count;

            for (var movementIndex = 0; movementIndex < count; movementIndex++)
            {
                var entityId = entityIds[movementIndex];
                var movementComponent = components[movementIndex];

                if (movementComponent.FramesToWait > 0)
                {
                    movementComponent.FramesToWait -= 1;
                    movementComponentSet.Save(entityId, movementComponent);
                    continue;
                }

                if (!ComponentRepo.EnergyComponents.TryGetValue(entityId, out var energyComponent))
                {
                    continue;
                }

                if (energyComponent.CurrentEnergy < movementComponent.EnergyToMove)
                {
                    continue;
                }

                if (ComponentRepo.TransformComponentPresent[entityId] == 0)
                {
                    continue;
                }
                var transformComponent = ComponentRepo.TransformComponents[entityId];

                SetNextMapPosition(entityId, movementComponent, transformComponent);
                TryMoveToNextMapPosition(entityId, movementComponent, energyComponent, transformComponent);
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
        public void TryMoveToNextMapPosition(int entityId, MovementComponent movementComponent, EnergyComponent energyComponent, TransformComponent transformComponent)
        {
            if (movementComponent.NextMapPosition != null)
            {
                world.MoveEntity(entityId, movementComponent.NextMapPosition.Value, transformComponent);

                energyComponent.CurrentEnergy -= movementComponent.EnergyToMove;
                ComponentRepo.SaveEnergyComponent(entityId, energyComponent, ComponentSaveMode.Overwrite);
            }
        }

        /// <summary>
        /// Determines if an entity can move to a cube of selected mapNodes.
        /// Basic collision detection is run to determine if any of the mapNodes are already occupied.
        /// Each map node can contain a single entity.
        /// </summary>
        public bool CanMove(TransformComponent newTransform, int entityId)
        {
            for (xCoordinate = newTransform.Position.X; xCoordinate < newTransform.Position.X + newTransform.Size.X; xCoordinate++)
            {
                for (yCoordinate = newTransform.Position.Y; yCoordinate < newTransform.Position.Y + newTransform.Size.Y; yCoordinate++)
                {
                    for (zCoordinate = newTransform.Position.Z; zCoordinate < newTransform.Position.Z + newTransform.Size.Z; zCoordinate++)
                    {
                        candidateMapNode = world.Map.MapNodes[xCoordinate, yCoordinate, zCoordinate];
                        if (candidateMapNode.EntityId != null && candidateMapNode.EntityId != entityId)
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
            if (movementComponent.NextMapPosition == null || transformComponent.Position == movementComponent.NextMapPosition.Value)
            {
                movementCandidateCount = 0;
                movementCandidate.Size = transformComponent.Size;

                currentMapNode = world.Map.MapNodes[transformComponent.Position.X, transformComponent.Position.Y, transformComponent.Position.Z];

                if (currentMapNode.NeighborNorth != null)
                {
                    movementCandidate.Position = currentMapNode.NeighborNorth.Value;
                    if (CanMove(movementCandidate, entityId))
                    {
                        _movementCandidates[movementCandidateCount++] = movementCandidate.Position;
                    }
                }
                if (currentMapNode.NeighborEast != null)
                {
                    movementCandidate.Position = currentMapNode.NeighborEast.Value;
                    if (CanMove(movementCandidate, entityId))
                    {
                        _movementCandidates[movementCandidateCount++] = movementCandidate.Position;
                    }
                }
                if (currentMapNode.NeighborSouth != null)
                {
                    movementCandidate.Position = currentMapNode.NeighborSouth.Value;
                    if (CanMove(movementCandidate, entityId))
                    {
                        _movementCandidates[movementCandidateCount++] = movementCandidate.Position;
                    }
                }
                if (currentMapNode.NeighborWest != null)
                {
                    movementCandidate.Position = currentMapNode.NeighborWest.Value;
                    if (CanMove(movementCandidate, entityId))
                    {
                        _movementCandidates[movementCandidateCount++] = movementCandidate.Position;
                    }
                }

                if (movementCandidateCount > 0)
                {
                    movementComponent.NextMapPosition = _movementCandidates[randomizer.Next(movementCandidateCount)];
                    ComponentRepo.SaveMovementComponent(entityId, movementComponent, ComponentSaveMode.Overwrite);
                }
                else
                {
                    movementComponent.FramesToWait = framesToWaitIfNoOptions;
                }
            }
        }
    }
}
