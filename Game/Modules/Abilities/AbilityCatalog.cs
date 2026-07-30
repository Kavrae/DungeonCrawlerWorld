namespace Game.Modules.Abilities;

/// <summary>
/// Collects every AbilityDefinition registered during IGameModule.Configure, keyed by Id --
/// mirrors StatusEffectAuraApplierRegistry: every module's Configure call completes before any
/// RegisterSystems runs (see GameBootstrapper.Build's ordering), so by the time
/// AbilityActivationSystem is constructed, every ability registered here -- regardless of
/// Configure call order -- is available. A fresh instance per GameModuleContext (see its own
/// StatusEffectAuraAppliers doc comment for why), so a dry-run mod-validation trial's
/// registrations never leak into the real build's.
/// </summary>
public sealed class AbilityCatalog
{
    private readonly Dictionary<Guid, AbilityDefinition> _definitionsById = [];

    public void Register(AbilityDefinition definition) => _definitionsById[definition.Id] = definition;

    public bool TryGet(Guid abilityId, out AbilityDefinition definition) =>
        _definitionsById.TryGetValue(abilityId, out definition!);
}
