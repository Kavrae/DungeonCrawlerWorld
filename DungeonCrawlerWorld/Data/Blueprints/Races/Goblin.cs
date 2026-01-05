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
        private static readonly short minimumEnergyRecharge = 5;
        private static readonly short maximumEnergyRecharge = 10;

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

            ComponentRepo.DisplayTextComponents.Add(
                entityId,
                new DisplayTextComponent(
                    $"{personalNameOptions[randomizer.Next(personalNameOptions.Length)]} : {raceName}",
                    description));

            ComponentRepo.EnergyComponents.Add(
                entityId,
                new EnergyComponent(
                    (short)randomizer.Next(0, maximumEnergy),
                    (short)randomizer.Next(minimumEnergyRecharge, maximumEnergyRecharge),
                    maximumEnergy));

            ComponentRepo.GlyphComponents.Add(
                entityId,
                new GlyphComponent("g", Color.DarkGreen, new Point(3, -2)));

            ComponentRepo.HealthComponents.Add(
                entityId,
                new HealthComponent(100, 10, 200));

            ComponentRepo.MovementComponents.Add(
                entityId,
                new MovementComponent(MovementMode.Random, 40, null, null));

            ComponentRepo.TransformComponents.Add(
                entityId,
                new TransformComponent(
                    new Vector3Int(-1, -1, (int)MapHeight.Standing),
                    new Vector3Byte(1, 1, 1)));
        }
    }
}