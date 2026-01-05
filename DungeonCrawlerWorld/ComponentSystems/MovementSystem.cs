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

        private readonly World world;

        private readonly Random randomizer;


        short framesToWaitIfNoOptions = 10;
        private Vector3Byte TransformSize1 = new(1, 1, 1);

        byte movementCandidateCount;
        private Vector3Int[] _movementCandidates;

        public MovementSystem()
        {
            var dataAccessService = GameServices.GetService<DataAccessService>();
            world = dataAccessService.RetrieveWorld();

            randomizer = new Random();
            _movementCandidates = new Vector3Int[4];
        }

        public void Update(GameTime gameTime)
        {
            var energyComponentSet = ComponentRepo.EnergyComponents;
            var movementComponentSet = ComponentRepo.MovementComponents;
            var transformComponentSet = ComponentRepo.TransformComponents;

            var entityIds = movementComponentSet.AllEntityIds;
            var count = entityIds.Length;

            ref var movementComponents = ref movementComponentSet.AllComponents;

            for (var movementIndex = 0; movementIndex < count; movementIndex++)
            {
                var entityId = entityIds[movementIndex];

                if (!transformComponentSet.HasComponent(entityId) ||
                    !energyComponentSet.HasComponent(entityId))
                {
                    continue;
                }

                ref var energyComponent = ref energyComponentSet.Get(entityId);
                ref var movementComponent = ref movementComponents[movementIndex];
                ref var transformComponent = ref transformComponentSet.Get(entityId);

                if (!world.IsOnMap(transformComponent.Position))
                {
                    continue;
                }

                if (energyComponent.CurrentEnergy < movementComponent.EnergyToMove)
                {
                    continue;
                }

                if (movementComponent.FramesToWait > 0)
                {
                    movementComponent.FramesToWait -= 1;
                    continue;
                }

                if (movementComponent.NextMapPosition == null || transformComponent.Position == movementComponent.NextMapPosition.Value)
                {
                    SetNextMapPosition(entityId, ref movementComponent, transformComponent);
                }

                if (movementComponent.NextMapPosition != null)
                {
                    TryMoveToNextMapPosition(entityId, movementComponent, ref energyComponent, ref transformComponent);
                }
            }
        }

        /// <summary>
        /// Determine the next valid map node to move the entity to based upon the specified movement type
        /// </summary>
        public void SetNextMapPosition(int entityId, ref MovementComponent movementComponent, TransformComponent transformComponent)
        {
            if (movementComponent.MovementMode == MovementMode.Random)
            {
                SetRandomMapPosition(entityId, ref movementComponent, transformComponent);
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
        public void TryMoveToNextMapPosition(int entityId, MovementComponent movementComponent, ref EnergyComponent energyComponent, ref TransformComponent transformComponent)
        {
            world.MoveEntity(entityId, movementComponent.NextMapPosition.Value, ref transformComponent);

            energyComponent.CurrentEnergy -= movementComponent.EnergyToMove;
        }

        public bool CanMove(Vector3Int position, Vector3Byte size, int entityId)
        {
            MapNode mapNode;
            if (size == TransformSize1)
            {
                mapNode = world.Map.GetMapNode(position);
                return mapNode.EntityId == -1 || mapNode.EntityId == entityId;
            }
            else
            {
                for (var x = position.X; x < position.X + size.X; x++)
                {
                    for (var y = position.Y; y < position.Y + size.Y; y++)
                    {
                        for (var z = position.Z; z < position.Z + size.Z; z++)
                        {
                            if (!world.IsOnMap(new Vector3Int(x, y, z)))
                            {
                                var checkPosition = new Vector3Int(x, y, z);
                                mapNode = world.Map.GetMapNode(checkPosition);
                                if (mapNode.EntityId != -1 && mapNode.EntityId != entityId)
                                {
                                    return false;
                                }
                            }
                        }
                    }
                }
                return true;
            }
        }

        public void SetTarget()
        {

        }

        /// <summary>
        /// Select a random neighboring map node to move to.
        /// The map node must be on the map and not currently occupied.
        /// All valid options are equally likely to be chosen.
        /// </summary>
        public void SetRandomMapPosition(int entityId, ref MovementComponent movementComponent, TransformComponent transformComponent)
        {
            var size = transformComponent.Size;
            movementCandidateCount = 0;

            if (transformComponent.Position.Y > 0)
            {
                var northPosition = new Vector3Int(transformComponent.Position.X, transformComponent.Position.Y - 1, transformComponent.Position.Z);
                if (CanMove(northPosition, size, entityId))
                {
                    _movementCandidates[movementCandidateCount++] = northPosition;
                }
            }
            if (transformComponent.Position.Y < world.Map.Size.Y - 1)
            {
                var southPosition = new Vector3Int(transformComponent.Position.X, transformComponent.Position.Y + 1, transformComponent.Position.Z);
                if (CanMove(southPosition, size, entityId))
                {
                    _movementCandidates[movementCandidateCount++] = southPosition;
                }
            }
            if (transformComponent.Position.X > 0)
            {
                var westPosition = new Vector3Int(transformComponent.Position.X - 1, transformComponent.Position.Y, transformComponent.Position.Z);
                if (CanMove(westPosition, size, entityId))
                {
                    _movementCandidates[movementCandidateCount++] = westPosition;
                }
            }
            if (transformComponent.Position.X < world.Map.Size.X - 1)
            {
                var eastPosition = new Vector3Int(transformComponent.Position.X + 1, transformComponent.Position.Y, transformComponent.Position.Z);
                if (CanMove(eastPosition, size, entityId))
                {
                    _movementCandidates[movementCandidateCount++] = eastPosition;
                }
            }

            if (movementCandidateCount > 0)
            {
                movementComponent.NextMapPosition = _movementCandidates[randomizer.Next(movementCandidateCount)];
            }
            else
            {
                movementComponent.FramesToWait = framesToWaitIfNoOptions;
            }
        }
    }
}
