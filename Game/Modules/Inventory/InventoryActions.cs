using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Inventory.Components;
using Game.World;

namespace Game.Modules.Inventory;

/// <summary>Write-side counterpart to InventoryQueries -- mutates an entity's inventory storage.</summary>
public static class InventoryActions
{
    /// <summary>The per-item-stack cap every entity starts with -- see MaxStackSizeComponent's own doc comment for the per-entity override that replaces this for whichever entity has one.</summary>
    public const ushort DefaultMaxStackSize = 999;

    /// <summary>Grants quantity plain units of itemDefinitionId: no Override, not divergent, not disabled.</summary>
    /// <remarks>
    /// A stack with an Override is a different item that shares the id, so only plain stacks are joined. Returns the
    /// StackInstanceId the last unit landed in, so a caller can bind a hotkey to exactly what it granted (see
    /// ItemHotkeyBindingComponent for why binding is by stack). See AddUnits for how units are placed.
    /// </remarks>
    public static uint AddItem(ComponentManager componentManager, int entityId, Guid itemDefinitionId, ushort quantity) =>
        AddUnits(componentManager, entityId, new StackTemplate(itemDefinitionId, overrideDefinition: null, isDivergent: false, isDisabled: false), quantity);

    /// <summary>Adds quantity units shaped by template to entityId, filling every interchangeable stack with room before starting new ones.</summary>
    /// <remarks>
    /// Every item grant ends here, so it is also where InventoryGrant.EnsureInventoryComponentExists runs: whoever is
    /// given an item gains an inventory. No stack grows past entityId's effective cap (GetEffectiveMaxStackSize); what
    /// doesn't fit starts new stacks. A joined stack keeps its StackInstanceId and AcquiredSequence. Returns the
    /// StackInstanceId the last unit landed in, or 0 when quantity is 0.
    /// </remarks>
    private static uint AddUnits(ComponentManager componentManager, int entityId, StackTemplate template, ushort quantity)
    {
        InventoryGrant.EnsureInventoryComponentExists(componentManager, entityId);

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        var effectiveCap = GetEffectiveMaxStackSize(componentManager, entityId);
        var remaining = quantity;
        var lastStackInstanceId = 0u;

        while (remaining > 0)
        {
            var joinedDenseIndex = FindMatchingDenseIndex(stacks, entityId, (Template: template, Cap: effectiveCap),
                static (stack, state) => stack.Quantity < state.Cap && state.Template.Matches(in stack));
            if (joinedDenseIndex != -1)
            {
                var addNow = (ushort)System.Math.Min(remaining, effectiveCap - stacks.GetReadonlyByDenseIndex(joinedDenseIndex).Quantity);
                stacks.UpdateByDenseIndex(joinedDenseIndex, addNow, static (ref InventoryItemStackComponent stack, ushort add) => stack.Quantity += add);
                lastStackInstanceId = stacks.GetReadonlyByDenseIndex(joinedDenseIndex).StackInstanceId;
                remaining -= addNow;
                continue;
            }

            var chunk = (ushort)System.Math.Min(remaining, effectiveCap);
            var newStack = template.CreateStack(chunk);
            stacks.Add(entityId, newStack);
            lastStackInstanceId = newStack.StackInstanceId;
            remaining -= chunk;
        }

        return lastStackInstanceId;
    }

