using Game.Modules.Auras;

namespace Game.Blueprints;

/// <summary>An aura every entity built with a definition radiates.</summary>
/// <param name="Aura">The aura's definition, declared with the blueprint that radiates it.</param>
/// <param name="Strength">The source's strength, which also sets its reach (see AuraSourceComponent).</param>
public sealed record AuraGrant(AuraDefinition Aura, byte Strength);
