using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// The in-memory storage of all components.
    /// Dense components are those that appear on the majority of entities.
    /// Dense components are stored as arrays, indexed by the Integer entityId, to take advantage of array efficiency.
    /// Sparse components are those that appear infrequently on entities. 
    /// They are stored as dictionaries, keyed by the Integer entityId, to avoid allocating unused memory on entities without those components.
    /// </summary>
    /// <todo> 
    /// Recycle entityIds and deal with maxInt limit.
    /// Retrieve from data storage.
    /// </todo>
    public static class ComponentRepo
    {
        /// <summary>
        /// A custom unique identifier for entities that allows the entityId to be used as an array index and dictionary key
        /// </summary>
        private static int currentMaxEntityId = 0;
        public static int CurrentMaxEntityId { get => currentMaxEntityId; }

        /// <summary>
        /// Dense components
        /// These components are utilized on the majority of entities
        /// They are stored in arrays for increased efficiency when used by Systems at the cost of wasted storage space for entities without those components.
        /// </summary>
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

        /// <summary>
        /// Sparse components
        /// These components are utilized infrequently on entities
        /// They are stored in dictionaries for more efficient data storage at the cost of performance when used.
        /// Class and Race components are in get-only dictionaries to require the use of their Add and Remove methods with 
        /// additional logic to deal with an entity using multiple classes and races.
        /// </summary>
        public static Dictionary<int, List<ClassComponent>> ClassComponents { get; }
        public static Dictionary<int, List<RaceComponent>> RaceComponents { get; set; }
        public static ConcurrentDictionary<int, EnergyComponent> EnergyComponents { get; set; }
        public static ConcurrentDictionary<int, HealthComponent> HealthComponents { get; set; }
        public static Dictionary<int, MovementComponent> MovementComponents { get; set; }

        /// <summary>
        /// Specifies the starting size of Dense component arrays.
        /// This allows them to be set to a specified size once and then fill in that memory instead of re-allocating the array with each new component added.
        /// This value should be adjusted as more components are added to the game and the array sizes are tested.
        /// </summary>
        private static readonly int defaultDenseArraySize = 1000000;

        /// <summary>
        /// Specifies the amount to increase the dense array sizes by whenever they have been filled.
        /// By increasing as a percentage we can avoid frequent expensive reallocations and grow better with the game size
        /// </summary>
        private static readonly int denseArrayIncrementAmount = (int)(defaultDenseArraySize * 0.1f);

        /// <summary>
        /// The current size of the dense component arrays.
        /// </summary>
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

        /// <summary>
        /// Increment the currentMaxEntityId and return that value
        /// This is used in the creation of new entities as both the entityId and to index/key components for that entity
        /// If the new id would exceed the current dense component array size, increase them all.
        /// </summary>
        public static int GetNextEntityId()
        {
            currentMaxEntityId++;

            if (currentMaxEntityId >= currentDenseArraySize)
            {
                IncrementAllDenseComponentArrays();
            }
            return currentMaxEntityId;
        }

        /// <summary>
        /// Increase the size of all dense component arrays to account for new entityIds.
        /// </summary>
        private static void IncrementAllDenseComponentArrays()
        {
            currentDenseArraySize += denseArrayIncrementAmount;
            Array.Resize(ref _backgroundComponents, currentDenseArraySize);
            Array.Resize(ref _displayTextComponents, currentDenseArraySize);
            Array.Resize(ref _glyphComponents, currentDenseArraySize);
            Array.Resize(ref _transformComponents, currentDenseArraySize);
        }

        /// <summary>
        /// Add a class component to an entity
        /// An entity can contain multiple classes.
        /// </summary>
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

        /// <summary>
        /// Remove a class component from an entity.
        /// </summary>
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

        /// <summary>
        /// Add a race component to an entity
        /// An entity can contain multiple races.
        /// </summary>
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

        /// <summary>
        /// Remove a race component from an entity
        /// </summary>
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

        //Return a list of all components attached to an entity. This is primarily used in debugging mode.
        public static List<IEntityComponent> GetAllComponents(int entityId)
        {
            var components = new List<IEntityComponent>(9);

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
            {
                components.AddRange(raceComponentList);
            }

            if (ClassComponents.TryGetValue(entityId, out var classComponentList))
            {
                components.AddRange(classComponentList);
            }

            if (EnergyComponents.TryGetValue(entityId, out var energyComponent))
            {
                components.Add(energyComponent);
            }

            if (HealthComponents.TryGetValue(entityId, out var healthComponent))
            {
                components.Add(healthComponent);
            }

            if (MovementComponents.TryGetValue(entityId, out var movementComponent))
            {
                components.Add(movementComponent);
            }

            return components;
        }

        /// <summary>
        /// Remove all components for an entity.
        /// This is primarily used when an entity is deleted.
        /// </summary>
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
