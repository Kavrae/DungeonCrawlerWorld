namespace Engine.Tiles;

/// <summary>How an effect gets from its caster to where it lands.</summary>
/// <remarks>Says nothing about when it happens: activation timing is the action's own.</remarks>
public enum TargetPath : byte
{
    /// <summary>Appears on its area with no path to it: nothing between the caster and the aim is crossed or met.</summary>
    Placed,

    /// <summary>A straight path from the caster's footprint tile nearest the aim, through the aim; what it meets is the hit count's to decide.</summary>
    Projectile,

    /// <summary>Arcs over every entity and lands on the aimed tile; walls still stop it.</summary>
    Lobbed,
}
