using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Fairy : IBaseFactoryTemplate
    {
        private static string RaceName => "Fairy";

        private static string Description => "TODO fairy description. Their magic is stored in their wings.";

        private static string[] Names => new[]
        {
            "Astrid",
            "Fairy2"
        };
        public override void Build(Guid entityId)
        {
            var randomizer = new Random();
            var personalName = Names[randomizer.Next(0, Names.Length)];
            _ = new DisplayTextComponent(entityId, $"{personalName} - {RaceName}", Description);
            _ = new GlyphComponent(entityId, "f", Color.DeepPink, new Point(4, 0));
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Flying), new Utilities.Vector3Int(1, 1, 1));
            _ = new EnergyComponent(entityId, 0, 10, 100);
            _ = new MovementComponent(entityId, MovementMode.Random, 20);
        }
    }
}
