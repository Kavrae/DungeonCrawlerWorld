using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Utilities;
using Microsoft.Xna.Framework;
using System;

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
            var description = "Small, green and smart. What Goblins lack in physical strength they make up in pure spunk.";

            ComponentRepo.AddRaceComponent(
                entityId,
                new RaceComponent
                (
                    new("1aa7b1c2-0b54-4745-b616-8aaff734a7d6"),
                    "Goblin",
                    description));

            ComponentRepo.SaveDisplayTextComponent(
                entityId,
                new DisplayTextComponent(
                    $"{personalNameOptions[randomizer.Next(personalNameOptions.Length)]} : Goblin",
                    description),
                ComponentSaveMode.Merge);

            ComponentRepo.SaveEnergyComponent(
                entityId,
                new EnergyComponent(
                    (short)currentEnergy,
                    (short)energyRecharge,
                    (short)maxEnergy),
                ComponentSaveMode.Merge);

            ComponentRepo.SaveGlyphComponent(
                entityId,
                new GlyphComponent("g", Color.DarkGreen, new Point(3, -2)),
                ComponentSaveMode.Merge);

            ComponentRepo.SaveHealthComponent(
                entityId,
                new HealthComponent(100, 10, 200),
                ComponentSaveMode.Merge);

            ComponentRepo.SaveMovementComponent(
                entityId,
                new MovementComponent(MovementMode.Random, 20, null, null),
                ComponentSaveMode.Merge);

            ComponentRepo.SaveTransformComponent(
                entityId,
                new TransformComponent(
                    new Vector3Int(-1, -1, (int)MapHeight.Standing),
                    new Vector3Int(1, 1, 1)),
                ComponentSaveMode.Merge);
        }
    }
}