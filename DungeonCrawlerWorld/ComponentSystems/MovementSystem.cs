using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.ComponentSystems
{
    /// <summary>
    /// Movable System
    /// Responsible for selecting the next MapNode to move to and then moving towards that node based upon the set movement type
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
            foreach (var keyComponent in ComponentRepo.MovementComponents)
            {
                var movementComponent = keyComponent.Value;

                if (movementComponent.FramesToWait <= 0)
                {
                    if (ComponentRepo.EnergyComponents.TryGetValue(keyComponent.Key, out EnergyComponent actionEnergyComponent)
                        && actionEnergyComponent.CurrentEnergy >= movementComponent.EnergyToMove
                        && ComponentRepo.TransformComponents.TryGetValue(keyComponent.Key, out TransformComponent transformComponent))
                    {
                        SetNextMapPosition(movementComponent, transformComponent);
                        TryMoveToNextMapPosition(movementComponent, actionEnergyComponent);
                    }
                }
                else
                {
                    movementComponent.FramesToWait -= 1;
                    ComponentRepo.MovementComponents[movementComponent.EntityId] = movementComponent;
                }
            }
        }

        public void SetNextMapPosition(MovementComponent movementComponent, TransformComponent transformComponent)
        {
            if (movementComponent.MovementMode == MovementMode.Random)
            {
                SetRandomMapPosition(movementComponent, transformComponent);

            }
            else if (movementComponent.MovementMode == MovementMode.SeekTarget)
            {
                //TODO if has target, path to it.
            }
        }

        public void TryMoveToNextMapPosition(MovementComponent movementComponent, EnergyComponent actionEnergyComponent)
        {
            if (movementComponent.NextMapPosition != null)
            {
                world.MoveEntity(movementComponent.EntityId, movementComponent.NextMapPosition.Value);
                actionEnergyComponent.CurrentEnergy -= movementComponent.EnergyToMove;
                ComponentRepo.EnergyComponents[actionEnergyComponent.EntityId] = actionEnergyComponent;
            }
        }
        public bool CanMove(CubeInt newPosition, Guid entityId)
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

        public void SetRandomMapPosition(MovementComponent movementComponent, TransformComponent transformComponent)
        {
            short framesToWaitIfNoOptions = 10;

            if (movementComponent.NextMapPosition == null || transformComponent.Position == movementComponent.NextMapPosition.Value)
            {
                var mapNode = world.Map.MapNodes[transformComponent.Position.X, transformComponent.Position.Y, transformComponent.Position.Z];

                var validRandomMovementTargets = new List<Vector3Int>();
                if (mapNode.NeighborNorth != null)
                {
                    var newPositionCube = new CubeInt(mapNode.NeighborNorth.Value, transformComponent.Size);
                    if (CanMove(newPositionCube, movementComponent.EntityId))
                    {
                        validRandomMovementTargets.Add(newPositionCube.Position);
                    }
                }
                if (mapNode.NeighborEast != null)
                {
                    var newPositionCube = new CubeInt(mapNode.NeighborEast.Value, transformComponent.Size);
                    if (CanMove(newPositionCube, movementComponent.EntityId))
                    {
                        validRandomMovementTargets.Add(newPositionCube.Position);
                    }
                }
                if (mapNode.NeighborSouth != null)
                {
                    var newPositionCube = new CubeInt(mapNode.NeighborSouth.Value, transformComponent.Size);
                    if (CanMove(newPositionCube, movementComponent.EntityId))
                    {
                        validRandomMovementTargets.Add(newPositionCube.Position);
                    }
                }
                if (mapNode.NeighborWest != null)
                {
                    var newPositionCube = new CubeInt(mapNode.NeighborWest.Value, transformComponent.Size);
                    if (CanMove(newPositionCube, movementComponent.EntityId))
                    {
                        validRandomMovementTargets.Add(newPositionCube.Position);
                    }
                }

                if (validRandomMovementTargets.Any())
                {
                    movementComponent.NextMapPosition = validRandomMovementTargets[randomizer.Next(0, validRandomMovementTargets.Count)];
                    ComponentRepo.MovementComponents[movementComponent.EntityId] = movementComponent;
                }
                else
                {
                    movementComponent.FramesToWait = framesToWaitIfNoOptions;
                }
            }
        }
    }
}
