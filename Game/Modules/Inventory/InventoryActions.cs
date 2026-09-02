using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Inventory.Components;
using Game.World;

namespace Game.Modules.Inventory;

/// <summary>Write-side counterpart to InventoryQueries -- mutates an entity's inventory storage.</summary>
public static class InventoryActions
{
    /// <summary>
    /// Grants quantity of itemDefinitionId, stacking onto an existing matching stack if one
    /// exists rather than always creating a new one -- this is the "identical items grouped with
    /// a count" behavior. The single chokepoint every item grant goes through (starting kits,
    /// future loot drops), so it's also where InventoryGrant.EnsureInventoryComponentExists runs
    /// -- every caller gets the "gains an inventory on first item" behavior for free, the player
    /// included, with no per-call-site handling needed. Returns the StackInstanceId of whichever
    /// stack the granted units ended up in (new or merged-into-existing) -- e.g. so a caller can
    /// immediately bind a hotkey to the exact stack just granted (see ItemHotkeyBindingComponent's
    /// own doc comment for why binding is by StackInstanceId, not ItemDefinitionId).
    /// </summary>
    public static Guid AddItem(ComponentManager componentManager, int entityId, Guid itemDefinitionId, ushort quantity)
    {
        InventoryGrant.EnsureInventoryComponentExists(componentManager, entityId);

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

        var matchedDenseIndex = FindMatchingDenseIndex(stacks, entityId, stack => stack.ItemDefinitionId == itemDefinitionId);
        if (matchedDenseIndex != -1)
        {
            stacks.UpdateByDenseIndex(matchedDenseIndex, quantity, static (ref InventoryItemStackComponent stack, ushort add) => stack.Quantity += add);
            return stacks.GetReadonlyByDenseIndex(matchedDenseIndex).StackInstanceId;
        }

        var newStack = new InventoryItemStackComponent(itemDefinitionId, quantity);
        stacks.Add(entityId, newStack);
        return newStack.StackInstanceId;
    }

    /// <summary>
    /// Ticks the matching stack's Quantity down by 1, removing the stack entirely once it hits
    /// 0 (same "no instance for this item" empty-state convention InventoryItemStackComponent's
    /// own doc comment describes) -- called by ConsumableActivationSystem after every successful
    /// activation. A no-op if the entity doesn't actually have the item (defense-in-depth; the
    /// caller is expected to have already checked).
    /// </summary>
    public static void ConsumeItem(ComponentManager componentManager, int entityId, Guid itemDefinitionId)
    {
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

        if (!InventoryQueries.TryGetStack(stacks, entityId, itemDefinitionId, out var stack))
        {
            return;
        }

        if (stack.Quantity <= 1)
        {
            stacks.RemoveFirst(entityId, itemDefinitionId, static (ref readonly s, id) => s.ItemDefinitionId == id);
            return;
        }

        stacks.TryUpdateFirst(
            entityId,
            itemDefinitionId,
            static (ref readonly s, id) => s.ItemDefinitionId == id,
            static (ref s, id) => s.Quantity--);
    }

    /// <summary>
    /// Structural equality for two divergence Overrides -- ItemDefinition's auto-generated record
    /// equality isn't reliable here, since its Tags/Effects list-typed fields compare by reference,
    /// not content, and two independently-`with`-derived definitions won't reliably share the same
    /// list reference. Used to decide whether a new unit can merge into an existing stack rather
    /// than needing its own.
    /// </summary>
    private static bool AreEquivalentOverrides(ItemDefinition a, ItemDefinition b) =>
        a.Id == b.Id &&
        a.Name == b.Name &&
        a.SpriteName == b.SpriteName &&
        a.Glyph == b.Glyph &&
        a.GlyphColor == b.GlyphColor &&
        a.Description == b.Description &&
        a.Summary == b.Summary &&
        a.MaxStackSize == b.MaxStackSize &&
        Equals(a.Activator, b.Activator) &&
        a.Tags.SequenceEqual(b.Tags) &&
        a.Effects.SequenceEqual(b.Effects);

    /// <summary>
    /// Manual dense-index walk over entityId's own chain, stopping at the first component matching
    /// predicate -- the same "no id-indexed direct lookup, so scan by hand" shape
    /// AbilityScoreEffects.SetBaseValue and InventoryQueries.TryFindByStackInstanceId both already
    /// use. Returns -1 if nothing matches. Needed (rather than TryGetFirst/TryUpdateFirst) wherever
    /// the *dense index itself* -- not just the matched value -- is what a caller needs, to mutate
    /// via UpdateByDenseIndex or read fields (like StackInstanceId) off the match afterward.
    /// </summary>
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

