using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Currency;
using Game.Modules.Death;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Lootboxes;
using Game.Modules.Movement.Components;
using Game.Modules.Shops;

namespace Game.Bootstrap;

/// <summary>Every per-session service that changes game state from outside a system, each holding what it needs so a caller passes only ids and values.</summary>
/// <remarks>Built from the built-in pools, which a game always has. The static rules each one delegates to stay the implementation, since systems call them with pools of their own.</remarks>
public sealed class GameCommands(World.World world, GameModuleContext context)
{
    public InventoryCommands InventoryCommands { get; } = new(context.ComponentManager, context.Items, world);

    public CurrencyCommands CurrencyCommands { get; } = new(context.ComponentManager);

    public ShopCommands ShopCommands { get; } = new(context.ComponentManager, context.Items, context.EventBus, world);

    public HotkeyBindingCommands HotkeyBindingCommands { get; } = new(context.ComponentManager, context.Items, context.EventBus);

    public LootboxCommands LootboxCommands { get; } = new(context.LootboxOpener);

    public LootCommands LootCommands { get; } = new(context.ComponentManager);

    /// <summary>The player's one pending command and the only writer of the player's step and activation requests.</summary>
    public PlayerCommands PlayerCommands { get; } = new(
        world,
        context.ComponentManager.GetDirectPool<TransformComponent>(),
        context.ComponentManager.GetPackedPool<MovementComponent>(),
        context.ComponentManager.GetPackedPool<ActionLockComponent>(),
        context.ComponentManager.GetPackedPool<PendingActionActivationComponent>(),
        context.ComponentManager.GetPackedPool<PendingConsumableActivationComponent>(),
        context.ComponentManager.GetPackedPool<PendingDelayedActionComponent>(),
        context.SimulationClock,
        EntityActions.For(context.ComponentManager, context.Actions, context.Definitions),
        context.EventBus);
}
