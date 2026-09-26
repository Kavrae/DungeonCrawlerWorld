using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;

namespace Engine.Diagnostics;

/// <summary>Measures every component pool's memory, distinct values and value churn across a benchmark range, then writes one report.</summary>
/// <remarks>
/// CaptureBaseline copies every pool's values when the range opens; Complete compares them with the
/// pools when it closes. An entity counts as changed when any of its values in that pool differ, or
/// it gained or lost one; entities destroyed during the range (or whose id was recycled, checked
/// through EntityKey) are left out rather than counted as changed.
///
/// Copying every pool is as large as the pools themselves and allocates heavily, so a run with this
/// on is for memory, not for timing. The process figures (allocation and collections before the
/// range) are read before the copy.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class PoolMemoryReport(ComponentManager componentManager, EntityManager entityManager)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ComponentManager _componentManager = componentManager ?? throw new ArgumentNullException(nameof(componentManager));
    private readonly EntityManager _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
    private readonly List<PoolSampler> _samplers = [];

    private ProcessFigures _atBaseline;

    public bool HasBaseline { get; private set; }

    /// <summary>Copies every pool's current values and records the process's allocation and collection counts so far.</summary>
    public void CaptureBaseline()
    {
        _atBaseline = new ProcessFigures(
            GC.GetTotalAllocatedBytes(precise: true),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));

        _samplers.Clear();
        foreach (var pool in _componentManager.AllPools)
        {
            if (PoolSampler.TryCreate(pool, _entityManager) is { } sampler)
            {
                sampler.CaptureBaseline();
                _samplers.Add(sampler);
            }
        }

        HasBaseline = true;
    }

    /// <summary>Compares every pool with its baseline copy, descending by estimated bytes.</summary>
    public IReadOnlyList<PoolMemoryReportEntry> Complete()
    {
        if (!HasBaseline)
        {
            throw new InvalidOperationException("CaptureBaseline must run before Complete.");
        }

        var entries = _samplers.Select(static sampler => sampler.Complete()).ToList();
        entries.Sort(static (a, b) => b.EstimatedBytes.CompareTo(a.EstimatedBytes));
        return entries;
    }

    /// <summary>Completes the comparison and writes memory-&lt;timestamp&gt;-&lt;pid&gt;.json and .txt to outputDirectory, returning the json path.</summary>
    public string WriteReport(string outputDirectory, int? randomSeed, BenchmarkFrameRange range)
    {
        var entries = Complete();
        _samplers.Clear();
        HasBaseline = false;

        var liveHeapBytes = GC.GetTotalMemory(forceFullCollection: true);
        using var process = Process.GetCurrentProcess();

        var report = new MemoryReport(
            DateTime.UtcNow,
            randomSeed,
            Environment.ProcessId,
            range.StartFrame,
            range.EndFrame,
            _entityManager.LivingEntityCount,
            _atBaseline.AllocatedBytes,
            _atBaseline.Gen0Collections,
            _atBaseline.Gen1Collections,
            _atBaseline.Gen2Collections,
            liveHeapBytes,
            process.PeakWorkingSet64,
            entries.Sum(static entry => entry.EstimatedBytes),
            entries);

        Directory.CreateDirectory(outputDirectory);
        var stem = Path.Combine(outputDirectory, $"memory-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}");
        File.WriteAllText(stem + ".json", JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllLines(stem + ".txt", FormatText(report));
        return stem + ".json";
    }

    private static IEnumerable<string> FormatText(MemoryReport report)
    {
        yield return $"[Memory] seed {report.RandomSeed}, frames {report.StartFrame}-{report.EndFrame}, {report.LivingEntities:N0} living entities";
        yield return $"Allocated before range: {report.AllocatedBytesBeforeRange / 1048576.0:N0} MB, collections gen0/1/2: {report.Gen0CollectionsBeforeRange}/{report.Gen1CollectionsBeforeRange}/{report.Gen2CollectionsBeforeRange}";
        yield return $"Live heap at end: {report.LiveHeapBytesAtEnd / 1048576.0:N0} MB, peak working set: {report.PeakWorkingSetBytes / 1048576.0:N0} MB, pools: {report.TotalPoolBytes / 1048576.0:N1} MB";
        yield return "";
        yield return $"{"Kind",-6} | {"Component",-38} | {"Size",4} | {"Refs",-4} | {"Count",10} | {"MB",7} | {"Distinct",10} | Changed (survivors)";
        foreach (var entry in report.Pools)
        {
            var changed = entry.SurvivingHolders == 0
                ? "-"
                : $"{entry.ChangedHolders:N0}/{entry.SurvivingHolders:N0} ({100.0 * entry.ChangedHolders / entry.SurvivingHolders:F1}%)";
            yield return $"{entry.PoolKind,-6} | {entry.ComponentType,-38} | {entry.ComponentSize,4} | {(entry.HoldsReferences ? "yes" : "no"),-4} | {entry.Count,10:N0} | {entry.EstimatedBytes / 1048576.0,7:F1} | {entry.DistinctValues,10:N0} | {changed}";
        }
    }

    private readonly record struct ProcessFigures(long AllocatedBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections);

    private sealed record MemoryReport(
        DateTime TimestampUtc,
        int? RandomSeed,
        int ProcessId,
        long StartFrame,
        long EndFrame,
        int LivingEntities,
        long AllocatedBytesBeforeRange,
        int Gen0CollectionsBeforeRange,
        int Gen1CollectionsBeforeRange,
        int Gen2CollectionsBeforeRange,
        long LiveHeapBytesAtEnd,
        long PeakWorkingSetBytes,
        long TotalPoolBytes,
        IReadOnlyList<PoolMemoryReportEntry> Pools);

    /// <summary>A pool's typed baseline copy, created per component type through reflection since the report only sees the untyped pool list.</summary>
    private abstract class PoolSampler
    {
        public static PoolSampler? TryCreate(IComponentPool pool, EntityManager entityManager)
        {
            if (pool is not IMemoryReportingComponentPool)
            {
                return null;
            }

            var samplerType = typeof(PoolSampler<>).MakeGenericType(pool.ComponentType);
            return (PoolSampler)Activator.CreateInstance(samplerType, pool, entityManager)!;
        }

        public abstract void CaptureBaseline();

        public abstract PoolMemoryReportEntry Complete();
    }

    private sealed class PoolSampler<T>(IComponentPool pool, EntityManager entityManager) : PoolSampler where T : struct
    {
        private Holdings _baseline;
        private EntityKey[] _baselineKeys = [];
        private int _distinctValues;

        public override void CaptureBaseline()
        {
            _baseline = Read();
            _baselineKeys = new EntityKey[_baseline.EntityIds.Length];
            for (var i = 0; i < _baseline.EntityIds.Length; i++)
            {
                _baselineKeys[i] = entityManager.Keys.GetKey(_baseline.EntityIds[i]);
            }

            _distinctValues = new HashSet<T>(_baseline.Values, ValueComparer<T>.Instance).Count;
        }

        public override PoolMemoryReportEntry Complete()
        {
            var current = Read();
            var (holders, survivors, changed) = CountChanges(current);
            var memoryReportingPool = (IMemoryReportingComponentPool)pool;

            return new PoolMemoryReportEntry(
                typeof(T).Name,
                PoolKind(),
                Unsafe.SizeOf<T>(),
                RuntimeHelpers.IsReferenceOrContainsReferences<T>(),
                memoryReportingPool.Count,
                memoryReportingPool.EstimatedBytes,
                _distinctValues,
                holders,
                survivors,
                changed);
        }

        private string PoolKind() => pool switch
        {
            DirectComponentPool<T> => "Direct",
            PackedComponentPool<T> => "Packed",
            MultiComponentPool<T> => "Multi",
            _ => pool.GetType().Name,
        };

        /// <summary>Every (entity id, value) the pool holds, sorted by entity id so one entity's values sit together.</summary>
        private Holdings Read()
        {
            int[] entityIds;
            T[] values;
            switch (pool)
            {
                case DirectComponentPool<T> direct:
                {
                    var present = direct.Present;
                    var components = direct.Components;
                    var ids = new List<int>(direct.Count);
                    var copies = new List<T>(direct.Count);
                    for (var entityId = 0; entityId < present.Length; entityId++)
                    {
                        if (present[entityId] != 0)
                        {
                            ids.Add(entityId);
                            copies.Add(components[entityId]);
                        }
                    }

                    return new Holdings([.. ids], [.. copies]);
                }
                case PackedComponentPool<T> packed:
                    entityIds = packed.EntityIds.ToArray();
                    values = packed.Components.ToArray();
                    break;
                case MultiComponentPool<T> multi:
                    entityIds = multi.EntityIds.ToArray();
                    values = multi.Components.ToArray();
                    break;
                default:
                    return new Holdings([], []);
            }

            Array.Sort(entityIds, values);
            return new Holdings(entityIds, values);
        }

        private (int Holders, int Survivors, int Changed) CountChanges(Holdings current)
        {
            int holders = 0, survivors = 0, changed = 0;
            var baselineIndex = 0;
            var currentIndex = 0;

            while (baselineIndex < _baseline.EntityIds.Length)
            {
                var entityId = _baseline.EntityIds[baselineIndex];
                var baselineEnd = GroupEnd(_baseline.EntityIds, baselineIndex);
                var key = _baselineKeys[baselineIndex];

                while (currentIndex < current.EntityIds.Length && current.EntityIds[currentIndex] < entityId)
                {
                    currentIndex++;
                }

                var currentEnd = currentIndex < current.EntityIds.Length && current.EntityIds[currentIndex] == entityId
                    ? GroupEnd(current.EntityIds, currentIndex)
                    : currentIndex;

                holders++;
                if (entityManager.EntityExists(entityId) && entityManager.Keys.GetKey(entityId) == key)
                {
                    survivors++;
                    if (!SameMultiset(_baseline.Values.AsSpan(baselineIndex, baselineEnd - baselineIndex), current.Values.AsSpan(currentIndex, currentEnd - currentIndex)))
                    {
                        changed++;
                    }
                }

                baselineIndex = baselineEnd;
                currentIndex = currentEnd;
            }

            return (holders, survivors, changed);
        }

        private static int GroupEnd(int[] entityIds, int start)
        {
            var end = start + 1;
            while (end < entityIds.Length && entityIds[end] == entityIds[start])
            {
                end++;
            }

            return end;
        }

        private static bool SameMultiset(ReadOnlySpan<T> before, ReadOnlySpan<T> after)
        {
            if (before.Length != after.Length)
            {
                return false;
            }

            if (before.Length == 1)
            {
                return ValueComparer<T>.Instance.Equals(before[0], after[0]);
            }

            Span<bool> matched = after.Length <= 64 ? stackalloc bool[after.Length] : new bool[after.Length];
            foreach (var value in before)
            {
                var found = false;
                for (var i = 0; i < after.Length; i++)
                {
                    if (!matched[i] && ValueComparer<T>.Instance.Equals(value, after[i]))
                    {
                        matched[i] = true;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private readonly record struct Holdings(int[] EntityIds, T[] Values);
    }

    /// <summary>Field-by-field value equality for any component struct.</summary>
    /// <remarks>
    /// Not EqualityComparer&lt;T&gt;.Default: a struct that doesn't implement IEquatable falls back to
    /// ValueType.GetHashCode, which hashes only its first field, and a HashSet of 5M such values
    /// degrades to quadratic. Not a bitwise comparison either: padding bytes (a 1-byte enum beside a
    /// ushort) hold whatever the copy left there, and read as changes that never happened.
    /// </remarks>
    private sealed class ValueComparer<T> : IEqualityComparer<T> where T : struct
    {
        private static readonly MethodInfo InlineArrayEqualsMethod = typeof(ValueComparer<T>).GetMethod(nameof(InlineArrayEquals), BindingFlags.Static | BindingFlags.NonPublic)!;

        private static readonly MethodInfo InlineArrayHashMethod = typeof(ValueComparer<T>).GetMethod(nameof(InlineArrayHash), BindingFlags.Static | BindingFlags.NonPublic)!;

        public static readonly ValueComparer<T> Instance = new();

        private readonly Func<T, T, bool> _equals;
        private readonly Func<T, int> _hash;

        private ValueComparer()
        {
            var left = Expression.Parameter(typeof(T), "left");
            var right = Expression.Parameter(typeof(T), "right");
            var hash = Expression.Variable(typeof(HashCode), "hash");
            var hashAdd = typeof(HashCode).GetMethods().First(static method => method.Name == nameof(HashCode.Add) && method.GetParameters().Length == 1);

            Expression equality = Expression.Constant(true);
            var hashBody = new List<Expression>();
            foreach (var field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var leftField = Expression.Field(left, field);
                var rightField = Expression.Field(right, field);

                if (field.FieldType.GetCustomAttribute<InlineArrayAttribute>() is not null)
                {
                    equality = Expression.AndAlso(equality, Expression.Call(InlineArrayEqualsMethod.MakeGenericMethod(field.FieldType), leftField, rightField));
                    hashBody.Add(Expression.Call(hash, hashAdd.MakeGenericMethod(typeof(int)), Expression.Call(InlineArrayHashMethod.MakeGenericMethod(field.FieldType), leftField)));
                    continue;
                }

                var comparerType = typeof(EqualityComparer<>).MakeGenericType(field.FieldType);
                var comparer = Expression.Property(null, comparerType, nameof(EqualityComparer<int>.Default));
                equality = Expression.AndAlso(equality, Expression.Call(comparer, comparerType.GetMethod(nameof(Equals), [field.FieldType, field.FieldType])!, leftField, rightField));
                hashBody.Add(Expression.Call(hash, hashAdd.MakeGenericMethod(field.FieldType), leftField));
            }

            hashBody.Add(Expression.Call(hash, typeof(HashCode).GetMethod(nameof(HashCode.ToHashCode))!));
            _equals = Expression.Lambda<Func<T, T, bool>>(equality, left, right).Compile();
            _hash = Expression.Lambda<Func<T, int>>(Expression.Block([hash], hashBody), left).Compile();
        }

        public bool Equals(T x, T y) => _equals(x, y);

        public int GetHashCode(T obj) => _hash(obj);

        /// <summary>Compares an [InlineArray] field's bytes, since every built-in equality on one throws and its elements sit contiguously with no padding between them.</summary>
        private static bool InlineArrayEquals<TField>(TField left, TField right) where TField : struct =>
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<TField>(in left))
                .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<TField>(in right)));

        private static int InlineArrayHash<TField>(TField value) where TField : struct
        {
            var hash = new HashCode();
            hash.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<TField>(in value)));
            return hash.ToHashCode();
        }
    }
}
