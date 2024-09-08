using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;

//TODO reference to transform
namespace DungeonCrawlerWorld.EntityComponents
{
    public enum MovementMode
    {
        Stationary,
        Random,
        SeekTarget
    }

    public class Movable : IEntityComponent 
    {
        private DataAccess _dataAccess;

        private Vector2 _mapSize;
        private Random _randomizer;

        public MovementMode MovementMode;

        public int EnergyToMove;

        public Point? TargetMapPosition;
        public Point? NextMapPosition;

        public Movable(MovementMode movementMode, int energyToMove) : base()
        {
            MovementMode = movementMode;
            EnergyToMove = energyToMove;

            var dataAccessService = GameServices.GetService<DataAccessService>();
            _dataAccess = dataAccessService.Connect();

            _mapSize = _dataAccess.RetrieveMapSize();
            _randomizer = new Random();
        }

        public override void Update(GameTime gameTime)
        {
            SetNextMapPosition();
            TryMoveToNextMapPosition();
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch) { }

        public void SetNextMapPosition()
        {
            if (MovementMode == MovementMode.Stationary)
            {
                //Do nothing
            }
            else if (MovementMode == MovementMode.Random)
            {
                if(Entity.EntityData.MapPosition == null)
                {
                    Entity.EntityData.MapPosition = new Point(0, 0);
                }

                if ( NextMapPosition == null || Entity.EntityData.MapPosition == NextMapPosition)
                {
                    var validRandomMovementTargets = new List<Point>();
                    if (Entity.EntityData.MapPosition.Value.X > 0)
                    {
                        var newPositionCandidate = new Point(Entity.EntityData.MapPosition.Value.X - 1, Entity.EntityData.MapPosition.Value.Y);
                        var mapNode = _dataAccess.RetrieveMapNode(newPositionCandidate);
                        if( !mapNode.Entities?.Any() == true)
                        {
                            validRandomMovementTargets.Add(newPositionCandidate);
                        }
                    }
                    if (Entity.EntityData.MapPosition.Value.X < _mapSize.X - 1)
                    {
                        var newPositionCandidate = new Point(Entity.EntityData.MapPosition.Value.X + 1, Entity.EntityData.MapPosition.Value.Y);
                        var mapNode = _dataAccess.RetrieveMapNode(newPositionCandidate);
                        if (!mapNode.Entities?.Any() == true)
                        {
                            validRandomMovementTargets.Add(newPositionCandidate);
                        }
                    }
                    if (Entity.EntityData.MapPosition.Value.Y > 0)
                    {
                        var newPositionCandidate = new Point(Entity.EntityData.MapPosition.Value.X, Entity.EntityData.MapPosition.Value.Y - 1);
                        var mapNode = _dataAccess.RetrieveMapNode(newPositionCandidate);
                        if (!mapNode.Entities?.Any() == true)
                        {
                            validRandomMovementTargets.Add(newPositionCandidate);
                        }
                    }
                    if (Entity.EntityData.MapPosition.Value.Y < _mapSize.Y - 1)
                    {
                        var newPositionCandidate = new Point(Entity.EntityData.MapPosition.Value.X, Entity.EntityData.MapPosition.Value.Y + 1);
                        var mapNode = _dataAccess.RetrieveMapNode(newPositionCandidate);
                        if (!mapNode.Entities?.Any() == true)
                        {
                            validRandomMovementTargets.Add(newPositionCandidate);
                        }
                    }

                    NextMapPosition = validRandomMovementTargets[_randomizer.Next(0, validRandomMovementTargets.Count)];
                }
            }
            else if (MovementMode == MovementMode.SeekTarget)
            {
                //TODO if has target, path to it.
            }
        }

        public void TryMoveToNextMapPosition()
        {
            if (NextMapPosition != null && Entity.EntityData.CurrentEnergy >= EnergyToMove)
            {
                _dataAccess.MoveEntity(Entity.EntityData.Id, NextMapPosition.Value);

                //TODO change to SpendEnergy on the energy module. Then other things can trigger off it.
                Entity.EntityData.CurrentEnergy -= EnergyToMove;
            }
        }

        public void SetTarget()
        {

        }
    }
}
