using System;
using System.Collections.Generic;

namespace DungeonCrawlerWorld.Components
{
    public static class ComponentRepo
    {
        public static Dictionary<Guid, BackgroundComponent> BackgroundComponents;
        public static Dictionary<Guid, DisplayTextComponent> DisplayTextComponents;
        public static Dictionary<Guid, EnergyComponent> EnergyComponents;
        public static Dictionary<Guid, HealthComponent> HealthComponents;
        public static Dictionary<Guid, GlyphComponent> GlyphComponents;
        public static Dictionary<Guid, ClassGlyphComponent> ClassGlyphComponents;
        public static Dictionary<Guid, MovementComponent> MovementComponents;
        public static Dictionary<Guid, Guid> Races;
        public static Dictionary<Guid, TransformComponent> TransformComponents;

        static ComponentRepo()
        {
            BackgroundComponents = new Dictionary<Guid, BackgroundComponent>();
            DisplayTextComponents = new Dictionary<Guid, DisplayTextComponent>();
            GlyphComponents = new Dictionary<Guid, GlyphComponent>();
            ClassGlyphComponents = new Dictionary<Guid, ClassGlyphComponent>();
            EnergyComponents = new Dictionary<Guid, EnergyComponent>();
            HealthComponents = new Dictionary<Guid, HealthComponent>();
            MovementComponents = new Dictionary<Guid, MovementComponent>();
            Races = new Dictionary<Guid, Guid>();
            TransformComponents = new Dictionary<Guid, TransformComponent>();
        }
    }
}
