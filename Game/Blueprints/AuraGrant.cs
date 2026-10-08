using Game.Modules.Auras;

namespace Game.Blueprints;

/// <summary>An aura every entity built with a definition radiates.</summary>
/// <param name="Aura">The aura's definition, declared with the blueprint that radiates it.</param>
/// <param name="Power">The source's value at its own tile (see AuraSourceComponent).</param>
/// <param name="Size">How many tiles the source reaches.</param>
public sealed record AuraGrant(AuraDefinition Aura, ushort Power, byte Size);
