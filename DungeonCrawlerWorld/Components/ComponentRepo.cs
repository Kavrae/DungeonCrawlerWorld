using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DungeonCrawlerWorld.Components
{
    //TODO tags. New collections of sparse components that are set based on other 
    // components all being set, which requires setters for those components
    public static class ComponentRepo
    {
        /* Dense Components */
        private static BackgroundComponent?[] _backgroundComponents;
        public static BackgroundComponent?[] BackgroundComponents
        {
            get => _backgroundComponents;
            set => _backgroundComponents = value;
        }

        private static DisplayTextComponent?[] _displayTextComponents;
        public static DisplayTextComponent?[] DisplayTextComponents
        {
            get => _displayTextComponents;
            set => _displayTextComponents = value;
        }

        private static GlyphComponent?[] _glyphComponents;
        public static GlyphComponent?[] GlyphComponents
        {
            get => _glyphComponents;
            set => _glyphComponents = value;
        }

        private static TransformComponent?[] _transformComponents;
        public static TransformComponent?[] TransformComponents
        {
            get => _transformComponents;
            set => _transformComponents = value;
        }

        /* Sparse Components */
        public static Dictionary<int, List<ClassComponent>> ClassComponents { get; }
        public static Dictionary<int, List<RaceComponent>> RaceComponents { get; set; }
        public static ConcurrentDictionary<int, EnergyComponent> EnergyComponents { get; set; }
        public static ConcurrentDictionary<int, HealthComponent> HealthComponents { get; set; }
        public static Dictionary<int, MovementComponent> MovementComponents { get; set; }

        private static int currentMaxEntityId = 0;
        public static int CurrentMaxEntityId { get => currentMaxEntityId; }

        //TODO derive this from config file world size.
        private static readonly int defaultDenseArraySize = 1000000;
        private static readonly int denseArrayIncrementAmount = (int)(defaultDenseArraySize * 0.1f);
        private static int currentDenseArraySize = defaultDenseArraySize;

        static ComponentRepo()
        {
            /* Dense Components */
            _backgroundComponents = new BackgroundComponent?[defaultDenseArraySize];
            _displayTextComponents = new DisplayTextComponent?[defaultDenseArraySize];
            _glyphComponents = new GlyphComponent?[defaultDenseArraySize];
            _transformComponents = new TransformComponent?[defaultDenseArraySize];

            /* Sparse Components */
            ClassComponents = new Dictionary<int, List<ClassComponent>>();
            RaceComponents = new Dictionary<int, List<RaceComponent>>();
            EnergyComponents = new ConcurrentDictionary<int, EnergyComponent>();
            HealthComponents = new ConcurrentDictionary<int, HealthComponent>();
            MovementComponents = new Dictionary<int, MovementComponent>();
        }

        public static int GetNextEntityId()
        {
            currentMaxEntityId++;

            if (currentMaxEntityId >= currentDenseArraySize)
            {
                IncrementAllDenseComponentArrays();
            }
            return currentMaxEntityId;
        }

        private static void IncrementAllDenseComponentArrays()
        {
            currentDenseArraySize += denseArrayIncrementAmount;
            Array.Resize(ref _backgroundComponents, currentDenseArraySize);
            Array.Resize(ref _displayTextComponents, currentDenseArraySize);
            Array.Resize(ref _glyphComponents, currentDenseArraySize);
            Array.Resize(ref _transformComponents, currentDenseArraySize);
        }

        public static void AddClass(int entityId, ClassComponent newClass)
        {
            if (!ClassComponents.TryGetValue(entityId, out var classComponents))
            {
                classComponents = new List<ClassComponent>();
                ClassComponents[entityId] = classComponents;
            }

            if (!classComponents.Any(classComponent => classComponent.ClassId == newClass.ClassId))
            {
                classComponents.Add(newClass);
            }
        }

        public static void RemoveClass(int entityId, Guid classId)
        {
            if (ClassComponents.TryGetValue(entityId, out var classComponents))
            {
                for (int i = 0; i < classComponents.Count; i++)
                {
                    if (classComponents[i].ClassId == classId)
                    {
                        classComponents.RemoveAt(i);
                        break;
                    }
                }

                if (classComponents.Count == 0)
                {
                    ClassComponents.Remove(entityId);
                }
            }
        }

        public static void AddRace(int entityId, RaceComponent newRace)
        {
            if (!RaceComponents.TryGetValue(entityId, out var raceComponents))
            {
                raceComponents = new List<RaceComponent>();
                RaceComponents[entityId] = raceComponents;
            }

            if (!raceComponents.Any(raceComponent => raceComponent.RaceId == newRace.RaceId))
            {
                raceComponents.Add(newRace);
            }
        }

        public static void RemoveRace(int entityId, Guid raceId)
        {
            if (RaceComponents.TryGetValue(entityId, out var raceComponets))
            {
                for (int i = 0; i < raceComponets.Count; i++)
                {
                    if (raceComponets[i].RaceId == raceId)
                    {
                        raceComponets.RemoveAt(i);
                        break;
                    }
                }

                if (raceComponets.Count == 0)
                {
                    RaceComponents.Remove(entityId);
                }
            }
        }

        public static List<IEntityComponent> GetAllComponents(int entityId)
        {
            var components = new List<IEntityComponent>(8);

            // Dense components
            var backgroundComponent = BackgroundComponents[entityId];
            if (backgroundComponent != null) components.Add(backgroundComponent);

            var displayTextComponent = DisplayTextComponents[entityId];
            if (displayTextComponent != null) components.Add(displayTextComponent);

            var glyphComponent = GlyphComponents[entityId];
            if (glyphComponent != null) components.Add(glyphComponent);

            var transformComponent = TransformComponents[entityId];
            if (transformComponent != null) components.Add(transformComponent);

            // Sparse components
            if (RaceComponents.TryGetValue(entityId, out var raceComponentList))
                components.AddRange(raceComponentList);

            if (ClassComponents.TryGetValue(entityId, out var classComponentList))
                components.AddRange(classComponentList);

            if (EnergyComponents.TryGetValue(entityId, out var energyComponent))
                components.Add(energyComponent);

            if (HealthComponents.TryGetValue(entityId, out var healthComponent))
                components.Add(healthComponent);

            if (MovementComponents.TryGetValue(entityId, out var movementComponent))
                components.Add(movementComponent);

            return components;
        }

        public static void RemoveAllComponents(int entityId)
        {
            BackgroundComponents[entityId] = null;
            DisplayTextComponents[entityId] = null;
            GlyphComponents[entityId] = null;
            TransformComponents[entityId] = null;
            
            RaceComponents.Remove(entityId);
            ClassComponents.Remove(entityId);
            EnergyComponents.TryRemove(entityId, out _);
            HealthComponents.TryRemove(entityId, out _);
            MovementComponents.Remove(entityId);
        }
    }
}
