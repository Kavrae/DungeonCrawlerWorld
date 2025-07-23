using System;
using System.Collections.Generic;

namespace DungeonCrawlerWorld.Components
{
    public static class ComponentRepo
    {
        public static Dictionary<Guid, ClassComponent> ClassComponents;
        public static Dictionary<Guid, ClassGlyphComponent> ClassGlyphComponents;
        public static Dictionary<Guid, BackgroundComponent> BackgroundComponents;
        public static Dictionary<Guid, DisplayTextComponent> DisplayTextComponents;
        public static Dictionary<Guid, EnergyComponent> EnergyComponents;
        public static Dictionary<Guid, GlyphComponent> GlyphComponents;
        public static Dictionary<Guid, HealthComponent> HealthComponents;
        public static Dictionary<Guid, MovementComponent> MovementComponents;
        public static Dictionary<Guid, RaceComponent> RaceComponents;
        public static Dictionary<Guid, TransformComponent> TransformComponents;

        static ComponentRepo()
        {
            BackgroundComponents = new Dictionary<Guid, BackgroundComponent>();
            ClassComponents = new Dictionary<Guid, ClassComponent>();
            ClassGlyphComponents = new Dictionary<Guid, ClassGlyphComponent>();
            DisplayTextComponents = new Dictionary<Guid, DisplayTextComponent>();
            EnergyComponents = new Dictionary<Guid, EnergyComponent>();
            GlyphComponents = new Dictionary<Guid, GlyphComponent>();
            HealthComponents = new Dictionary<Guid, HealthComponent>();
            MovementComponents = new Dictionary<Guid, MovementComponent>();
            RaceComponents = new Dictionary<Guid, RaceComponent>();
            TransformComponents = new Dictionary<Guid, TransformComponent>();
        }
    }
}
