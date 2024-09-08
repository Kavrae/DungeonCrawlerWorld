using System;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.EntityComponents
{
    public class Actionable : IEntityComponent
    {
        public int EnergyRecharge {  get; set; }
        public int MaximumEnergy { get; set; }

        public Actionable(int energyRecharge, int maximumEnergy) : base()
        {
            EnergyRecharge = energyRecharge;
            MaximumEnergy = maximumEnergy;
        }

        public override void Update(GameTime gameTime)
        {
            if (Entity.EntityData.CurrentEnergy < MaximumEnergy)
            {
                Entity.EntityData.CurrentEnergy = Math.Clamp(Entity.EntityData.CurrentEnergy + EnergyRecharge, 0, MaximumEnergy);
            }
        }
    }
}
