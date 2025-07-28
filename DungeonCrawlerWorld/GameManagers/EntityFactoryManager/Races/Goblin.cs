using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Goblin : RaceComponent
    {
        private static string[] PersonalNames => new[]
        {
            "TestName1",
            "TestName2"
        };

        public Goblin() : this(Guid.NewGuid()){}
        public Goblin(Guid entityId)
        {
            Name = "Goblin";
            Description = "Small, green and smart. What Goblins lack in physical strength they make up in pure spunk.";

            var randomizer = new Random();
            PersonalName = PersonalNames[randomizer.Next(0, PersonalNames.Length)];

            var maxEnergy = 100;
            var currentEnergy = randomizer.Next(0, maxEnergy);
            var energyRecharge = randomizer.Next(10, 12);

            _ = new GlyphComponent(entityId, "g", Color.DarkGreen, new Point(3, -2));
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(-1, -1, (int)MapHeight.Standing), new Utilities.Vector3Int(1, 1, 1));
            _ = new EnergyComponent(entityId, (short)currentEnergy, (short)energyRecharge, (short)maxEnergy);
            _ = new HealthComponent(entityId, 100, 5, 100);
            _ = new MovementComponent(entityId, MovementMode.Random, 20);

            base.Build(entityId);
        }
    }
}
