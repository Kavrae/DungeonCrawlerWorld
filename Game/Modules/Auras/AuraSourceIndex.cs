using Engine.Math;
using Game.Modules.Auras.Components;
using Game.World;

namespace Game.Modules.Auras;

/// <summary>Where each aura's entity sources are, bucketed by 32x32 chunk, and who each is credited to -- so a cell's strongest single contributor can be found without scanning every source.</summary>
/// <remarks>
/// Holds entity sources only: terrain cells are counted as one share, the cell's total minus the
/// entity sources' values (AuraField.Attribute). Each entry's attribution is fixed when it is added --
/// the entity carrying it, or for an anchor whoever placed it -- and kept through moves. A query
/// looks only at the chunks the aura's largest source could reach from, so a lookup near a few small
/// sources reads a handful of buckets.
/// </remarks>
public sealed class AuraSourceIndex
{
    private const int ChunkShift = 5;

    private readonly AuraEntries?[] _entriesByAuraId = new AuraEntries?[byte.MaxValue + 1];

    /// <summary>One entity source: whose it is, where it is, its shape and whom its effects are credited to.</summary>
    private readonly record struct Entry(int EntityId, uint HeldGrantKey, Vector3Int Position, ushort Power, byte Size, ActionSource Attribution);

    private sealed class AuraEntries
    {
        public Dictionary<(int ChunkX, int ChunkY, int Layer), List<Entry>> ByChunk { get; } = [];

        public int Count { get; set; }

        /// <summary>The largest size any source of the aura has had: how far a query looks. Never shrinks, so it never misses one.</summary>
        public int MaximumSize { get; set; }
    }

    /// <summary>Whether any entity source of auraId is indexed.</summary>
    public bool HasEntitySources(byte auraId) => _entriesByAuraId[auraId] is { Count: > 0 };

    public void Add(int entityId, Vector3Int position, AuraSourceComponent source, ActionSource attribution)
    {
        var entries = _entriesByAuraId[source.AuraId] ??= new AuraEntries();
        var key = ChunkOf(position);
        if (!entries.ByChunk.TryGetValue(key, out var bucket))
        {
            bucket = [];
            entries.ByChunk.Add(key, bucket);
        }

        bucket.Add(new Entry(entityId, source.HeldGrantKey, position, source.Power, source.Size, attribution));
        entries.Count++;
        entries.MaximumSize = Math.Max(entries.MaximumSize, source.Size);
    }

    public void Remove(int entityId, Vector3Int position, AuraSourceComponent source) => TakeOut(entityId, position, source);

    /// <summary>Moves entityId's source from previousPosition, where it was added, to currentPosition, keeping its attribution.</summary>
    public void Move(int entityId, Vector3Int previousPosition, Vector3Int currentPosition, AuraSourceComponent source)
    {
        if (TakeOut(entityId, previousPosition, source) is { } entry)
        {
            Add(entityId, currentPosition, source, entry.Attribution);
        }
    }

    /// <summary>The strongest entity source of auraId at position other than the owner's own, by value there under falloff -- ties to the lowest key -- and the sums of every entity source's value there and of the owner's own.</summary>
    /// <remarks>The owner's own are the sources ownerEntityId carries. An anchor it placed is not its own: it reaches and credits its placer like anyone else, and an aura that shouldn't hit its placer grants it an immunity first.</remarks>
    /// <returns>False when no entity source other than the owner's own reaches position; the totals are still summed.</returns>
    public bool TryFindStrongest(byte auraId, Vector3Int position, int ownerEntityId, AuraFalloff falloff, out ActionSource strongest, out int strongestValue, out int entityTotal, out int ownTotal)
    {
        strongest = default;
        strongestValue = 0;
        entityTotal = 0;
        ownTotal = 0;
        if (_entriesByAuraId[auraId] is not { Count: > 0 } entries)
        {
            return false;
        }

        var chunkRadius = (entries.MaximumSize + (1 << ChunkShift) - 1) >> ChunkShift;
        var (centreChunkX, centreChunkY, layer) = ChunkOf(position);
        var found = false;

        for (var chunkY = centreChunkY - chunkRadius; chunkY <= centreChunkY + chunkRadius; chunkY++)
        {
            for (var chunkX = centreChunkX - chunkRadius; chunkX <= centreChunkX + chunkRadius; chunkX++)
            {
                if (!entries.ByChunk.TryGetValue((chunkX, chunkY, layer), out var bucket))
                {
                    continue;
                }

                foreach (var entry in bucket)
                {
                    var value = falloff.ValueAt(entry.Power, entry.Size, GridDistance.ManhattanDistance(entry.Position, position));
                    if (value <= 0)
                    {
                        continue;
                    }

                    entityTotal += value;
                    if (entry.EntityId == ownerEntityId)
                    {
                        ownTotal += value;
                        continue;
                    }

                    if (!found || value > strongestValue || (value == strongestValue && entry.Attribution.Key.Value < strongest.Key.Value))
                    {
                        strongest = entry.Attribution;
                        strongestValue = value;
                        found = true;
                    }
                }
            }
        }

        return found;
    }

    private Entry? TakeOut(int entityId, Vector3Int position, AuraSourceComponent source)
    {
        if (_entriesByAuraId[source.AuraId] is not { } entries || !entries.ByChunk.TryGetValue(ChunkOf(position), out var bucket))
        {
            return null;
        }

        for (var index = 0; index < bucket.Count; index++)
        {
            var entry = bucket[index];
            if (entry.EntityId == entityId && entry.HeldGrantKey == source.HeldGrantKey && entry.Position == position)
            {
                bucket[index] = bucket[^1];
                bucket.RemoveAt(bucket.Count - 1);
                entries.Count--;
                return entry;
            }
        }

        return null;
    }

    private static (int ChunkX, int ChunkY, int Layer) ChunkOf(Vector3Int position) => (position.X >> ChunkShift, position.Y >> ChunkShift, position.Z);
}
