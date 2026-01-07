using System;

namespace DungeonCrawlerWorld.Components
{
    public sealed class SparseSet<T> where T : struct
    {
        private int _maxEntities;
        private int[] _entityIdToDenseIndexMap;
        private int[] _denseIndexToEntityIdMap;
        private T[] _denseComponents;
        private readonly MergeAction<T> _mergeImplementation;

        private int _count;
        public int Count => _count;


        public SparseSet(int maximumEntityCount, int initialCapacity, MergeAction<T> mergeImplementation)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

            _maxEntities = maximumEntityCount;
            _entityIdToDenseIndexMap = new int[_maxEntities];
            for (int i = 0; i < _entityIdToDenseIndexMap.Length; i++)
            {
                _entityIdToDenseIndexMap[i] = -1;
            }

            _denseComponents = new T[initialCapacity];
            _denseIndexToEntityIdMap = new int[initialCapacity];
            _count = 0;

            _mergeImplementation = mergeImplementation;
        }

        public void Resize(int newMaximumEntityCount)
        {
            Array.Resize(ref _entityIdToDenseIndexMap, newMaximumEntityCount);
            for (int i = _maxEntities; i < newMaximumEntityCount; i++)
            {
                _entityIdToDenseIndexMap[i] = -1;
            }
            _maxEntities = newMaximumEntityCount;
        }

        public void Add(int entityId, T newComponent)
        {
            var denseIndex = _entityIdToDenseIndexMap[entityId];
            if (denseIndex >= 0)
            {
                ref var existingComponent = ref _denseComponents[denseIndex];
                _mergeImplementation(ref existingComponent, newComponent);
                return;
            }

            if (_count == _denseComponents.Length)
            {
                var newSize = _denseComponents.Length * 2;
                Array.Resize(ref _denseComponents, newSize);
                Array.Resize(ref _denseIndexToEntityIdMap, newSize);
            }

            _denseComponents[_count] = newComponent;
            _denseIndexToEntityIdMap[_count] = entityId;
            _entityIdToDenseIndexMap[entityId] = _count;
            _count++;
        }

        public bool HasComponent(int entityId) => _entityIdToDenseIndexMap[entityId] >= 0;

        public ref T Get(int entityId)
        {
            var denseIndex = _entityIdToDenseIndexMap[entityId];
            return ref _denseComponents[denseIndex];
        }

        public ref T[] AllComponents => ref _denseComponents;

        public int[] AllEntityIds => _denseIndexToEntityIdMap;

        public bool Remove(int entityId)
        {
            var denseIndex = _entityIdToDenseIndexMap[entityId];
            if (denseIndex < 0)
            {
                return false;
            }

            var lastAssignedDenseIndex = _count - 1;
            if (denseIndex != lastAssignedDenseIndex)
            {
                _denseComponents[denseIndex] = _denseComponents[lastAssignedDenseIndex];
                _denseIndexToEntityIdMap[denseIndex] = _denseIndexToEntityIdMap[lastAssignedDenseIndex];
                _entityIdToDenseIndexMap[_denseIndexToEntityIdMap[denseIndex]] = denseIndex;
            }

            _entityIdToDenseIndexMap[entityId] = -1;
            _count--;
            return true;
        }
    }
}