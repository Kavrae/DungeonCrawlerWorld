using Engine.ECS.Components.Stores;

namespace Engine.ECS.Components;

/// <summary>
/// "Give me this pool if the component is registered, else null" -- the pattern every
/// IGameModule.RegisterSystems uses to accept an optional dependency on another module's
/// component (e.g. Game.Modules.Actions.ActionsModule/Game.Modules.Inventory.InventoryModule both
/// gating on StatModifierComponent/DeadComponent/ManaComponent/AbilityScoreComponent/
/// StatusEffectAuraSourceComponent this way) instead of a hard Dependencies entry. One
/// IsRegistered-then-Get call in one place rather than every RegisterSystems re-writing the same
/// ternary per pool -- generic over T with no game-specific knowledge, so it belongs on
/// ComponentManager itself here in Engine rather than in Game.
/// </summary>
public static class ComponentManagerOptionalPoolExtensions
{
    public static DirectComponentPool<T>? GetOptionalDirectPool<T>(this ComponentManager componentManager) where T : struct =>
        componentManager.IsRegistered<T>() ? componentManager.GetDirectPool<T>() : null;

    public static PackedComponentPool<T>? GetOptionalPackedPool<T>(this ComponentManager componentManager) where T : struct =>
        componentManager.IsRegistered<T>() ? componentManager.GetPackedPool<T>() : null;

    public static MultiComponentPool<T>? GetOptionalMultiPool<T>(this ComponentManager componentManager) where T : struct =>
        componentManager.IsRegistered<T>() ? componentManager.GetMultiPool<T>() : null;
}
