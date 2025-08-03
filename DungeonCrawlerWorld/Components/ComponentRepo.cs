using System;
using System.Collections.Generic;
using System.Linq;

namespace DungeonCrawlerWorld.Components
{
    public static class ComponentRepo
    {
        public static Dictionary<Guid, List<ClassComponent>> ClassComponents { get; }
        public static Dictionary<Guid, ClassGlyphComponent> ClassGlyphComponents { get; }
        public static Dictionary<Guid, BackgroundComponent> BackgroundComponents { get; set; }
        public static Dictionary<Guid, DisplayTextComponent> DisplayTextComponents { get; set; }
        public static Dictionary<Guid, EnergyComponent> EnergyComponents { get; set; }
        public static Dictionary<Guid, GlyphComponent> GlyphComponents { get; set; }
        public static Dictionary<Guid, HealthComponent> HealthComponents { get; set; }
        public static Dictionary<Guid, MovementComponent> MovementComponents { get; set; }
        public static Dictionary<Guid, List<RaceComponent>> RaceComponents { get; set; }
        public static Dictionary<Guid, TransformComponent> TransformComponents { get; set; }

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

        public static List<IEntityComponent> GetAllComponents(Guid entityId)
        {
            List<IEntityComponent> components = new();

            if (RaceComponents.TryGetValue(entityId, out List<RaceComponent> raceComponents))
            {
                foreach (var component in raceComponents)
                {
                    components.Add(component);
                }
            }
            if (ClassComponents.TryGetValue(entityId, out List<ClassComponent> classComponents))
            {
                foreach (var component in classComponents)
                {
                    components.Add(component);
                }
            }
            if (BackgroundComponents.TryGetValue(entityId, out BackgroundComponent backgroundComponent))
            {
                components.Add(backgroundComponent);
            }
            if (ClassGlyphComponents.TryGetValue(entityId, out ClassGlyphComponent classglyphComponent))
            {
                components.Add(classglyphComponent);
            }
            if (DisplayTextComponents.TryGetValue(entityId, out DisplayTextComponent displayTextComponent))
            {
                components.Add(displayTextComponent);
            }
            if (EnergyComponents.TryGetValue(entityId, out EnergyComponent energyComponent))
            {
                components.Add(energyComponent);
            }
            if (GlyphComponents.TryGetValue(entityId, out GlyphComponent glyphComponent))
            {
                components.Add(glyphComponent);
            }
            if (HealthComponents.TryGetValue(entityId, out HealthComponent healthComponent))
            {
                components.Add(healthComponent);
            }
            if (MovementComponents.TryGetValue(entityId, out MovementComponent movementComponent))
            {
                components.Add(movementComponent);
            }
            if (TransformComponents.TryGetValue(entityId, out TransformComponent transformComponent))
            {
                components.Add(transformComponent);
            }

            return components;
        }

        public static void RemoveAllComponents(Guid entityId)
        {
            RaceComponents.Remove(entityId);
            ClassComponents.Remove(entityId);
            
            BackgroundComponents.Remove(entityId);
            ClassGlyphComponents.Remove(entityId);
            DisplayTextComponents.Remove(entityId);
            EnergyComponents.Remove(entityId);
            GlyphComponents.Remove(entityId);
            HealthComponents.Remove(entityId);
            MovementComponents.Remove(entityId);
            TransformComponents.Remove(entityId);
        }
    }
}
