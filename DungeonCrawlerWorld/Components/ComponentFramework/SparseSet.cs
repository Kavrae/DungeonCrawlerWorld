using System;

namespace DungeonCrawlerWorld.Components
{
    public sealed class SparseSet<T> where T : struct
    {
        private int _maxEntities;
        private int[] _entityIdToDenseIndexMap;
        private int[] _denseIndexToEntityIdMap;
        private T[] _denseComponents;
        private int _count;

        public int Count => _count;


        public SparseSet(int maximumEntityCount, int initialDenseCapacity = 256)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntityCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialDenseCapacity);

            _maxEntities = maximumEntityCount;
            _entityIdToDenseIndexMap = new int[_maxEntities];
            for (int i = 0; i < _entityIdToDenseIndexMap.Length; i++)
            {
                _entityIdToDenseIndexMap[i] = -1;
            }

            _denseComponents = new T[initialDenseCapacity];
            _denseIndexToEntityIdMap = new int[_denseComponents.Length];
            _count = 0;
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

        public bool TryGetValue(int entityId, out T component)
        {
            var denseIndex = _entityIdToDenseIndexMap[entityId];
            if ((uint)denseIndex < (uint)_count)
            {
                component = _denseComponents[denseIndex];
                return true;
            }
            component = default;
            return false;
        }

        public bool Contains(int entityId) => _entityIdToDenseIndexMap[entityId] >= 0;

        public void Save(int entityId, T newComponent)
        {
            var denseIndex = _entityIdToDenseIndexMap[entityId];
            if (denseIndex >= 0)
            {
                _denseComponents[denseIndex] = newComponent;
                return;
            }

            if (_count == _denseComponents.Length)
            {
                Array.Resize(ref _denseComponents, _denseComponents.Length * 2);
                Array.Resize(ref _denseIndexToEntityIdMap, _denseIndexToEntityIdMap.Length * 2);
            }

            _denseComponents[_count] = newComponent;
            _denseIndexToEntityIdMap[_count] = entityId;
            _entityIdToDenseIndexMap[entityId] = _count;
            _count++;
        }

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

        public T[] Components => _denseComponents;
        public int[] EntityIds => _denseIndexToEntityIdMap;
    }
}