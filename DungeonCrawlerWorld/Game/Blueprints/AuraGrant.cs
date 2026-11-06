namespace Game.Blueprints;

/// <summary>An aura every entity built with a definition radiates.</summary>
/// <param name="AuraId">The aura's Guid in the content's Auras table, resolved through the session's AuraCatalog.</param>
/// <param name="Power">The source's value at its own tile (see AuraSourceComponent).</param>
/// <param name="Size">How many tiles the source reaches.</param>
public sealed record AuraGrant(Guid AuraId, ushort Power, byte Size);
