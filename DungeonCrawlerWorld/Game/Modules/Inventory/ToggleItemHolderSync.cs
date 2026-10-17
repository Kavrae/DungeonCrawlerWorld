using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

/// <summary>Keeps each holder's active toggles equal to the lit units it holds: the on/off state belongs to the item, and its effects to whoever holds it.</summary>
/// <remarks>
/// <para>
/// Observes the stack pool rather than being called: whenever a stack is added, changed or about to
/// be removed, the holder's active toggles for that stack are made equal in number to the stack's lit
/// quantity (none for a stack being removed). That covers lighting and putting out a unit, every
/// transfer (the stack leaves one holder and arrives at another), merges, and any future drop or
/// destroy path, with no caller having to remember.
/// </para>
/// <para>
/// Each lit unit is its own toggle, so two lit units in one stack are two toggles. Any holder can
/// hold a lit item -- alive or dead, on the map or not. A stack removed because its holder is being
/// destroyed is ignored: everything the holder had goes with it.
/// </para>
/// </remarks>
internal sealed class ToggleItemHolderSync : IToggleOwner
{
    private readonly MultiComponentPool<InventoryItemStackComponent> _stacks;
    private readonly MultiComponentPool<ActiveToggleComponent> _activeToggles;
    private readonly Toggles _toggles;
    private readonly ItemCatalog _itemCatalog;
    private readonly EntityManager _entityManager;
    private readonly SimulationClock _simulationClock;
    private readonly ComponentManager _componentManager;

    /// <summary>The toggle SwitchOff is ending, so the reconcile that follows its unit being put out takes back that one rather than any of the stack's. 0 the rest of the time.</summary>
    private uint _keyBeingSwitchedOff;

    public ToggleItemHolderSync(
        MultiComponentPool<InventoryItemStackComponent> stacks,
        MultiComponentPool<ActiveToggleComponent> activeToggles,
        Toggles toggles,
        ItemCatalog itemCatalog,
        EntityManager entityManager,
        SimulationClock simulationClock,
        ComponentManager componentManager)
    {
        _stacks = stacks;
        _activeToggles = activeToggles;
        _toggles = toggles;
        _itemCatalog = itemCatalog;
        _entityManager = entityManager;
        _simulationClock = simulationClock;
        _componentManager = componentManager;

        stacks.ComponentChanged += OnStackChanged;
        stacks.ComponentRemoving += OnStackRemoving;
    }

    private void OnStackChanged(int entityId, int denseIndex)
    {
        ref readonly var stack = ref _stacks.GetReadonlyByDenseIndex(denseIndex);
        Reconcile(entityId, in stack, IsLit(in stack) ? stack.Quantity : 0);
    }

    private void OnStackRemoving(int entityId, int denseIndex) =>
        Reconcile(entityId, in _stacks.GetReadonlyByDenseIndex(denseIndex), litQuantity: 0);

    /// <summary>A unit is lit only through its stack's Override (see ToggleItemActivator), so a stack without one is answered with no catalog lookup -- this runs for every stack written in the game.</summary>
    private static bool IsLit(in InventoryItemStackComponent stack) =>
        stack.Override is { Activator: ToggleItemActivator { IsToggledOn: true } };

    private void Reconcile(int entityId, in InventoryItemStackComponent stack, int litQuantity)
    {
        if (litQuantity == 0 && !_activeToggles.Has(entityId))
        {
            return;
        }

        if (_entityManager.IsDestroying(entityId) || !InventoryQueries.TryResolveEffectiveItem(_itemCatalog, in stack, out var item))
        {
            return;
        }

        var owner = ActivatableReference.ItemStack(stack.StackInstanceId);
        var activeCount = _toggles.CountOn(entityId, owner);
        var now = _simulationClock.CurrentFrame;

        for (; activeCount < litQuantity; activeCount++)
        {
            _toggles.TurnOn(entityId, owner, item, now);
        }

        for (; activeCount > litQuantity; activeCount--)
        {
            if (_keyBeingSwitchedOff != 0 && _toggles.TurnOff(entityId, _keyBeingSwitchedOff, item, now))
            {
                _keyBeingSwitchedOff = 0;
                continue;
            }

            if (!_toggles.TryGetKey(entityId, owner, out var key))
            {
                break;
            }

            _toggles.TurnOff(entityId, key, item, now);
        }
    }

    public bool TryResolveDefinition(int holderEntityId, in ActiveToggleComponent toggle, out ActivatableDefinition definition)
    {
        if (InventoryQueries.TryFindByStackInstanceId(_stacks, holderEntityId, toggle.Owner.StackInstanceId, out var stack) &&
            InventoryQueries.TryResolveEffectiveItem(_itemCatalog, in stack, out var item))
        {
            definition = item;
            return true;
        }

        definition = null!;
        return false;
    }

    /// <remarks>Puts one unit of the stack out, with no timing; the stack change that follows is what takes the toggle back (Reconcile).</remarks>
    public void SwitchOff(int holderEntityId, in ActiveToggleComponent toggle, ActivatableDefinition definition, long now)
    {
        _keyBeingSwitchedOff = toggle.Key;
        try
        {
            ToggleItemActions.TryToggle(_componentManager, _itemCatalog, holderEntityId, toggle.Owner.StackInstanceId, out _);
        }
        finally
        {
            _keyBeingSwitchedOff = 0;
        }
    }

    /// <remarks>A lit item with nothing to keep up keeps working on a corpse; one with periodic effects goes out with its holder.</remarks>
    public bool EndsWhenHolderDies(ActivatableDefinition definition) => definition.Toggle?.Periodic is not null;
}
