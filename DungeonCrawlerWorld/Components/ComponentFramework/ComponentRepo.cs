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
    /// Dense components are stored as arrays, indexed by the Integer entityId, and checked before usage via presence arrays.
    /// Sparse components are those that appear infrequently on entities. 
    /// Sparse components are stored as arrays and mapped to a separate dense array via entityId to avoid allocating unused memory on entities without those components.
    /// </summary>
    /// <todo> 
    /// Recycle entityIds and deal with maxInt limit.
    /// Retrieve from data storage.
    /// </todo>

    public delegate void MergeAction<T>(ref T existingComponent, T newComponent);

    public static class ComponentRepo
    {
        /// <summary>
        /// A custom unique identifier for entities that allows the entityId to be used as an array index and dictionary key
        /// </summary>
        private static int currentMaxEntityId = 0;
        public static int CurrentMaxEntityId { get => currentMaxEntityId; }

        /// <summary>
        /// Specifies the starting size of Dense component arrays.
        /// This allows them to be set to a specified size once and then fill in that memory instead of re-allocating the array with each new component added.
        /// This value should be adjusted as more components are added to the game and the array sizes are tested.
        /// </summary>
        private static readonly int defaultDenseArraySize = 2000000;

        private static readonly int defaultSparseArraySize = 200000;

        /// <summary>
        /// Specifies the amount to increase the dense array sizes by whenever they have been filled.
        /// By increasing as a percentage we can avoid frequent expensive reallocations and grow better with the game size
        /// </summary>
        private static readonly int denseArrayIncrementAmount = (int)(defaultDenseArraySize * 0.1f);

        /// <summary>
        /// The current size of the dense component arrays. It will be incremented by the denseArrayIncrementAmount whenever the currentMaxEntityId matches the currentDenseArraySize.
        /// </summary>
        private static int currentDenseArraySize = defaultDenseArraySize;

        private static readonly DenseSet<BackgroundComponent> _backgroundComponents;
        public static DenseSet<BackgroundComponent> BackgroundComponents => _backgroundComponents;

        private static readonly DenseSet<DisplayTextComponent> _displayTextComponents;
        public static DenseSet<DisplayTextComponent> DisplayTextComponents => _displayTextComponents;

        private static readonly DenseSet<GlyphComponent> _glyphComponents;
        public static DenseSet<GlyphComponent> GlyphComponents => _glyphComponents;

        private static readonly DenseSet<TransformComponent> _transformComponents;
        public static DenseSet<TransformComponent> TransformComponents => _transformComponents;

        private static SparseSet<EnergyComponent> _energyComponents { get; set; }
        public static SparseSet<EnergyComponent> EnergyComponents { get => _energyComponents; }

        private static SparseSet<HealthComponent> _healthComponents { get; set; }
        public static SparseSet<HealthComponent> HealthComponents { get => _healthComponents; }

        private static SparseSet<MovementComponent> _movementComponents { get; set; }
        public static SparseSet<MovementComponent> MovementComponents { get => _movementComponents; }

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

        /// <summary>
        /// Initialize the DenseSets and SparseSets with their default sizes and merge methods.
        /// </summary>
        static ComponentRepo()
        {
            _backgroundComponents = new DenseSet<BackgroundComponent>(defaultDenseArraySize,
                (ref BackgroundComponent existingComponent, BackgroundComponent newComponent) =>
                {
                    existingComponent.BackgroundColor = Color.Lerp(existingComponent.BackgroundColor, newComponent.BackgroundColor, 0.5f);
                });

            _displayTextComponents = new DenseSet<DisplayTextComponent>(defaultDenseArraySize,
                (ref DisplayTextComponent existingComponent, DisplayTextComponent newComponent) =>
                {
                    existingComponent.Name = existingComponent.Name + " " + newComponent.Name;
                    existingComponent.Description = existingComponent.Description + Environment.NewLine + newComponent.Description;
                });

            _glyphComponents = new DenseSet<GlyphComponent>(defaultDenseArraySize,
                (ref GlyphComponent existingComponent, GlyphComponent newComponent) =>
                {
                    existingComponent.GlyphColor = Color.Lerp(existingComponent.GlyphColor, newComponent.GlyphColor, 0.5f);
                });

            _transformComponents = new DenseSet<TransformComponent>(defaultDenseArraySize,
                (ref TransformComponent existingComponent, TransformComponent newComponent) =>
                {
                    existingComponent.Size = new Vector3Byte(
                        (byte)((existingComponent.Size.X + newComponent.Size.X) / 2),
                        (byte)((existingComponent.Size.Y + newComponent.Size.Y) / 2),
                        (byte)((existingComponent.Size.Z + newComponent.Size.Z) / 2));
                });

            _energyComponents = new SparseSet<EnergyComponent>(defaultDenseArraySize, defaultSparseArraySize,
                (ref EnergyComponent existingComponent, EnergyComponent newComponent) =>
                {
                    existingComponent.EnergyRecharge = (short)((existingComponent.EnergyRecharge + newComponent.EnergyRecharge) / 2);
                    existingComponent.MaximumEnergy = (short)((existingComponent.MaximumEnergy + newComponent.MaximumEnergy) / 2);
                    existingComponent.CurrentEnergy = MathUtility.ClampShort(
                        (short)((existingComponent.CurrentEnergy + newComponent.CurrentEnergy) / 2),
                        0,
                        existingComponent.MaximumEnergy);
                });

            _healthComponents = new SparseSet<HealthComponent>(defaultDenseArraySize, defaultSparseArraySize,
                (ref HealthComponent existingComponent, HealthComponent newComponent) =>
                {
                    existingComponent.HealthRegen = (short)((existingComponent.HealthRegen + newComponent.HealthRegen) / 2);
                    existingComponent.MaximumHealth = (short)((existingComponent.MaximumHealth + newComponent.MaximumHealth) / 2);
                    existingComponent.CurrentHealth = MathUtility.ClampShort(
                        (short)((existingComponent.CurrentHealth + newComponent.CurrentHealth) / 2),
                        0,
                        existingComponent.MaximumHealth);
                });

            _movementComponents = new SparseSet<MovementComponent>(defaultDenseArraySize, defaultSparseArraySize,
                (ref MovementComponent existingComponent, MovementComponent newComponent) =>
                {
                    existingComponent.MovementMode = (MovementMode)Math.Max((short)existingComponent.MovementMode, (short)newComponent.MovementMode);
                    existingComponent.EnergyToMove = (short)((existingComponent.EnergyToMove + newComponent.EnergyToMove) / 2);
                    existingComponent.FramesToWait = (short)((existingComponent.FramesToWait + newComponent.FramesToWait) / 2);
                    existingComponent.NextMapPosition = newComponent.NextMapPosition;
                    existingComponent.TargetMapPosition = newComponent.TargetMapPosition;
                });

            /* Multi-assignment sparse components */
            //TODO
            _classComponents = [];
            _raceComponents = [];
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
                ResizeComponentArrays();
            }
            return currentMaxEntityId;
        }

        /// <summary>
        /// Increase the size of all dense component arrays to account for new entityIds.
        /// </summary>
        private static void ResizeComponentArrays()
        {
            currentDenseArraySize += denseArrayIncrementAmount;

            _backgroundComponents.Resize(currentDenseArraySize);
            _displayTextComponents.Resize(currentDenseArraySize);
            _glyphComponents.Resize(currentDenseArraySize);
            _transformComponents.Resize(currentDenseArraySize);

            _healthComponents.Resize(currentDenseArraySize);
            _energyComponents.Resize(currentDenseArraySize);
            _movementComponents.Resize(currentDenseArraySize);
        }

        //Return a list of all components attached to an entity. This is primarily used in debugging mode.
        public static List<IEntityComponent> GetAllComponents(int entityId)
        {
            var components = new List<IEntityComponent>(9);

            // Dense components
            if (_backgroundComponents.HasComponent(entityId))
            {
                components.Add(_backgroundComponents.Get(entityId));
            }

            if (_displayTextComponents.HasComponent(entityId))
            {
                components.Add(_displayTextComponents.Get(entityId));
            }

            if (_glyphComponents.HasComponent(entityId))
            {
                components.Add(_glyphComponents.Get(entityId));
            }

            if (_transformComponents.HasComponent(entityId))
            {
                components.Add(_transformComponents.Get(entityId));
            }

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

            if (_energyComponents.HasComponent(entityId))
            {
                components.Add(_energyComponents.Get(entityId));
            }

            if (_healthComponents.HasComponent(entityId))
            {
                components.Add(_healthComponents.Get(entityId));
            }

            if (_movementComponents.HasComponent(entityId))
            {
                components.Add(_movementComponents.Get(entityId));
            }

            return components;
        }

        /// <summary>
        /// Remove all components for an entity.
        /// This is primarily used when an entity is deleted.
        /// </summary>
        public static void RemoveAllComponents(int entityId)
        {
            _backgroundComponents.Remove(entityId);
            _displayTextComponents.Remove(entityId);
            _glyphComponents.Remove(entityId);
            _transformComponents.Remove(entityId);

            _classComponents.Remove(entityId);
            _raceComponents.Remove(entityId);
            _energyComponents.Remove(entityId);
            _healthComponents.Remove(entityId);
            _movementComponents.Remove(entityId);
        }
    }
}
