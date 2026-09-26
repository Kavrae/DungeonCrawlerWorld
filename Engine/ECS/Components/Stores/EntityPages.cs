using System.Runtime.CompilerServices;

namespace Engine.ECS.Components.Stores;

/// <summary>A per-entity value table split into fixed-size pages, each allocated the first time an entity in its range is written.</summary>
/// <remarks>
/// The entity-indexed half of a sparse set. A flat array costs every pool its full entity capacity
/// whether it holds one component or a million; pages cost only the ranges its holders actually fall
/// in, plus an 8-byte slot per page for the table. A read of an entity whose page doesn't exist
/// returns the empty value without allocating. Pages are never freed: a Multi pool's entity version
/// must keep counting up after an entity's last instance is removed.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
internal sealed class EntityPages<TEntry> where TEntry : struct
{
    private const int PageShift = 10;
    private const int PageSize = 1 << PageShift;
    private const int PageMask = PageSize - 1;

    private readonly TEntry _empty;
    private TEntry[]?[] _pages;
    private int _allocatedPageCount;

    /// <param name="entityCapacity">The entity id space the page table starts out covering; it grows on demand past this.</param>
    /// <param name="empty">The value an entity holds until it is first written.</param>
    public EntityPages(int entityCapacity, TEntry empty)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityCapacity);

        _empty = empty;
        _pages = new TEntry[]?[PageCountFor(entityCapacity)];
    }

    /// <summary>The entity id space the page table covers.</summary>
    public int Capacity => _pages.Length << PageShift;

    /// <summary>Bytes held by the page table and every allocated page.</summary>
    public long EstimatedBytes => (long)_pages.Length * IntPtr.Size + (long)_allocatedPageCount * PageSize * Unsafe.SizeOf<TEntry>();

    /// <summary>entityId's value, or the empty value if it was never written.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TEntry Get(int entityId)
    {
        var pageIndex = (uint)entityId >> PageShift;
        var pages = _pages;
        if (pageIndex < (uint)pages.Length && pages[pageIndex] is { } page)
        {
            return page[entityId & PageMask];
        }

        return _empty;
    }

    /// <summary>A writable reference to entityId's value, allocating its page (and growing the page table) if needed.</summary>
    public ref TEntry GetWritable(int entityId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityId);

        var pageIndex = entityId >> PageShift;
        if (pageIndex >= _pages.Length)
        {
            EnsureCapacity(entityId + 1);
        }

        var page = _pages[pageIndex] ??= AllocatePage();
        return ref page[entityId & PageMask];
    }

    /// <summary>Grows the page table, where smaller, to cover entity ids below entityCapacity. Allocates no pages.</summary>
    public void EnsureCapacity(int entityCapacity)
    {
        var pageCount = PageCountFor(entityCapacity);
        if (pageCount > _pages.Length)
        {
            Array.Resize(ref _pages, System.Math.Max(pageCount, _pages.Length * 2));
        }
    }

    /// <summary>Returns every entity to the empty value, releasing every page.</summary>
    public void Clear()
    {
        Array.Clear(_pages);
        _allocatedPageCount = 0;
    }

    private TEntry[] AllocatePage()
    {
        var page = new TEntry[PageSize];
        if (!EqualityComparer<TEntry>.Default.Equals(_empty, default))
        {
            Array.Fill(page, _empty);
        }

        _allocatedPageCount++;
        return page;
    }

    private static int PageCountFor(int entityCapacity) => (entityCapacity + PageMask) >> PageShift;
}
