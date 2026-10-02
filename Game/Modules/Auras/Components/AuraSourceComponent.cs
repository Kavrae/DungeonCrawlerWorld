namespace Game.Modules.Auras.Components;

/// <summary>One aura an entity radiates: which aura, and how strongly.</summary>
/// <remarks>
/// Strength is both the aura's magnitude at the source and what sets its reach: it halves with each
/// tile of Manhattan distance (DistanceFalloff), and the glow follows the same falloff, so what is
/// drawn always matches what the aura reaches. What the aura does and the colour it glows are the
/// definition's (AuraCatalog). A Multi pool: an entity radiating several auras holds one instance
/// per aura.
/// </remarks>
/// <param name="auraId">The aura's session-local id (AuraCatalog).</param>
public struct AuraSourceComponent(byte auraId, byte strength)
{
    public byte AuraId { get; set; } = auraId;
    public byte Strength { get; set; } = strength;

    public override readonly string ToString() => $"AuraId : {AuraId}\nStrength : {Strength}";
}
