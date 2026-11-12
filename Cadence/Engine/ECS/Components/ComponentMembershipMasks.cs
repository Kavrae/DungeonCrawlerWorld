using System.Runtime.CompilerServices;

namespace Engine.ECS.Components;

/// <summary>Which pools hold each entity: one bit per registered pool, kept by the pools themselves as an entity gains its first and loses its last component of a type.</summary>
/// <remarks>
/// What lets destroying an entity visit only the pools it holds (ComponentManager.RemoveAllComponents) rather
/// than probing every registered pool. Exact, not grow-only: a bit is cleared the moment its pool lets the entity
/// go, so the cost of a destroy stays the entity's own pool count however long the session runs.
/// Entity-indexed like a Direct pool, in words of 64 bits per entity; a registration past a multiple of 64 widens
/// every entity's mask.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
internal sealed class ComponentMembershipMasks
{
    private ulong[] _words;
    private int _wordsPerEntity = 1;
    private int _entityCapacity;

    public ComponentMembershipMasks(int entityCapacity)
    {
        _entityCapacity = entityCapacity;
        _words = new ulong[entityCapacity];
    }

    /// <summary>How many 64-bit words each entity's mask spans.</summary>
    public int WordsPerEntity => _wordsPerEntity;

    /// <summary>Bytes held by every entity's mask.</summary>
    public long EstimatedBytes => (long)_words.Length * sizeof(ulong);

    /// <summary>Widens every mask, where narrower, to hold bitCount bits.</summary>
    public void EnsureBitCount(int bitCount)
    {
        var wordsPerEntity = System.Math.Max(1, (bitCount + 63) >> 6);
        if (wordsPerEntity <= _wordsPerEntity)
        {
            return;
        }

        var words = new ulong[(long)_entityCapacity * wordsPerEntity];
        for (var entityId = 0; entityId < _entityCapacity; entityId++)
        {
            Array.Copy(_words, entityId * _wordsPerEntity, words, entityId * wordsPerEntity, _wordsPerEntity);
        }

        _words = words;
        _wordsPerEntity = wordsPerEntity;
    }

    /// <summary>Grows every mask's entity range, where smaller, to cover ids below entityCapacity.</summary>
    public void EnsureEntityCapacity(int entityCapacity)
    {
        if (entityCapacity <= _entityCapacity)
        {
            return;
        }

        Array.Resize(ref _words, entityCapacity * _wordsPerEntity);
        _entityCapacity = entityCapacity;
    }

    /// <summary>Marks entityId as held by the pool at bit.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int entityId, int bit)
    {
        if ((uint)entityId >= (uint)_entityCapacity)
        {
            EnsureEntityCapacity(System.Math.Max(entityId + 1, _entityCapacity * 2));
        }

        _words[entityId * _wordsPerEntity + (bit >> 6)] |= 1UL << bit;
    }

    /// <summary>Marks entityId as no longer held by the pool at bit.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear(int entityId, int bit)
    {
        if ((uint)entityId < (uint)_entityCapacity)
        {
            _words[entityId * _wordsPerEntity + (bit >> 6)] &= ~(1UL << bit);
        }
    }

    /// <summary>The pools holding entityId, bits wordIndex * 64 and up: 0 for an id past the range.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong WordAt(int entityId, int wordIndex) =>
        (uint)entityId < (uint)_entityCapacity ? _words[entityId * _wordsPerEntity + wordIndex] : 0;
}

/// <summary>A pool that keeps its holders' bits in a ComponentMembershipMasks: every pool ComponentManager creates.</summary>
internal interface IMembershipMaskedComponentPool
{
    /// <summary>From now on, sets membershipBit for an entity as it gains its first component here and clears it as it loses its last.</summary>
    void AttachMembershipMasks(ComponentMembershipMasks membershipMasks, int membershipBit);
}
