namespace Game.Modules.Actions;

/// <summary>
/// Collects every ActionDefinition registered during IGameModule.Configure, keyed by Id
/// </summary>
/// <remarks>
/// Mirrors StatusEffectAuraApplierRegistry: every module's Configure call completes before any
/// RegisterSystems runs (see GameBootstrapper.Build's ordering), so by the time
/// ActionActivationSystem is constructed, every action registered here -- regardless of
/// Configure call order -- is available. A fresh instance per GameModuleContext (see its own
/// StatusEffectAuraAppliers doc comment for why), so a dry-run mod-validation trial's
/// registrations never leak into the real build's.
/// </remarks>
public sealed class ActionCatalog
{
    private readonly Dictionary<Guid, ActionDefinition> _definitionsById = [];

    public void Register(ActionDefinition definition) => _definitionsById[definition.Id] = definition;

    public bool TryGet(Guid actionId, out ActionDefinition definition) =>
        _definitionsById.TryGetValue(actionId, out definition!);
}
