using System;
using System.Collections.Generic;

namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// Sparse multi-set: allows many components per entity while keeping compact dense storage.
    /// Iterate all components via DenseView for best performance in systems.
    /// </summary>
    public sealed class SparseMultiSet<T> where T : struct
    {
        private int _maxEntities;
        private T[] _denseComponents;

        private int[] _entityIdToFirstDenseIndexMap;
        private int[] _denseIndexToEntityIdMap;
        private int[] _denseIndexLinks;
        private int[] _denseIndexLinksReverse;

        private int _count;

        public int Count => _count;

        public SparseMultiSet(int maximumEntityCount, int initialCapacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

            _maxEntities = maximumEntityCount;
            _entityIdToFirstDenseIndexMap = new int[_maxEntities];
            for (int entityId = 0; entityId < _entityIdToFirstDenseIndexMap.Length; entityId++)
            {
                _entityIdToFirstDenseIndexMap[entityId] = -1;
            }

            _denseComponents = new T[initialCapacity];
            _denseIndexToEntityIdMap = new int[initialCapacity];
            _denseIndexLinks = new int[initialCapacity];
            _denseIndexLinksReverse = new int[initialCapacity];
            _count = 0;
        }

        public void Resize(int newMaximumEntityCount)
        {
            Array.Resize(ref _entityIdToFirstDenseIndexMap, newMaximumEntityCount);
            for (int entityId = _maxEntities; entityId < newMaximumEntityCount; entityId++)
            {
                _entityIdToFirstDenseIndexMap[entityId] = -1;
            }
            _maxEntities = newMaximumEntityCount;
        }

        /// <summary>
        /// Add a new component to an entity. 
        /// If it's that entity's first component, set it as the FirstDenseIndexMap.
        /// Otherwise, update the linked lists to connect it to the previous first component.
        /// </summary>
        /// <param name="entityId"></param>
        /// <param name="component"></param>

        public void Add(int entityId, T component)
        {
            if (_count == _denseComponents.Length)
            {
                var newSize = _denseComponents.Length * 2;
                Array.Resize(ref _denseComponents, newSize);
                Array.Resize(ref _denseIndexToEntityIdMap, newSize);
                Array.Resize(ref _denseIndexLinks, newSize);
                Array.Resize(ref _denseIndexLinksReverse, newSize);
            }

            //We know the dense array is already condensed, so add the component to the end
            int newDenseIndex = _count++;
            _denseComponents[newDenseIndex] = component;
            _denseIndexToEntityIdMap[newDenseIndex] = entityId;

            //Add the component to the front of the list instead of iterating to the last link to add it to the end.
            var previousFirstDenseIndex = _entityIdToFirstDenseIndexMap[entityId];
            _denseIndexLinks[newDenseIndex] = previousFirstDenseIndex;
            _denseIndexLinksReverse[newDenseIndex] = -1;
            _entityIdToFirstDenseIndexMap[entityId] = newDenseIndex;
            if (previousFirstDenseIndex != -1)
            {
                _denseIndexLinksReverse[previousFirstDenseIndex] = newDenseIndex;
            }
        }

        /// <summary>
        /// Remove all components for an entity by following the _denseIndexLinks chain.
        /// </summary>
        /// <param name="entityId"></param>
        /// <returns></returns>
        public bool RemoveAll(int entityId)
        {
            var denseIndexesToRemove = new HashSet<int>();
            for (int denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1; denseIndex = _denseIndexLinks[denseIndex])
            {
                denseIndexesToRemove.Add(denseIndex);
            }

            //Clean up the links
            foreach (var denseIndexToRemove in denseIndexesToRemove)
            {
                _denseIndexLinks[denseIndexToRemove] = -1;
                _denseIndexLinksReverse[denseIndexToRemove] = -1;
                _denseIndexToEntityIdMap[denseIndexToRemove] = -1;
            }
            _entityIdToFirstDenseIndexMap[entityId] = -1;

            //Replace the removed components with components at the end of the array
            foreach (var denseIndexToRemove in denseIndexesToRemove)
            {
                ConsolidateDenseArray(denseIndexToRemove);
                _count--;
            }

            return denseIndexesToRemove.Count > 0;
        }

        /// <summary>
        /// Remove the first component matching the predicate for the given entityId.
        /// </summary>
        public bool RemoveFirst(int entityId, Func<T, bool> predicate)
        {
            int previousDenseIndex = -1;
            //Loop through the dense index chain until we find a match or reach the end. Assuming no circular references.
            for (int denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1; denseIndex = _denseIndexLinks[denseIndex])
            {
                ref var component = ref _denseComponents[denseIndex];
                if (predicate(component))
                {
                    // If it's the first component in the entity's linked list, remap the entity's first dense index to the current component's linked next component
                    //Otherwise, map the previous component's next link to the current component's next link
                    var nextDenseIndex = _denseIndexLinks[denseIndex];
                    if (previousDenseIndex == -1)
                    {
                        _entityIdToFirstDenseIndexMap[entityId] = nextDenseIndex;
                        _denseIndexLinksReverse[nextDenseIndex] = -1;
                    }
                    else
                    {
                        _denseIndexLinks[previousDenseIndex] = nextDenseIndex;
                        _denseIndexLinksReverse[nextDenseIndex] = previousDenseIndex;
                    }

                    ConsolidateDenseArray(denseIndex);

                    _count--;
                    return true;
                }
                previousDenseIndex = denseIndex;
            }
            return false;
        }

        /// <summary>
        /// If the removed component is not at the end of the dense component array, consolidate the dense array.
        /// This is done to avoid skipped indices by moving the last component into the removed component's index.
        /// The component linked lists and entity id mapping must be remapped to compensate.
        /// </summary>
        /// <param name="denseIndex"></param>
        private void ConsolidateDenseArray(int denseIndex)
        {
            //TODO this isn't properly cleaning up the links. They're pointing to the wrong place after consolidation.
            //After the RemoveALl test is complete, 1 should point to 0 and the rest to -1.
            //Reverse should have 0 point to 1 and the rest to -1. Somehow one of them still points to 3.
            int lastPopulatedDenseIndex = _count - 1;

            //If it's the last component in the dense list, no consolidation is needed
            if (denseIndex == lastPopulatedDenseIndex)
            {
                return;
            }

            //Move the last component into the removed component's index
            _denseComponents[denseIndex] = _denseComponents[lastPopulatedDenseIndex];
            _denseIndexToEntityIdMap[denseIndex] = _denseIndexToEntityIdMap[lastPopulatedDenseIndex];
            _denseIndexLinks[denseIndex] = _denseIndexLinks[lastPopulatedDenseIndex];
            _denseIndexLinksReverse[denseIndex] = _denseIndexLinksReverse[lastPopulatedDenseIndex];

            //If the moved component was the first component in the entity's list. Remap the entity's first dense index to the new dense index.
            //Otherwise remap the previous component's next link to point to the new dense index.
            var previousDenseIndex = _denseIndexLinksReverse[denseIndex];
            if (previousDenseIndex == -1)
            {
                int entityIdOfMovedComponent = _denseIndexToEntityIdMap[denseIndex];
                _entityIdToFirstDenseIndexMap[entityIdOfMovedComponent] = denseIndex;
            }
            else
            {
                _denseIndexLinks[previousDenseIndex] = denseIndex;
            }

            //Fix the previous link of the next denseIndex
            var nextDenseIndex = _denseIndexLinks[denseIndex];
            if (nextDenseIndex != -1)
            {
                _denseIndexLinksReverse[nextDenseIndex] = denseIndex;
            }

            //Cleanup
            _denseIndexLinks[lastPopulatedDenseIndex] = -1;
            _denseIndexLinksReverse[lastPopulatedDenseIndex] = -1;
            _denseIndexToEntityIdMap[lastPopulatedDenseIndex] = -1;
        }

        public bool HasComponent(int entityId) => _entityIdToFirstDenseIndexMap[entityId] >= 0;

        public T[] Get(int entityId)
        {
            var denseIndexes = new List<int>();
            for (int denseIndex = _entityIdToFirstDenseIndexMap[entityId]; denseIndex != -1; denseIndex = _denseIndexLinks[denseIndex])
            {
                denseIndexes.Add(denseIndex);
            }

            var components = new T[denseIndexes.Count];
            for (var componentIndex = 0; componentIndex < denseIndexes.Count; componentIndex++)
            {
                components[componentIndex] = _denseComponents[denseIndexes[componentIndex]];
            }

            return components;
        }

        public readonly struct DenseView(T[] components, int[] owners, int count)
        {
            public readonly T[] Components = components;
            public readonly int[] EntityIds = owners;
            public readonly int Count = count;
        }

        public DenseView GetDenseView() => new(_denseComponents, _denseIndexToEntityIdMap, _count);
    }
}