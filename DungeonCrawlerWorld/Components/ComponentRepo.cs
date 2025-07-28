using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonCrawlerWorld.Components
{
    public static class ComponentRepo
    {
        public static Dictionary<Guid, List<ClassComponent>> ClassComponents;
        public static Dictionary<Guid, ClassGlyphComponent> ClassGlyphComponents;
        public static Dictionary<Guid, BackgroundComponent> BackgroundComponents;
        public static Dictionary<Guid, DisplayTextComponent> DisplayTextComponents;
        public static Dictionary<Guid, EnergyComponent> EnergyComponents;
        public static Dictionary<Guid, GlyphComponent> GlyphComponents;
        public static Dictionary<Guid, HealthComponent> HealthComponents;
        public static Dictionary<Guid, MovementComponent> MovementComponents;
        public static Dictionary<Guid, List<RaceComponent>> RaceComponents;
        public static Dictionary<Guid, TransformComponent> TransformComponents;

        static ComponentRepo()
        {
            BackgroundComponents = new Dictionary<Guid, BackgroundComponent>();
            ClassComponents = new Dictionary<Guid, List<ClassComponent>>();
            ClassGlyphComponents = new Dictionary<Guid, ClassGlyphComponent>();
            DisplayTextComponents = new Dictionary<Guid, DisplayTextComponent>();
            EnergyComponents = new Dictionary<Guid, EnergyComponent>();
            GlyphComponents = new Dictionary<Guid, GlyphComponent>();
            HealthComponents = new Dictionary<Guid, HealthComponent>();
            MovementComponents = new Dictionary<Guid, MovementComponent>();
            RaceComponents = new Dictionary<Guid, List<RaceComponent>>();
            TransformComponents = new Dictionary<Guid, TransformComponent>();
        }

        public static void AddClass(Guid entityId, ClassComponent newClass)
        {
            if (!ClassComponents.ContainsKey(entityId))
            {
                ClassComponents.Add(entityId, new List<ClassComponent> { newClass });
            }
            else
            {
                var existingClasses = ClassComponents[entityId];
                if (!existingClasses.Any(existingClass => existingClass.ClassId == newClass.ClassId))
                {
                    ClassComponents[entityId] = ClassComponents[entityId]
                        .Append(newClass)
                        .ToList();
                }
            }
        }

        public static void RemoveClass(Guid entityId, Guid classId)
        {
            if (ClassComponents.ContainsKey(entityId))
            {
                ClassComponents[entityId] = ClassComponents[entityId]
                    .Where(existingClass => existingClass.ClassId != classId)
                    .ToList();
            }
        }

        public static void AddRace(Guid entityId, RaceComponent newRace)
        {
            if (!RaceComponents.ContainsKey(entityId))
            {
                RaceComponents.Add(entityId, new List<RaceComponent> { newRace });
            }
            else
            {
                var existingRaces = RaceComponents[entityId];
                if (!existingRaces.Any(existingRace => existingRace.RaceId == newRace.RaceId))
                {
                    RaceComponents[entityId] = RaceComponents[entityId]
                        .Append(newRace)
                        .ToList();
                }
            }
        }

        public static void RemoveRace(Guid entityId, Guid raceId)
        {
            if (RaceComponents.ContainsKey(entityId))
            {
                RaceComponents[entityId] = RaceComponents[entityId]
                    .Where(existingClass => existingClass.RaceId != raceId)
                    .ToList();
            }
        }
    }
}
