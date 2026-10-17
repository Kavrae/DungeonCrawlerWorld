namespace Game.Modules.Auras.Components;

/// <summary>One aura an entity radiates: which aura, how strongly and how far, and which toggle holds it.</summary>
/// <remarks>
/// Power is the aura's value at the source and Size how many tiles of Manhattan distance it reaches;
/// between them the aura's own falloff (AuraDefinition.Falloff) decides the value at each distance,
/// and the glow follows the same values, so what is drawn always matches what the aura reaches. What
/// the aura does and the colour it glows are the definition's (AuraCatalog). A Multi pool: an entity
/// holds one instance per source, and may hold several of one aura -- at most one with no key (a
/// blueprint's, or a timed grant's), and one more per toggle holding it. Every source adds to the
/// field. 8 bytes: Size sits beside AuraId so Power needs no padding.
/// </remarks>
/// <param name="auraId">The aura's session-local id (AuraCatalog).</param>
/// <param name="power">The aura's value at the source.</param>
/// <param name="size">How many tiles from the source it reaches.</param>
/// <param name="heldGrantKey">The key of the toggle holding this source (EffectContext.HeldGrantKey), or 0 for a source no toggle holds.</param>
public struct AuraSourceComponent(byte auraId, ushort power, byte size, uint heldGrantKey = AuraSourceComponent.NoHeldGrantKey)
{
    /// <summary>HeldGrantKey of a source no toggle holds.</summary>
    public const uint NoHeldGrantKey = 0;

    public byte AuraId { get; set; } = auraId;
    public byte Size { get; set; } = size;
    public ushort Power { get; set; } = power;

    /// <summary>The key of the toggle holding this source, or NoHeldGrantKey. Only that toggle removes it.</summary>
    public uint HeldGrantKey { get; } = heldGrantKey;

    public override readonly string ToString() => $"AuraId : {AuraId}\nPower : {Power}\nSize : {Size}\nHeldGrantKey : {HeldGrantKey}";
}
