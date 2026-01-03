using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Utilities;
using Microsoft.Xna.Framework;
using System;

namespace DungeonCrawlerWorld.Data.Blueprints.Races
{
    public class Fairy : IBlueprint
    {
        private static readonly Guid raceId = new("c22f6339-0a56-4528-b818-10052a831dc5");
        private static readonly string raceName = "Fairy";

        private static readonly string[] personalNameOptions =
        [
            "Fairy1",
            "Fairy2"
        ];

        private static readonly string description = "TODO fairy description. Their magic is stored in their wings.";

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
                new RaceComponent(
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
                new GlyphComponent("f", Color.DeepPink, new Point(4, 0)),
                ComponentSaveMode.Merge);

            ComponentRepo.SaveHealthComponent(
                entityId,
                new HealthComponent(100, 5, 100),
                ComponentSaveMode.Merge);

            ComponentRepo.SaveMovementComponent(
                entityId,
                new MovementComponent(MovementMode.Random, 15, null, null),
                ComponentSaveMode.Merge);

            ComponentRepo.SaveTransformComponent(
                entityId,
                new TransformComponent(
                    new Vector3Int(0, 0, (int)MapHeight.Flying),
                    new Vector3Byte(1, 1, 1)),
                ComponentSaveMode.Merge);
        }
    }
}
