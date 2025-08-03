using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Fairy : RaceComponent
    {
        public override Guid RaceId => new("c22f6339-0a56-4528-b818-10052a831dc5");

        private static string[] Names => new[]
        {
            "Astrid",
            "Fairy2"
        };

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
