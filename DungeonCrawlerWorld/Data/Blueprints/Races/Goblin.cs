using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Utilities;
using Microsoft.Xna.Framework;
using System;

namespace DungeonCrawlerWorld.Data.Blueprints.Races
{
    public class Goblin : IBlueprint
    {
        private static readonly Guid raceId = new("1aa7b1c2-0b54-4745-b616-8aaff734a7d6");
        private static readonly string raceName = "Goblin";

        private static readonly string[] personalNameOptions =
        [
            "TestName1",
            "TestName2"
        ];

        private static readonly string description = "Small, green and smart. What Goblins lack in physical strength they make up in pure spunk.";

        private static readonly short maximumEnergy = 100;
        private static readonly short minimumEnergyRecharge = 10;
        private static readonly short maximumEnergyRecharge = 100;

        //TODO randomizer service
        private static Random randomizer;

        public static void Build(int entityId)
        {
            randomizer = new Random();

            ComponentRepo.AddRaceComponent(
                entityId,
                new RaceComponent
                (
                    raceId,
                    raceName,
                    description));

            ComponentRepo.SaveDisplayTextComponent(
                entityId,
                new DisplayTextComponent(
                    $"{personalNameOptions[randomizer.Next(personalNameOptions.Length)]} : {raceName}",
                    description),
                ComponentSaveMode.Merge);

            ComponentRepo.SaveEnergyComponent(
                entityId,
                new EnergyComponent(
                    (short)randomizer.Next(0, maximumEnergy),
                    (short)randomizer.Next(minimumEnergyRecharge, maximumEnergyRecharge),
                    maximumEnergy),
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
                    new Vector3Byte(1, 1, 1)),
                ComponentSaveMode.Merge);
        }
    }
}