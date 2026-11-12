using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Engine.ECS.Components.Stores;

/// <summary>A per-entity value table split into fixed-size pages, each allocated when an entity in its range is first claimed and freed when its last one is released.</summary>
/// <remarks>
/// The entity-indexed half of a sparse set. A flat array costs every pool its full entity capacity
/// whether it holds one component or a million; pages cost only the ranges its holders currently fall
/// in, plus an 8-byte slot and a 4-byte count per page for the table. Entity ids are recycled, so over a
/// long session a rare component's holders pass through every range: freeing a page once nothing in it
/// is held keeps the cost to the ranges held now rather than every range ever held.
///
/// An entity's slot is claimed (empty to held) with <see cref="Claim"/> and released (held to empty)
/// with <see cref="Release"/>, which count the page's holders; a held slot is changed in place through
/// <see cref="GetExisting"/>. A read of an entity whose page doesn't exist returns the empty value
/// without allocating. A freed page is all-empty and is kept as a spare for the next claim anywhere in
/// the table, so pages in use plus spares never exceed the most pages ever in use at once: a transient
/// component held for a frame by entities scattered across many ranges (a pending activation) reuses the
/// same pages every frame instead of allocating them, and the table stops growing at its busiest moment
/// rather than at every range ever touched.
/// </remarks>
/// <cleanupVersion>2</cleanupVersion>
internal sealed class EntityPages<TEntry> where TEntry : struct
{
    private const int PageShift = 10;
    private const int PageSize = 1 << PageShift;
    private const int PageMask = PageSize - 1;

    private readonly TEntry _empty;
    private TEntry[]?[] _pages;
    private int[] _heldCountByPage;
    private readonly Stack<TEntry[]> _sparePages = new();
    private int _allocatedPageCount;

    /// <param name="entityCapacity">The entity id space the page table starts out covering; it grows on demand past this.</param>
    /// <param name="empty">The value an entity holds until it is claimed, and again once released.</param>
    public EntityPages(int entityCapacity, TEntry empty)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityCapacity);

        _empty = empty;
        var pageCount = PageCountFor(entityCapacity);
        _pages = new TEntry[]?[pageCount];
        _heldCountByPage = new int[pageCount];
    }

    /// <summary>The entity id space the page table covers.</summary>
    public int Capacity => _pages.Length << PageShift;

    /// <summary>How many pages are in use, not counting spares.</summary>
    public int AllocatedPageCount => _allocatedPageCount;

    /// <summary>Bytes held by the page table, every page in use and every spare.</summary>
    public long EstimatedBytes =>
        (long)_pages.Length * (IntPtr.Size + sizeof(int)) +
        (long)(_allocatedPageCount + _sparePages.Count) * PageSize * Unsafe.SizeOf<TEntry>();

    /// <summary>entityId's value, or the empty value if it is not held.</summary>
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

    /// <summary>Takes entityId's slot from empty to held and returns it for the caller to fill, allocating its page (and growing the page table) if needed.</summary>
    /// <remarks>The slot must be empty: claiming a held one counts it twice and its page is never freed.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TEntry Claim(int entityId)
    {
        var pageIndex = (uint)entityId >> PageShift;
        var pages = _pages;
        if (pageIndex < (uint)pages.Length && pages[pageIndex] is { } page)
        {
            ThrowIfHeld(page, entityId);
            _heldCountByPage[pageIndex]++;
            return ref page[entityId & PageMask];
        }

        return ref ClaimOnNewPage(entityId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ref TEntry ClaimOnNewPage(int entityId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityId);

        var pageIndex = entityId >> PageShift;
        if (pageIndex >= _pages.Length)
        {
            var pageCount = System.Math.Max(pageIndex + 1, _pages.Length * 2);
            Array.Resize(ref _pages, pageCount);
            Array.Resize(ref _heldCountByPage, pageCount);
        }

        var page = _pages[pageIndex] ??= TakePage();
        _heldCountByPage[pageIndex]++;
        return ref page[entityId & PageMask];
    }

    /// <summary>A writable reference to a held entity's value, for a change that leaves it held.</summary>
    /// <remarks>For a write that follows a read proving the entity is held (a pool's own holders); it neither grows the table nor allocates, and an entity with no page fails the array access. Never write the empty value through it: that is Release.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TEntry GetExisting(int entityId) => ref _pages[(uint)entityId >> PageShift]![entityId & PageMask];

    /// <summary>Takes a held entity's slot back to empty, freeing its page when nothing else in it is held.</summary>
    /// <remarks>The empty value is written before the page is let go, so a reference a caller still holds into the slot reads empty.</remarks>
    public void Release(int entityId)
    {
        var pageIndex = (uint)entityId >> PageShift;
        var page = _pages[pageIndex]!;
        page[entityId & PageMask] = _empty;

        if (--_heldCountByPage[pageIndex] != 0)
        {
            return;
        }

        _pages[pageIndex] = null;
        _allocatedPageCount--;
        _sparePages.Push(page);
    }

    /// <summary>Grows the page table, where smaller, to exactly cover entity ids below entityCapacity. Allocates no pages.</summary>
    /// <remarks>Sized exactly: the caller (EntityManager) already chose the capacity to grow to.</remarks>
    public void EnsureCapacity(int entityCapacity)
    {
        var pageCount = PageCountFor(entityCapacity);
        if (pageCount > _pages.Length)
        {
            Array.Resize(ref _pages, pageCount);
            Array.Resize(ref _heldCountByPage, pageCount);
        }
    }

    /// <summary>Returns every entity to the empty value, dropping every page and spare.</summary>
    public void Clear()
    {
        Array.Clear(_pages);
        Array.Clear(_heldCountByPage);
        _sparePages.Clear();
        _allocatedPageCount = 0;
    }

    private TEntry[] TakePage()
    {
        _allocatedPageCount++;
        if (_sparePages.TryPop(out var sparePage))
        {
            return sparePage;
        }

        var page = new TEntry[PageSize];
        if (!EqualityComparer<TEntry>.Default.Equals(_empty, default))
        {
            Array.Fill(page, _empty);
        }

        return page;
    }

    [Conditional("DEBUG")]
    private void ThrowIfHeld(TEntry[] page, int entityId)
    {
        if (!EqualityComparer<TEntry>.Default.Equals(page[entityId & PageMask], _empty))
        {
            throw new InvalidOperationException($"Entity {entityId}'s {typeof(TEntry).Name} slot was claimed while already held.");
        }
    }

    private static int PageCountFor(int entityCapacity) => (entityCapacity + PageMask) >> PageShift;
}
