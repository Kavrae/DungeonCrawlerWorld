using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.Data.Blueprints.Races
{
    public class Goblin : IBlueprint
    {
        public static void Build(int entityId)
        { 
            var randomizer = new Random();
            var maxEnergy = 100;
            var currentEnergy = randomizer.Next(0, maxEnergy);
            var energyRecharge = randomizer.Next(10, 12);
            var personalNameOptions = new string[] 
            {
                "TestName1",
                "TestName2"
            };

            _ = new RaceComponent(entityId, 
                new("1aa7b1c2-0b54-4745-b616-8aaff734a7d6"), 
                "Goblin", 
                "Small, green and smart. What Goblins lack in physical strength they make up in pure spunk.");
            _ = new GlyphComponent(entityId, "g", Color.DarkGreen, new Point(3, -2));
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(-1, -1, (int)MapHeight.Standing), new Utilities.Vector3Int(1, 1, 1));
            _ = new EnergyComponent(entityId, (short)currentEnergy, (short)energyRecharge, (short)maxEnergy);
            _ = new HealthComponent(entityId, 100, 5, 100);
            _ = new MovementComponent(entityId, MovementMode.Random, 20);
            _ = new DisplayTextComponent(
                   entityId,
                   personalNameOptions[randomizer.Next(personalNameOptions.Length)],
                   string.Empty);
        }
    }
}