    /// <summary>
    /// Grants quantity units carrying a shared Override -- IsDivergent stays false, since every
    /// unit granted together this way is still identical to every other (e.g. a freshly-granted
    /// batch of wands, all at the same Intelligence-derived MaxCharges baked in at grant time).
    /// Merges into an existing plain (IsDivergent == false) stack of the same ItemDefinitionId if
    /// one has an equivalent Override, respecting effectiveDefinition.MaxStackSize -- any quantity
    /// that would overflow the cap spills into additional new stacks rather than growing one stack
    /// past it. No ItemCatalog lookup needed -- effectiveDefinition already carries its own
    /// MaxStackSize, unlike plain AddItem above (which only ever receives a bare Guid).
    /// </summary>
    public static void AddItemWithOverride(ComponentManager componentManager, int entityId, ItemDefinition effectiveDefinition, ushort quantity)
    {
        InventoryGrant.EnsureInventoryComponentExists(componentManager, entityId);
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        var maxStackSize = effectiveDefinition.MaxStackSize;
        var remaining = quantity;

        var matchedDenseIndex = FindMatchingDenseIndex(stacks, entityId,
            stack => !stack.IsDivergent && stack.Override is { } existing && AreEquivalentOverrides(existing, effectiveDefinition));

        if (matchedDenseIndex != -1)
        {
            var existingQuantity = stacks.GetReadonlyByDenseIndex(matchedDenseIndex).Quantity;
            var room = maxStackSize is { } cap ? (ushort)System.Math.Max(0, cap - existingQuantity) : remaining;
            var addNow = (ushort)System.Math.Min(remaining, room);
            if (addNow > 0)
            {
                stacks.UpdateByDenseIndex(matchedDenseIndex, addNow, static (ref InventoryItemStackComponent stack, ushort add) => stack.Quantity += add);
                remaining -= addNow;
            }
        }

        while (remaining > 0)
        {
            var chunk = maxStackSize is { } cap ? (ushort)System.Math.Min(remaining, cap) : remaining;
            stacks.Add(entityId, new InventoryItemStackComponent(effectiveDefinition.Id, chunk, overrideDefinition: effectiveDefinition, isDivergent: false));
            remaining -= chunk;
        }
    }

    /// <summary>
    /// The generic divergence primitive -- reusable by anything that makes an item genuinely differ
    /// from its ItemDefinition (a wand's remaining charges today, a future enchant permanently
    /// modifying stats). Adds one unit with Override = overrideDefinition, IsDivergent: true,
    /// merging (Quantity++) into an existing divergent stack of the same ItemDefinitionId with an
    /// equivalent Override if one exists and has room under overrideDefinition.MaxStackSize, else
    /// creating a new Quantity: 1 stack. Returns the StackInstanceId of whichever stack the unit
    /// ended up in (new or merged-into-existing) -- callers (e.g. a wand repointing its hotkey
    /// binding after firing) need this to know exactly which physical stack now holds it.
    /// </summary>
    public static Guid AddDivergentItem(ComponentManager componentManager, int entityId, ItemDefinition overrideDefinition)
    {
        InventoryGrant.EnsureInventoryComponentExists(componentManager, entityId);
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        var maxStackSize = overrideDefinition.MaxStackSize;

        var matchedDenseIndex = FindMatchingDenseIndex(stacks, entityId,
            stack => stack.IsDivergent && stack.Override is { } existing && AreEquivalentOverrides(existing, overrideDefinition) &&
                     (maxStackSize is not { } cap || stack.Quantity < cap));

        if (matchedDenseIndex != -1)
        {
            stacks.UpdateByDenseIndex(matchedDenseIndex, static (ref InventoryItemStackComponent stack) => stack.Quantity++);
            return stacks.GetReadonlyByDenseIndex(matchedDenseIndex).StackInstanceId;
        }

        var newStack = new InventoryItemStackComponent(overrideDefinition.Id, quantity: 1, overrideDefinition: overrideDefinition, isDivergent: true);
        stacks.Add(entityId, newStack);
        return newStack.StackInstanceId;
    }

