using System;
using System.Collections.Generic;
using System.Linq;
using DungeonCrawlerWorld.EntityComponents;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DungeonCrawlerWorld.Data
{
    public abstract class Entity
    {
        public EntityData EntityData;

        public List<Guid> ComponentIds;
        private List<IEntityComponent> _components;

        public Entity()
        {
            _components = new List<IEntityComponent>();
            ComponentIds = new List<Guid>();
            EntityData ??= new EntityData();
        }
        
        public Entity(EntityData entityData) : this()
        {
            EntityData = entityData;
        }

        public void Update(GameTime gameTime)
        {
            foreach(var component in _components)
            {
                component.Update(gameTime);
            }
        }

        //TODO DisplayComponent should be doing the drawing based on the Displayable EntityComponent. Remove Draw from Entity and its components.
        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            foreach (var component in _components)
            {
                component.Draw(gameTime, spriteBatch);
            }
        }

        public void AddComponent<T>(T newComponent) where T : IEntityComponent
        {
            RemoveComponent<T>(newComponent);
            newComponent.SetEntity(this);
            _components.Add(newComponent);
            ComponentIds.Add(newComponent.Id);
        }

        public void RemoveComponent<T>(T component) where T : IEntityComponent
        {
            var matchedComponent = _components.FirstOrDefault(component => component is T);
            if (matchedComponent != null)
            {
                _components.Remove(matchedComponent);
                ComponentIds.Remove(matchedComponent.Id);
            }
        }

        public IEntityComponent GetComponent<T>() where T: IEntityComponent
        {
            return _components.FirstOrDefault(component => component is T);
        }

        public IEntityComponent GetComponent(Guid componentId)
        {
            return _components.FirstOrDefault(component => component.Id == componentId);
        }
    }
}
