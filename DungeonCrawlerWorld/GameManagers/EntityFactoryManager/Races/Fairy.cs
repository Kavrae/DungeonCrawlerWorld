using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Fairy : IBaseFactoryTemplate
    {
        public override Guid Id => new("8e4791cf-57ff-4251-b9c6-b1f8f00245d3");
        public override string Name => "Fairy";

        public override string Description => "TODO fairy description. Their magic is stored in their wings.";

        private static string[] Names => new[]
        {
            "Astrid",
            "Fairy2"
        };
        public override void Build(Guid entityId)
        {
            var randomizer = new Random();
            var personalName = Names[randomizer.Next(0, Names.Length)];

            ComponentRepo.Races.Remove(entityId);
            ComponentRepo.Races.Add(entityId, Id);
            _ = new DisplayTextComponent(entityId, personalName, "", Name, Description, "", "");
            _ = new GlyphComponent(entityId, "f", Color.DeepPink, new Point(4, 0));
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Flying), new Utilities.Vector3Int(1, 1, 1));
            _ = new EnergyComponent(entityId, 0, 10, 100);
            _ = new HealthComponent(entityId, 100, 5, 100);
            _ = new MovementComponent(entityId, MovementMode.Random, 20);
        }
    }
}