    /// <summary>
    /// Decrements the *exact* source stack (found via StackInstanceId, not an item-id search --
    /// works whether the source was plain or already divergent) by 1 Quantity, removing it entirely
    /// at 0 (same convention ConsumeItem below already follows for the non-diverging case), then
    /// adds/merges newOverrideDefinition as a divergent unit via AddDivergentItem above. What a
    /// wand's every single shot calls, uniformly, whether it's the first shot off a fresh batch or
    /// the Nth shot depleting an already-divergent instance -- see ConsumableActivationSystem.
    /// </summary>
    public static Guid PeelOneIntoDivergentStack(ComponentManager componentManager, int entityId, Guid sourceStackInstanceId, ItemDefinition newOverrideDefinition)
    {
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

        var sourceDenseIndex = FindMatchingDenseIndex(stacks, entityId, stack => stack.StackInstanceId == sourceStackInstanceId);
        if (sourceDenseIndex != -1)
        {
            var sourceQuantity = stacks.GetReadonlyByDenseIndex(sourceDenseIndex).Quantity;
            if (sourceQuantity <= 1)
            {
                stacks.RemoveByDenseIndex(sourceDenseIndex);
            }
            else
            {
                stacks.UpdateByDenseIndex(sourceDenseIndex, static (ref InventoryItemStackComponent stack) => stack.Quantity--);
            }
        }

        return AddDivergentItem(componentManager, entityId, newOverrideDefinition);
    }

    /// <summary>
    /// Ticks the exact matched stack's Quantity down by 1, removing it entirely at 0 -- the
    /// StackInstanceId-keyed counterpart to ConsumeItem below, used by activation now that item
    /// hotkey binding (and every activation request) targets one specific stack rather than an
    /// item id. A no-op if stackInstanceId no longer resolves to anything (defense-in-depth; the
    /// caller is expected to have already checked).
    /// </summary>
    public static void ConsumeItemByStackInstanceId(ComponentManager componentManager, int entityId, Guid stackInstanceId)
    {
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

        var denseIndex = FindMatchingDenseIndex(stacks, entityId, stack => stack.StackInstanceId == stackInstanceId);
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

    /// <summary>Disables/enables an entity's whole inventory -- items still exist and can still be granted while disabled, but the management window can't be opened (see InventoryFolderController).</summary>
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
    /// destination is a non-player entity already at its stack cap (see InventoryCapacity).
    ///
    /// FirstAcquiredUtcTicks is the one field NOT preserved verbatim: when destinationEntityId is
    /// the player (e.g. "Take" from a corpse/loot window), it's re-stamped to now -- since this
    /// method never merges, every transfer onto the player is by definition a new stack there, and
    /// looting something should read as freshly acquired regardless of how long it sat wherever it
    /// came from. A transfer to any other entity (e.g. "Give" from the player, or between two
    /// non-player entities) leaves it untouched.
    /// </summary>
    public static bool TryTransferStack(ComponentManager componentManager, int sourceEntityId, int destinationEntityId, Guid stackInstanceId, IPlayerQuery? playerQuery)
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
        stacks.RemoveByDenseIndex(sourceDenseIndex);

        if (destinationEntityId == playerQuery?.PlayerEntityId)
        {
            snapshot.FirstAcquiredUtcTicks = DateTime.UtcNow.Ticks;
        }

        InventoryGrant.EnsureInventoryComponentExists(componentManager, destinationEntityId);
        stacks.Add(destinationEntityId, snapshot);
        return true;
    }

    /// <summary>
    /// The "Merged Stack" drag case: moves every stack sharing itemDefinitionId on sourceEntityId
    /// to destinationEntityId in one go, each keeping its own identity (see TryTransferStack
    /// above) -- all or nothing, refusing the whole batch (no state changed) if the destination
    /// doesn't have room for every one of them, rather than transferring some and leaving the rest
    /// behind.
    /// </summary>
    public static bool TryTransferAllStacksOfItem(ComponentManager componentManager, int sourceEntityId, int destinationEntityId, Guid itemDefinitionId, IPlayerQuery? playerQuery)
    {
        if (sourceEntityId == destinationEntityId)
        {
            return false;
        }

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

        var matches = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(stacks, sourceEntityId, matches);
        matches.RemoveAll(stack => stack.ItemDefinitionId != itemDefinitionId);

        if (matches.Count == 0 || !InventoryCapacity.HasRoomForNewStacks(componentManager, destinationEntityId, playerQuery, matches.Count))
        {
            return false;
        }

        foreach (var stack in matches)
        {
            TryTransferStack(componentManager, sourceEntityId, destinationEntityId, stack.StackInstanceId, playerQuery);
        }

        return true;
    }
}
