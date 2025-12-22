using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Utilities;
using Microsoft.Xna.Framework;
using System;

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
            var name = "Fairy";
            var description = "TODO fairy description. Their magic is stored in their wings.";

            ComponentRepo.AddRaceComponent(
                entityId,
                new RaceComponent(
                    new("c22f6339-0a56-4528-b818-10052a831dc5"),
                    name,
                    description));

            ComponentRepo.SaveDisplayTextComponent(
                entityId,
                new DisplayTextComponent(
                    $"{personalNameOptions[randomizer.Next(personalNameOptions.Length)]} : {name}",
                    description),
                ComponentSaveMode.Merge);

            ComponentRepo.SaveEnergyComponent(
                entityId,
                new EnergyComponent(0, 10, 100),
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
                    new Vector3Int(1, 1, 1)),
                ComponentSaveMode.Merge);
        }
    }
}
