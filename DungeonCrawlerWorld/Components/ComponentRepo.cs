using DungeonCrawlerWorld.Utilities;
using Microsoft.Xna.Framework;
using System;
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

    //TODO when implementing the BitSet, for elements that can have shared values (glyph, background, race) have the bitset point to the same value.
    ///     This will save memory and speed up comparisons at the cost of creates/deletes taking longer, which are rare anyway.
    ///     Do this on a case-by-case basis.
    ///     Then evaluate if there can be a common structure to share.
    ///     BENCHMARK BEFORE CHANGES - fps and heap
    ///         Before
    ///             Live Objects : 632
    ///             Private Bytes : 2.6 GB
    ///             FPS : 34-39

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
        /// A save method is created for each component to define Merges, which will handle permanent changes to an entity's components.
        /// </summary>
        private static BackgroundComponent?[] _backgroundComponents;
        public static IReadOnlyList<BackgroundComponent?> BackgroundComponents { get => _backgroundComponents; }
        public static void SaveBackgroundComponent(int entityId, BackgroundComponent backgroundComponent, ComponentSaveMode componentSaveMode)
        {
            if (componentSaveMode == ComponentSaveMode.Merge)
            {
                var existingComponent = _backgroundComponents[entityId];
                if (existingComponent != null)
                {
                    backgroundComponent.BackgroundColor = Color.Lerp(backgroundComponent.BackgroundColor, existingComponent.Value.BackgroundColor, 0.5f);
                }
            }
            _backgroundComponents[entityId] = backgroundComponent;
        }
        public static void RemoveBackgroundComponent(int entityId)
        {
            _backgroundComponents[entityId] = null;
        }

        private static DisplayTextComponent?[] _displayTextComponents;
        public static IReadOnlyList<DisplayTextComponent?> DisplayTextComponents
        {
            get => _displayTextComponents;
        }
        public static void SaveDisplayTextComponent(int entityId, DisplayTextComponent displayTextComponent, ComponentSaveMode componentSaveMode)
        {
            if (componentSaveMode == ComponentSaveMode.Merge)
            {
                var existingComponent = _displayTextComponents[entityId];
                if (existingComponent != null)
                {
                    displayTextComponent.Name = existingComponent.Value.Name + " " + displayTextComponent.Name;
                    displayTextComponent.Description = existingComponent.Value.Description + Environment.NewLine + displayTextComponent.Description;
                }
            }
            _displayTextComponents[entityId] = displayTextComponent;
        }
        public static void RemoveDisplayTextComponent(int entityId)
        {
            _displayTextComponents[entityId] = null;
        }

        private static GlyphComponent?[] _glyphComponents;
        public static IReadOnlyList<GlyphComponent?> GlyphComponents
        {
            get => _glyphComponents;
        }
        public static void SaveGlyphComponent(int entityId, GlyphComponent glyphComponent, ComponentSaveMode componentSaveMode)
        {
            if (componentSaveMode == ComponentSaveMode.Merge)
            {
                var existingComponent = _glyphComponents[entityId];
                if (existingComponent != null)
                {
                    //Keep the glyph and offset the same as this will be hard to calculate currently.
                    glyphComponent.GlyphColor = Color.Lerp(glyphComponent.GlyphColor, existingComponent.Value.GlyphColor, 0.5f);
                    glyphComponent.Glyph = existingComponent.Value.Glyph;
                    glyphComponent.GlyphOffset = existingComponent.Value.GlyphOffset;
                }
            }
            _glyphComponents[entityId] = glyphComponent;
        }
        public static void RemoveGlyphComponent(int entityId)
        {
            _glyphComponents[entityId] = null;
        }

        private static TransformComponent?[] _transformComponents;
        public static IReadOnlyList<TransformComponent?> TransformComponents
        {
            get => _transformComponents;
        }
        public static void SaveTransformComponent(int entityId, TransformComponent transformComponent, ComponentSaveMode componentSaveMode)
        {
            if (componentSaveMode == ComponentSaveMode.Merge)
            {
                var existingComponent = _transformComponents[entityId];
                if (existingComponent != null)
                {
                    //Do not change the position.
                    transformComponent.Position = existingComponent.Value.Position;
                    //Use the average size, rounding down. This will avoid collision issues that growing would cause.
                    transformComponent.Size = new Vector3Byte(
                        (byte)((transformComponent.Size.X + existingComponent.Value.Size.X) / 2),
                        (byte)((transformComponent.Size.Y + existingComponent.Value.Size.Y) / 2),
                        (byte)((transformComponent.Size.Z + existingComponent.Value.Size.Z) / 2));
                }
            }
            _transformComponents[entityId] = transformComponent;
        }
        public static void RemoveTransformComponent(int entityId)
        {
            _transformComponents[entityId] = null;
        }

        /// <summary>
        /// Sparse components
        /// These components are utilized infrequently on entities
        /// They are stored in dictionaries for more efficient data storage at the cost of performance when used.
        /// Class and Race components are in get-only dictionaries to require the use of their Add and Remove methods with 
        /// additional logic to deal with an entity using multiple classes and races.
        /// </summary>
        private static Dictionary<int, List<ClassComponent>> _classComponents { get; }
        public static IReadOnlyDictionary<int, List<ClassComponent>> ClassComponents { get => _classComponents; }
        public static void AddClassComponent(int entityId, ClassComponent newClass)
        {
            if (!ClassComponents.TryGetValue(entityId, out var classComponents))
            {
                classComponents = [];
            }

            if (!classComponents.Any(classComponent => classComponent.Id == newClass.Id))
            {
                classComponents.Add(newClass);
                _classComponents[entityId] = classComponents;
            }
        }
        public static void RemoveClassComponent(int entityId, Guid classId)
        {
            if (ClassComponents.TryGetValue(entityId, out var classComponents))
            {
                for (int i = 0; i < classComponents.Count; i++)
                {
                    if (classComponents[i].Id == classId)
                    {
                        classComponents.RemoveAt(i);
                        _classComponents[entityId] = classComponents;
                        break;
                    }
                }

                if (classComponents.Count == 0)
                {
                    _classComponents.Remove(entityId);
                }
            }
        }

        private static Dictionary<int, List<RaceComponent>> _raceComponents { get; }
        public static IReadOnlyDictionary<int, List<RaceComponent>> RaceComponents { get => _raceComponents; }
        public static void AddRaceComponent(int entityId, RaceComponent newRace)
        {
            if (!RaceComponents.TryGetValue(entityId, out var raceComponents))
            {
                raceComponents = [];
            }

            if (!raceComponents.Any(raceComponent => raceComponent.Id == newRace.Id))
            {
                raceComponents.Add(newRace);
                _raceComponents[entityId] = raceComponents;
            }
        }
        public static void RemoveRaceComponent(int entityId, Guid raceId)
        {
            if (RaceComponents.TryGetValue(entityId, out var raceComponets))
            {
                for (int i = 0; i < raceComponets.Count; i++)
                {
                    if (raceComponets[i].Id == raceId)
                    {
                        raceComponets.RemoveAt(i);
                        _raceComponents[entityId] = raceComponets;
                        break;
                    }
                }

                if (raceComponets.Count == 0)
                {
                    _raceComponents.Remove(entityId);
                }
            }
        }

        private static Dictionary<int, EnergyComponent> _energyComponents { get; set; }
        public static IReadOnlyDictionary<int, EnergyComponent> EnergyComponents { get => _energyComponents; }
        public static void SaveEnergyComponent(int entityId, EnergyComponent energyComponent, ComponentSaveMode componentSaveMode)
        {
            if (componentSaveMode == ComponentSaveMode.Merge)
            {
                if (_energyComponents.TryGetValue(entityId, out var existingComponent))
                {
                    energyComponent.EnergyRecharge = (short)((energyComponent.EnergyRecharge + existingComponent.EnergyRecharge) / 2);
                    energyComponent.MaximumEnergy = (short)((energyComponent.MaximumEnergy + existingComponent.MaximumEnergy) / 2);
                    energyComponent.CurrentEnergy = MathUtility.ClampShort(
                        (short)((energyComponent.CurrentEnergy + existingComponent.CurrentEnergy) / 2),
                        0,
                        energyComponent.MaximumEnergy);
                }
            }
            _energyComponents[entityId] = energyComponent;
        }
        public static void RemoveEnergyComponent(int entityId)
        {
            _energyComponents.Remove(entityId);
        }

        private static Dictionary<int, HealthComponent> _healthComponents { get; set; }
        public static IReadOnlyDictionary<int, HealthComponent> HealthComponents { get => _healthComponents; }
        public static void SaveHealthComponent(int entityId, HealthComponent healthComponent, ComponentSaveMode componentSaveMode)
        {
            if (componentSaveMode == ComponentSaveMode.Merge)
            {
                if (_healthComponents.TryGetValue(entityId, out var existingComponent))
                {
                    healthComponent.HealthRegen = (short)((healthComponent.HealthRegen + existingComponent.HealthRegen) / 2);
                    healthComponent.MaximumHealth = (short)((healthComponent.MaximumHealth + existingComponent.MaximumHealth) / 2);
                    healthComponent.CurrentHealth = MathUtility.ClampShort(
                        (short)((healthComponent.CurrentHealth + existingComponent.CurrentHealth) / 2),
                        0,
                        healthComponent.MaximumHealth);
                }
            }
            _healthComponents[entityId] = healthComponent;
        }
        public static void RemoveHealthComponent(int entityId)
        {
            _healthComponents.Remove(entityId);
        }

        private static Dictionary<int, MovementComponent> _movementComponents { get; set; }
        public static IReadOnlyDictionary<int, MovementComponent> MovementComponents { get => _movementComponents; }
        public static void SaveMovementComponent(int entityId, MovementComponent movementComponent, ComponentSaveMode componentSaveMode)
        {
            if (componentSaveMode == ComponentSaveMode.Merge)
            {
                if (_movementComponents.TryGetValue(entityId, out var existingComponent))
                {
                    movementComponent.MovementMode = (MovementMode)Math.Max((short)movementComponent.MovementMode, (short)existingComponent.MovementMode);
                    movementComponent.EnergyToMove = (short)((movementComponent.EnergyToMove + existingComponent.EnergyToMove) / 2);
                    movementComponent.FramesToWait = (short)((movementComponent.FramesToWait + existingComponent.FramesToWait) / 2);
                    movementComponent.NextMapPosition = existingComponent.NextMapPosition;
                    movementComponent.TargetMapPosition = existingComponent.TargetMapPosition;
                }
            }
            _movementComponents[entityId] = movementComponent;
        }
        public static void RemoveMovementComponent(int entityId)
        {
            _movementComponents.Remove(entityId);
        }

        /// <summary>
        /// Specifies the starting size of Dense component arrays.
        /// This allows them to be set to a specified size once and then fill in that memory instead of re-allocating the array with each new component added.
        /// This value should be adjusted as more components are added to the game and the array sizes are tested.
        /// </summary>
        private static readonly int defaultDenseArraySize = 2000000;

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
            _classComponents = [];
            _raceComponents = [];
            _energyComponents = [];
            _healthComponents = [];
            _movementComponents = [];
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
                for (var i = 0; i < raceComponentList.Count; i++)
                {
                    components.Add(raceComponentList[i]);
                }
            }

            if (ClassComponents.TryGetValue(entityId, out var classComponentList))
            {
                for (var i = 0; i < classComponentList.Count; i++)
                {
                    components.Add(classComponentList[i]);
                }
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
            _backgroundComponents[entityId] = null;
            _displayTextComponents[entityId] = null;
            _glyphComponents[entityId] = null;
            _transformComponents[entityId] = null;

            _raceComponents.Remove(entityId);
            _classComponents.Remove(entityId);
            _energyComponents.Remove(entityId);
            _healthComponents.Remove(entityId);
            _movementComponents.Remove(entityId);
        }
    }
}
