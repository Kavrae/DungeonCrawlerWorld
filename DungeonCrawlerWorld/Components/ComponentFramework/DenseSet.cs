using System;

namespace DungeonCrawlerWorld.Components
{
    public sealed class DenseSet<T> where T : struct
    {
        private T[] _components;
        private byte[] _componentPresent;
        private readonly MergeAction<T> _mergeImplementation;

        private int _count = 0;
        public int Count => _count;

        public DenseSet(int initialCapacity, MergeAction<T> mergeImplementation)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
            ArgumentNullException.ThrowIfNull(mergeImplementation);

            _components = new T[initialCapacity];
            _componentPresent = new byte[initialCapacity];
            for (var index = 0; index < _components.Length; index++)
            {
                _componentPresent[index] = 0;
            }

            _mergeImplementation = mergeImplementation;
        }

        public void Resize(int newMaximumEntityCount)
        {
            var currentMaximum = _components.Length;

            Array.Resize(ref _components, newMaximumEntityCount);
            Array.Resize(ref _componentPresent, newMaximumEntityCount);

            for (int i = currentMaximum; i < newMaximumEntityCount; i++)
            {
                _componentPresent[i] = 0;
            }
        }

        public void Add(int entityId, T newComponent)
        {
            if (_componentPresent[entityId] != 0)
            {
                ref var existingComponent = ref _components[entityId];
                _mergeImplementation(ref existingComponent, newComponent);
            }
            else
            {
                _components[entityId] = newComponent;
                _componentPresent[entityId] = 1;
                _count++;
            }
        }

        public bool HasComponent(int entityId) => _componentPresent[entityId] != 0;

        public ref T Get(int entityId) => ref _components[entityId];

        public ref T[] GetAll() => ref _components;

        public void Remove(int entityId)
        {
            _componentPresent[entityId] = 0;
            _count--;
        }
    }
}