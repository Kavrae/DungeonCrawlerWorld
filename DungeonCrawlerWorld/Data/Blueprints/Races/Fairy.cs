using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.Data.Blueprints.Races
{
    public class Fairy : IBlueprint
    {
        public static void Build(int entityId)
        {
            var randomizer = new Random();
            var personalNameOptions = new string[]
            {
                "TestName1",
                "TestName2"
            };

            _ = new RaceComponent(entityId,
                new("c22f6339-0a56-4528-b818-10052a831dc5"),
                "Fairy",
                "TODO fairy description. Their magic is stored in their wings.");
            _ = new GlyphComponent(entityId, "f", Color.DeepPink, new Point(4, 0));
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Flying), new Utilities.Vector3Int(1, 1, 1));
            _ = new EnergyComponent(entityId, 0, 10, 100);
            _ = new HealthComponent(entityId, 100, 5, 100);
            _ = new MovementComponent(entityId, MovementMode.Random, 20);
            _ = new DisplayTextComponent(
                   entityId,
                   personalNameOptions[randomizer.Next(personalNameOptions.Length)],
                   string.Empty);
        }
    }
}
