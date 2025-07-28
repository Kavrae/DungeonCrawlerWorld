using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Fairy : RaceComponent
    {
        private static string[] Names => new[]
        {
            "Astrid",
            "Fairy2"
        };

        //TODO this is backwards.  Base is called before child. So we can't assign things like name/description in the constructor then expect the base to assign it to the displayComponent.Fix
        //Maybe what's needed is to separate stats from behaviors?
        //Still need to be calculated values.
        //Maybe go back to a Build function in Race that's called at the end of each constructor? 
        //Figure out which components are common to all races vs per race
        public Fairy() : this(Guid.NewGuid()) { }
        public Fairy(Guid entityId)
        {
            Name = "Fairy"; ;
            Description = "TODO fairy description. Their magic is stored in their wings.";

            var randomizer = new Random();
            PersonalName = Names[randomizer.Next(0, Names.Length)];

            _ = new GlyphComponent(entityId, "f", Color.DeepPink, new Point(4, 0));
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Flying), new Utilities.Vector3Int(1, 1, 1));
            _ = new EnergyComponent(entityId, 0, 10, 100);
            _ = new HealthComponent(entityId, 100, 5, 100);
            _ = new MovementComponent(entityId, MovementMode.Random, 20);

            base.Build(entityId);
        }
    }
}