    /// <summary>entityId's own MaxStackSizeComponent if it has one (today only ever the player, post-ObsessiveCollectorAchievement), else DefaultMaxStackSize -- every stack-growing method below reads this instead of any per-item cap, so the same entity's cap applies uniformly to every item it holds.</summary>
    public static ushort GetEffectiveMaxStackSize(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<MaxStackSizeComponent>().TryGetReadonly(entityId, out var overrideComponent) ? overrideComponent.Value : DefaultMaxStackSize;

    /// <summary>
    /// Structural equality for two divergence Overrides -- ItemDefinition's auto-generated record
    /// equality isn't reliable here, since its Effects list-typed field compares by reference,
    /// not content, and two independently-`with`-derived definitions won't reliably share the same
    /// list reference. Used to decide whether a new unit can merge into an existing stack rather
    /// than needing its own.
    /// </summary>
    internal static bool AreEquivalentOverrides(ItemDefinition a, ItemDefinition b) =>
        a.Id == b.Id &&
        a.Name == b.Name &&
        a.SpriteName == b.SpriteName &&
        a.Glyph == b.Glyph &&
        a.GlyphColor == b.GlyphColor &&
        a.SpriteTint == b.SpriteTint &&
        a.Description == b.Description &&
        a.Summary == b.Summary &&
        Equals(a.Activator, b.Activator) &&
        Equals(a.Contents, b.Contents) &&
        Equals(a.Toggle, b.Toggle) &&
        a.CanTrade == b.CanTrade &&
        a.Tags == b.Tags &&
        a.Effects.SequenceEqual(b.Effects);

    /// <summary>What a unit is, apart from which stack holds it: the stacks it can join, and the stack it starts when none has room.</summary>
    private readonly struct StackTemplate(Guid itemDefinitionId, ItemDefinition? overrideDefinition, bool isDivergent, bool isDisabled)
    {
        public Guid ItemDefinitionId { get; } = itemDefinitionId;

        public ItemDefinition? OverrideDefinition { get; } = overrideDefinition;

        public bool IsDivergent { get; } = isDivergent;

        public bool IsDisabled { get; } = isDisabled;

        public static StackTemplate Of(in InventoryItemStackComponent stack) =>
            new(stack.ItemDefinitionId, stack.Override, stack.IsDivergent, stack.IsDisabled);

        /// <summary>Whether stack holds units interchangeable with this template's: the same item, the same Override (or neither has one), and the same divergence and disabled state.</summary>
        public bool Matches(in InventoryItemStackComponent stack) =>
            stack.ItemDefinitionId == ItemDefinitionId &&
            stack.IsDivergent == IsDivergent &&
            stack.IsDisabled == IsDisabled &&
            (stack.Override, OverrideDefinition) switch
            {
                (null, null) => true,
                ({ } stackOverride, { } templateOverride) => AreEquivalentOverrides(stackOverride, templateOverride),
                _ => false,
            };

        public InventoryItemStackComponent CreateStack(ushort quantity) =>
            new(ItemDefinitionId, quantity, IsDisabled, OverrideDefinition, IsDivergent);
    }

    /// <summary>Moves stack stackInstanceId's units into another stack entityId already holds with interchangeable units, as many as that stack's cap allows, removing stackInstanceId once it's empty.</summary>
    /// <remarks>
    /// The stack merged into keeps its identity -- StackInstanceId and AcquiredSequence -- so a hotkey bound to it stays
    /// bound. Whatever doesn't fit stays in stackInstanceId's own stack. A no-op when entityId doesn't hold
    /// stackInstanceId or holds nothing interchangeable with room. Returns the StackInstanceId now holding the last of
    /// the moved units: the stack merged into, or stackInstanceId itself when anything was left behind.
    /// </remarks>
    public static uint MergeIntoEquivalentStack(ComponentManager componentManager, int entityId, uint stackInstanceId)
    {
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        var sourceDenseIndex = FindMatchingDenseIndex(stacks, entityId, stackInstanceId, static (stack, id) => stack.StackInstanceId == id);
        if (sourceDenseIndex == -1)
        {
            return stackInstanceId;
        }

        var source = stacks.GetReadonlyByDenseIndex(sourceDenseIndex);
        var effectiveCap = GetEffectiveMaxStackSize(componentManager, entityId);
        var targetDenseIndex = FindMatchingDenseIndex(stacks, entityId, (SourceStackInstanceId: source.StackInstanceId, Template: StackTemplate.Of(in source), Cap: effectiveCap),
            static (stack, state) => stack.StackInstanceId != state.SourceStackInstanceId && stack.Quantity < state.Cap && state.Template.Matches(in stack));
        if (targetDenseIndex == -1)
        {
            return stackInstanceId;
        }

        var target = stacks.GetReadonlyByDenseIndex(targetDenseIndex);
        var moved = (ushort)System.Math.Min(source.Quantity, effectiveCap - target.Quantity);
        stacks.UpdateByDenseIndex(targetDenseIndex, moved, static (ref InventoryItemStackComponent stack, ushort add) => stack.Quantity += add);

        if (moved == source.Quantity)
        {
            stacks.RemoveByDenseIndex(sourceDenseIndex);
            return target.StackInstanceId;
        }

        stacks.UpdateByDenseIndex(sourceDenseIndex, moved, static (ref InventoryItemStackComponent stack, ushort taken) => stack.Quantity -= taken);
        return stackInstanceId;
    }

    /// <summary>
    /// Manual dense-index walk over entityId's own chain, stopping at the first component matching
    /// predicate -- the same "no id-indexed direct lookup, so scan by hand" shape
    /// AbilityScoreEffects.SetBaseValue and InventoryQueries.TryFindByStackInstanceId both already
    /// use. Returns -1 if nothing matches. Needed (rather than TryGetFirst/TryUpdateFirst) wherever
    /// the *dense index itself* -- not just the matched value -- is what a caller needs, to mutate
    /// via UpdateByDenseIndex or read fields (like StackInstanceId) off the match afterward.
    /// </summary>
    /// <summary>FindMatchingDenseIndex with the predicate's state passed in rather than captured, so a hot caller allocates no closure per call.</summary>
    private static int FindMatchingDenseIndex<TState>(MultiComponentPool<InventoryItemStackComponent> stacks, int entityId, TState state, Func<InventoryItemStackComponent, TState, bool> predicate)
    {
        for (var denseIndex = stacks.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = stacks.GetNextDenseIndex(denseIndex))
        {
            if (predicate(stacks.GetReadonlyByDenseIndex(denseIndex), state))
            {
                return denseIndex;
            }
        }

        return -1;
    }

    private static int FindMatchingDenseIndex(MultiComponentPool<InventoryItemStackComponent> stacks, int entityId, Func<InventoryItemStackComponent, bool> predicate)
    {
        for (var denseIndex = stacks.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = stacks.GetNextDenseIndex(denseIndex))
        {
            if (predicate(stacks.GetReadonlyByDenseIndex(denseIndex)))
            {
                return denseIndex;
            }
        }

        return -1;
    }

    /// <summary>Grants quantity units sharing effectiveDefinition as their Override, not divergent and not disabled.</summary>
    /// <remarks>
    /// Units granted together this way are still identical to one another (a batch of wands with the same
    /// Intelligence-derived MaxCharges), so they are not divergent. Returns the StackInstanceId the last unit landed in.
    /// </remarks>
    public static uint AddItemWithOverride(ComponentManager componentManager, int entityId, ItemDefinition effectiveDefinition, ushort quantity) =>
        AddUnits(componentManager, entityId, new StackTemplate(effectiveDefinition.Id, effectiveDefinition, isDivergent: false, isDisabled: false), quantity);

    /// <summary>Adds one unit that differs from its catalog item, with overrideDefinition as its Override, divergent and not disabled.</summary>
    /// <remarks>
    /// For anything that makes an item genuinely differ from its definition (a wand's remaining charges). Returns the
    /// StackInstanceId the unit landed in.
    /// </remarks>
    public static uint AddDivergentItem(ComponentManager componentManager, int entityId, ItemDefinition overrideDefinition) =>
        AddUnits(componentManager, entityId, new StackTemplate(overrideDefinition.Id, overrideDefinition, isDivergent: true, isDisabled: false), quantity: 1);

    /// <summary>Changes one unit of entityId's stack stackInstanceId into newOverrideDefinition, moving it to the stack it now belongs in.</summary>
    /// <remarks>
    /// A null newOverrideDefinition returns the unit to a plain stack of its item; any other makes it a divergent unit
    /// with that Override. The unit keeps its stack's disabled state. It joins an interchangeable stack with room when
    /// there is one -- which may be the stack it left, if nothing about it changed -- and starts its own otherwise.
    /// Returns the StackInstanceId the unit landed in, or 0, changing nothing, when entityId doesn't hold the stack.
    /// </remarks>
    public static uint MoveOneUnit(ComponentManager componentManager, int entityId, uint stackInstanceId, ItemDefinition? newOverrideDefinition)
    {
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        var sourceDenseIndex = FindMatchingDenseIndex(stacks, entityId, stackInstanceId, static (stack, id) => stack.StackInstanceId == id);
        if (sourceDenseIndex == -1)
        {
            return 0;
        }

        var source = stacks.GetReadonlyByDenseIndex(sourceDenseIndex);
        var template = newOverrideDefinition is null
            ? new StackTemplate(source.ItemDefinitionId, overrideDefinition: null, isDivergent: false, source.IsDisabled)
            : new StackTemplate(newOverrideDefinition.Id, newOverrideDefinition, isDivergent: true, source.IsDisabled);

        RemoveOneUnit(componentManager, entityId, stackInstanceId);
        return AddUnits(componentManager, entityId, template, quantity: 1);
    }

    /// <summary>Takes one unit out of entityId's stack stackInstanceId, removing the stack once it is empty.</summary>
    /// <remarks>A no-op when the entity doesn't hold the stack.</remarks>
    public static void RemoveOneUnit(ComponentManager componentManager, int entityId, uint stackInstanceId)
    {
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

        var denseIndex = FindMatchingDenseIndex(stacks, entityId, stackInstanceId, static (stack, id) => stack.StackInstanceId == id);
        if (denseIndex == -1)
        {
            return;
        }

        if (stacks.GetReadonlyByDenseIndex(denseIndex).Quantity <= 1)
        {
            stacks.RemoveByDenseIndex(denseIndex);
            return;
        }

        stacks.UpdateByDenseIndex(denseIndex, static (ref InventoryItemStackComponent stack) => stack.Quantity--);
    }

    /// <summary>Disables/enables one specific stack (e.g. an item withheld until some later trigger) -- distinct from SetInventoryDisabled below, which disables the whole inventory.</summary>
    public static void SetStackDisabled(ComponentManager componentManager, int entityId, Guid itemDefinitionId, bool disabled)
    {
        componentManager.GetMultiPool<InventoryItemStackComponent>().TryUpdateFirst(
            entityId,
            (itemDefinitionId, disabled),
            static (ref readonly stack, state) => stack.ItemDefinitionId == state.itemDefinitionId,
            static (ref stack, state) => stack.IsDisabled = state.disabled);
    }

    /// <summary>Disables/enables an entity's whole inventory -- items still exist and can still be granted while disabled, but the management window can't be opened (see InventoryWindowController).</summary>
    public static void SetInventoryDisabled(ComponentManager componentManager, int entityId, bool disabled) =>
        componentManager.Merge(entityId, new InventoryDisabledComponent(disabled));

    /// <summary>
    /// Moves one exact stack from sourceEntityId to destinationEntityId, preserving its exact
    /// identity (StackInstanceId, Override, IsDisabled, IsDivergent) -- never merges into an
    /// existing stack on the destination, even one matching the same item id (stack splitting/
    /// merging is a separate, not-yet-built TODO item; duplicate stacks of the same item on one
    /// entity are accepted for now). Refuses (returns false, no state changed) if source and
    /// destination are the same entity -- a drop back onto the grid it came from should never
    /// remove-then-re-add a stack it's already looking at -- or if the stack isn't found, or if the
    /// destination is a non-player entity already at its stack cap (see InventoryCapacity), or if the
    /// stack's item can't be traded (ItemDefinition.CanTrade -- a loot box never leaves its owner).
    ///
    /// AcquiredSequence is the one field NOT preserved verbatim: when destinationEntityId is
    /// the player (e.g. "Take" from a corpse/loot window), it's re-stamped to now -- since this
    /// method never merges, every transfer onto the player is by definition a new stack there, and
    /// looting something should read as freshly acquired regardless of how long it sat wherever it
    /// came from. A transfer to any other entity (e.g. "Give" from the player, or between two
    /// non-player entities) leaves it untouched.
    /// </summary>
    public static bool TryTransferStack(ComponentManager componentManager, ItemCatalog itemCatalog, int sourceEntityId, int destinationEntityId, uint stackInstanceId, IPlayerQuery playerQuery)
    {
        if (sourceEntityId == destinationEntityId)
        {
            return false;
        }

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

        var sourceDenseIndex = FindMatchingDenseIndex(stacks, sourceEntityId, stack => stack.StackInstanceId == stackInstanceId);
        if (sourceDenseIndex == -1 || !InventoryCapacity.HasRoomForNewStack(componentManager, destinationEntityId, playerQuery))
        {
            return false;
        }

        var snapshot = stacks.GetReadonlyByDenseIndex(sourceDenseIndex);
        if (!CanTrade(itemCatalog, in snapshot))
        {
            return false;
        }

        stacks.RemoveByDenseIndex(sourceDenseIndex);

        if (destinationEntityId == playerQuery.PlayerEntityId)
        {
            snapshot.AcquiredSequence = InventoryItemStackComponent.NextAcquiredSequence();
        }

        InventoryGrant.EnsureInventoryComponentExists(componentManager, destinationEntityId);
        stacks.Add(destinationEntityId, snapshot);
        return true;
    }

    /// <summary>
    /// The "Merged Stack" drag case: moves every stack sharing itemDefinitionId on sourceEntityId
    /// to destinationEntityId in one go, each keeping its own identity (see TryTransferStack
    /// above) -- all or nothing, refusing the whole batch (no state changed) if the destination
    /// doesn't have room for every one of them, or if any of them can't be traded, rather than
    /// transferring some and leaving the rest behind.
    /// </summary>
    public static bool TryTransferAllStacksOfItem(ComponentManager componentManager, ItemCatalog itemCatalog, int sourceEntityId, int destinationEntityId, Guid itemDefinitionId, IPlayerQuery playerQuery)
    {
        if (sourceEntityId == destinationEntityId)
        {
            return false;
        }

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

        var matches = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(stacks, sourceEntityId, matches);
        matches.RemoveAll(stack => stack.ItemDefinitionId != itemDefinitionId);

        if (matches.Count == 0 || !InventoryCapacity.HasRoomForNewStacks(componentManager, destinationEntityId, playerQuery, matches.Count) || !matches.TrueForAll(stack => CanTrade(itemCatalog, in stack)))
        {
            return false;
        }

        foreach (var stack in matches)
        {
            TryTransferStack(componentManager, itemCatalog, sourceEntityId, destinationEntityId, stack.StackInstanceId, playerQuery);
        }

        return true;
    }

    /// <summary>Whether stack's item may leave its owner's inventory; an item the catalog doesn't know carries no restriction.</summary>
    private static bool CanTrade(ItemCatalog itemCatalog, in InventoryItemStackComponent stack) =>
        !InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var definition) || definition.CanTrade;
}
