namespace Game.Modules.Core.Components;

/// <summary>Represents the kind of non-blocking behavior a NonBlockingComponent instance grants.</summary>
/// <remarks>
/// Non-blocking entities can exist on the same map tile as other entities. They do not block movement or interactions for other entities on the same tile.
/// Actions performed on a tile are performed on both blocking and non-blocking entities on that tile.
/// Tiny represents a small entity that occupies a smaller space on the map.
/// Phasing represents an entity that can pass through other entities.</remarks>
/// <cleanupVersion>1</cleanupVersion>
[Flags]
public enum NonBlockingKind : byte
{
    None = 0,
    Tiny = 1 << 0,
    Phasing = 1 << 1,
}

public struct NonBlockingComponent(NonBlockingKind kind = NonBlockingKind.None)
{
    public NonBlockingKind Kind = kind;

    public override readonly string ToString() => $"Kind : {Kind}";
}
