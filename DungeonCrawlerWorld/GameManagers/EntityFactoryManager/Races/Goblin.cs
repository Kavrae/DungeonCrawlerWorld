using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Goblin : IBaseFactoryTemplate
    {
        public override Guid Id => new("8e4791cf-57ff-4251-b9c6-b1f8f00245d3");
        public override string Name => "Goblin";

        public override string Description => "Small, green and smart. What Goblins lack in physical strength they make up in pure spunk.";

        private static string[] PersonalNames => new[]
        {
            "TestName1",
            "TestName2"
        };

        public override void Build(Guid entityId)
        {
            var randomizer = new Random();
            var personalName = PersonalNames[randomizer.Next(0, PersonalNames.Length)];
            
            var maxEnergy = 100;
            var currentEnergy = randomizer.Next(0, maxEnergy);
            var energyRecharge = randomizer.Next(10, 12);

            ComponentRepo.Races.Remove(entityId);
            ComponentRepo.Races.Add(entityId, Id);
            _ = new DisplayTextComponent(entityId, personalName, "", Name, Description, "", "");
            _ = new GlyphComponent(entityId, "g", Color.DarkGreen, new Point(3, -2));
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(-1, -1, (int)MapHeight.Standing), new Utilities.Vector3Int(1, 1, 1));
            _ = new EnergyComponent(entityId, (short)currentEnergy, (short)energyRecharge, (short)maxEnergy);
            _ = new HealthComponent(entityId, 100, 5, 100);
            _ = new MovementComponent(entityId, MovementMode.Random, 20);
        }
    }
}
