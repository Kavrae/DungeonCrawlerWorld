using System;

namespace DungeonCrawlerWorld.Components
{
    //TODO Unused. Sort out later.
    public enum ColliderType : byte
    {
        None,
        Terrain,
        Impassable,
        Standard
    }

    public struct CollisionComponent
    {
        public Guid EntityId { get; set; }
        public ColliderType ColliderType { get; set; }

        public CollisionComponent(Guid entityId) : this( entityId, ColliderType.Standard){}
        public CollisionComponent( Guid entityId, ColliderType colliderType)
        {
            EntityId = entityId;
            ColliderType = colliderType;

            ComponentRepo.CollisionComponents.Remove(entityId);
            ComponentRepo.CollisionComponents.Add(entityId, this);
        }
        public override string ToString()
        {
            return $"Collider Type : {ColliderType}";
        }
    }
}
