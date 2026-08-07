using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using FontStashSharp;
using Game.Blueprints;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Input;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// Renders one slot per HotkeySlot value (all 10, not just bound ones), laid out in the QE /
/// RFV / 12345 visual clusters from HotkeySlotLayout. An unbound slot (or a bound-but-not-
/// registered-in-either-catalog one, defensively) is an empty dark-gray square -- no glyph,
/// background, or radial fill -- reserved for a future binding rather than simply omitted. A
/// bound slot reuses RadialFillRenderer exactly as ActionLockContent already does, with
/// FillPercentage taking the greater of the shared ActionLock's fraction (Immediate/Delayed
/// abilities and every consumable activation, all of which set it -- FreeCast abilities bypass
/// the shared lock entirely) and, for an ability specifically, the granted instance's own
/// cooldown fraction (any category, if AbilityTiming.CooldownFrames is set) -- an ability with
/// neither simply never shows a mask, since RadialFillRenderer already no-ops at FillPercentage
/// &lt;= 0. A bound item slot draws its sprite/glyph and inventory quantity the same way
/// InventoryItemStackCell's grid cells do (see ItemIconRenderer), greyed out (no radial mask
/// either) if it has no ConsumableEffect at all (an Equipment/Tool item with no activated
/// ability). A Potion slot with an active PotionCooldownComponent additionally shows the
/// remaining seconds as plain green text above the slot -- informational only, not a gate (see
/// PotionCooldownEffects' own doc comment: the cooldown never blocks a second potion, so this
/// deliberately isn't what drives the slot's own radial mask). The currently-armed slot
/// (MapViewState.ArmedSlot) gets a distinct outline drawn first, with the slot's normal content
/// inset within it. Implements TODO.md's "Inventory and spell hotbar" and "Player attack button
/// or key" items.
/// </summary>
public sealed class HotbarContent(
    World world,
    MapViewState mapViewState,
    ComponentManager componentManager,
    AbilityCatalog abilityCatalog,
    ItemCatalog itemCatalog,
    FontService fontService,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer) : IElementContent
{
    public static readonly Vector2 SlotSize = new(HudMetrics.EntrySize.Y * 2.25f, HudMetrics.EntrySize.Y * 2.25f);

    private const float GlyphSizeFraction = 0.75f;
    private const float QuantityFontFraction = 0.3f;
    private const float CountdownFontFraction = 0.35f;
    private const int ContentInset = 2;
    private const int ArmedOutlineThickness = 3;
    private const float SlotGap = 1f;
    private const float GroupGap = 10f;
    private const float CountdownTextGap = 2f;

    private static readonly Color UnboundSlotColor = new(48, 48, 48);
    private static readonly Color BoundSlotBackgroundColor = Color.WhiteSmoke;
    private static readonly Color BoundSlotGlyphColor = Color.Black;
    private static readonly Color DisabledItemTintColor = Color.Gray;
    private static readonly Color ArmedSlotOutlineColor = Color.White;
    private static readonly Color ArmedSlotMaskColor = Color.White * 0.5f;

    public static readonly Vector2 Size = ComputeTotalSize();

    /// <summary>Width for the Armed Hotkey Summary window: 3 slots plus the 2 inter-slot gaps
    /// between them (see ArmedHotkeySummaryWindow, which centers this over whichever single slot
    /// it's currently showing).</summary>
    internal const int SummarySlotSpan = 3;
    internal static readonly float SummaryWidth = SummarySlotSpan * SlotSize.X + (SummarySlotSpan - 1) * SlotGap;

    private static readonly Color DragDropTargetGlowColor = Color.Gold;

    /// <summary>
    /// Set by GameInputController while a content-drag (an inventory item cell, or an already-
    /// bound hotbar slot, picked up toward a new binding -- see its own doc comment) is in
    /// progress, so every slot glows to invite the drop -- any slot can accept any item, so
    /// there's no single "hovered" target the way a more typical drop zone would highlight.
    /// Cleared the moment the drag ends, drop accepted or not. Reuses GlowRenderer.Draw verbatim,
    /// the same primitive NotificationCenter's folder already uses for its own unread-glow --
    /// this is the pattern the later Equipment menu's own drop targets should follow too.
    /// </summary>
    public bool IsAcceptingDrag { get; set; }

    private readonly MultiComponentPool<ActionHotkeyBindingComponent> _actionHotkeyBindings = componentManager.GetMultiPool<ActionHotkeyBindingComponent>();
    private readonly MultiComponentPool<ItemHotkeyBindingComponent> _itemHotkeyBindings = componentManager.GetMultiPool<ItemHotkeyBindingComponent>();
    private readonly MultiComponentPool<AbilityInstanceComponent> _abilityInstances = componentManager.GetMultiPool<AbilityInstanceComponent>();
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks = componentManager.GetPackedPool<ActionLockComponent>();
    private readonly PackedComponentPool<PotionCooldownComponent> _potionCooldowns = componentManager.GetPackedPool<PotionCooldownComponent>();
    private readonly PackedComponentPool<ManaComponent> _mana = componentManager.GetPackedPool<ManaComponent>();
    private readonly RadialFillRenderer _radialFill = new(new GlyphRenderer(), spriteSheetService, spriteRenderer);

    private Window _hostWindow = null!;
    private SpriteFontBase _font = null!;
    private SpriteFontBase _quantityFont = null!;
    private SpriteFontBase _countdownFont = null!;

    private static Vector2 ComputeTotalSize()
    {
        var totalWidth = 0f;

        foreach (var group in HotkeySlotLayout.VisualGroups)
        {
            totalWidth += group.Count * SlotSize.X + (group.Count - 1) * SlotGap;
        }

        totalWidth += (HotkeySlotLayout.VisualGroups.Count - 1) * GroupGap;

        return new Vector2(totalWidth, SlotSize.Y);
    }

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont((int)(SlotSize.Y * GlyphSizeFraction));
        _quantityFont = fontService.GetFont((int)(SlotSize.Y * QuantityFontFraction));
        _countdownFont = fontService.GetFont((int)(SlotSize.Y * CountdownFontFraction));
    }

    public void Update(GameTime gameTime) { }

    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        var playerEntityId = world.PlayerEntityId;

        foreach (var (slot, bounds) in EnumerateSlotBounds())
        {
            DrawSlot(spriteBatch, unitRectangle, playerEntityId, slot, new Vector2(bounds.X, bounds.Y));

            if (IsAcceptingDrag)
            {
                GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, DragDropTargetGlowColor);
            }
        }
    }

    /// <summary>
    /// Every slot's bounds, in the same left-to-right, group-by-group order DrawContent walks --
    /// the shared layout math behind drawing, the drag-drop glow above, and TryGetSlotAt below,
    /// so all three can never drift out of sync with each other the way three independent copies
    /// of this walk risked.
    /// </summary>
    private IEnumerable<(HotkeySlot Slot, Rectangle Bounds)> EnumerateSlotBounds()
    {
        var origin = _hostWindow.ContentAbsolutePosition;
        var x = origin.X;

        foreach (var group in HotkeySlotLayout.VisualGroups)
        {
            foreach (var slot in group)
            {
                yield return (slot, new Rectangle((int)x, (int)origin.Y, (int)SlotSize.X, (int)SlotSize.Y));
                x += SlotSize.X + SlotGap;
            }

            // The trailing intra-group gap just added becomes the wider group gap instead.
            x += GroupGap - SlotGap;
        }
    }

    /// <summary>Screen-position hit test for exactly which hotbar slot (if any) screenPosition falls within -- GameInputController's content-drag path uses this both as a bind-drop target and, combined with TryGetBoundItemId, to detect a drag starting on an already-bound slot.</summary>
    internal bool TryGetSlotAt(Point screenPosition, out HotkeySlot slot)
    {
        foreach (var (candidateSlot, bounds) in EnumerateSlotBounds())
        {
            if (bounds.Contains(screenPosition))
            {
                slot = candidateSlot;
                return true;
            }
        }

        slot = default;
        return false;
    }

    /// <summary>The item (if any) currently bound to slot -- GameInputController's content-drag path reads this at press time to capture the payload of a drag starting on an already-bound hotbar slot.</summary>
    internal bool TryGetBoundItemId(HotkeySlot slot, out Guid itemDefinitionId) =>
        ItemHotkeyBindingQueries.TryGet(_itemHotkeyBindings, world.PlayerEntityId, slot, out itemDefinitionId);

    /// <summary>slot's bound ability/item resolved to a title+summary pair, for the Armed Hotkey
    /// Summary window -- false if the slot has no binding. Summary, not Description: a short,
    /// concrete statement of exact effect meant to be read at a glance in this small window (see
    /// AbilityDefinition/ItemDefinition's own doc comments on the Summary vs Description split) --
    /// Description is reserved for future, larger text boxes elsewhere.</summary>
    internal bool TryGetSlotSummary(HotkeySlot slot, out string title, out string summary)
    {
        var playerEntityId = world.PlayerEntityId;

        if (ActionHotkeyBindingQueries.TryGet(_actionHotkeyBindings, playerEntityId, slot, out var abilityId) &&
            abilityCatalog.TryGet(abilityId, out var ability))
        {
            title = ability.Name;
            summary = ability.Summary;
            return true;
        }

        if (ItemHotkeyBindingQueries.TryGet(_itemHotkeyBindings, playerEntityId, slot, out var itemDefinitionId) &&
            itemCatalog.TryGet(itemDefinitionId, out var item))
        {
            title = item.Name;
            summary = item.Summary;
            return true;
        }

        title = string.Empty;
        summary = string.Empty;
        return false;
    }

    /// <summary>slot's on-screen bounds -- reuses the same EnumerateSlotBounds walk DrawContent/
    /// TryGetSlotAt already share as their single source of truth.</summary>
    internal Rectangle GetSlotBounds(HotkeySlot slot)
    {
        foreach (var (candidateSlot, bounds) in EnumerateSlotBounds())
        {
            if (candidateSlot == slot)
            {
                return bounds;
            }
        }

        return Rectangle.Empty;
    }

    /// <summary>
    /// Writes (or overwrites) slot's item binding -- clears any existing action or item binding
    /// on that slot first, since a slot binds to at most one of {action, item} at a time (see
    /// IHotkeySlotBinding's own doc comment). Does not touch the inventory stack itself: binding
    /// is a reference, not a transfer (see ItemHotkeyBindingComponent's own doc comment). The
    /// real assignment path, driven by GameInputController's content-drag drop resolution.
    /// </summary>
    internal void BindItem(HotkeySlot slot, Guid itemDefinitionId)
    {
        var playerEntityId = world.PlayerEntityId;
        ActionHotkeyBindingQueries.Unbind(_actionHotkeyBindings, playerEntityId, slot);
        ItemHotkeyBindingQueries.Unbind(_itemHotkeyBindings, playerEntityId, slot);
        _itemHotkeyBindings.Add(playerEntityId, new ItemHotkeyBindingComponent(slot, itemDefinitionId));
    }

    /// <summary>Removes slot's item binding, if any -- dragging a bound item off the hotbar entirely (see GameInputController's content-drag path).</summary>
    internal void UnbindItemSlot(HotkeySlot slot) =>
        ItemHotkeyBindingQueries.Unbind(_itemHotkeyBindings, world.PlayerEntityId, slot);

    private void DrawSlot(SpriteBatch spriteBatch, Texture2D unitRectangle, int playerEntityId, HotkeySlot slot, Vector2 slotOrigin)
    {
        var bounds = new Rectangle((int)slotOrigin.X, (int)slotOrigin.Y, (int)SlotSize.X, (int)SlotSize.Y);

        var isArmed = mapViewState.ArmedSlot == slot;
        if (isArmed)
        {
            spriteBatch.Draw(unitRectangle, bounds, ArmedSlotOutlineColor);
        }

        var inset = isArmed ? ArmedOutlineThickness : ContentInset;
        var contentBounds = new Rectangle(bounds.X + inset, bounds.Y + inset, bounds.Width - inset * 2, bounds.Height - inset * 2);

        if (ActionHotkeyBindingQueries.TryGet(_actionHotkeyBindings, playerEntityId, slot, out var abilityId) && abilityCatalog.TryGet(abilityId, out var ability))
        {
            DrawAbilitySlot(spriteBatch, unitRectangle, playerEntityId, ability, contentBounds);
        }
        else if (ItemHotkeyBindingQueries.TryGet(_itemHotkeyBindings, playerEntityId, slot, out var itemDefinitionId) && itemCatalog.TryGet(itemDefinitionId, out var item))
        {
            DrawItemSlot(spriteBatch, unitRectangle, playerEntityId, item, bounds, contentBounds);
        }
        else
        {
            spriteBatch.Draw(unitRectangle, contentBounds, UnboundSlotColor);
        }

        if (isArmed)
        {
            spriteBatch.Draw(unitRectangle, contentBounds, ArmedSlotMaskColor);
        }
    }

    /// <summary>An ability the player can't currently afford (see HasEnoughMana) greys out exactly like an unusable item slot -- same DisabledItemTintColor, same fully-suppressed radial fill (0f) rather than showing a cooldown/lock mask that would read as "almost ready" when it's actually just unaffordable.</summary>
    private void DrawAbilitySlot(SpriteBatch spriteBatch, Texture2D unitRectangle, int playerEntityId, AbilityDefinition ability, Rectangle contentBounds)
    {
        var isUsable = HasEnoughMana(playerEntityId, ability);

        _radialFill.Sprite = ability.SpriteName is not null && SpriteManifest.TryGet(ability.SpriteName, out var sprite) ? sprite : null;
        _radialFill.SpriteTint = isUsable ? Color.White : DisabledItemTintColor;
        _radialFill.Glyph = ability.Glyph;
        _radialFill.GlyphColor = isUsable ? ability.GlyphColor : DisabledItemTintColor;
        _radialFill.BackgroundColor = isUsable ? BoundSlotBackgroundColor : UnboundSlotColor;
        _radialFill.FillPercentage = isUsable ? ComputeAbilityFillPercentage(playerEntityId, ability) : 0f;
        _radialFill.Draw(spriteBatch, unitRectangle, _font, contentBounds);
    }

    /// <summary>Mirrors AbilityActivationSystem/AbilityTargetingController's own gate (see either's doc comment) -- a zero-cost ability (e.g. Punch) always passes.</summary>
    private bool HasEnoughMana(int playerEntityId, AbilityDefinition ability) =>
        ability.ManaCost <= 0 || (_mana.TryGetReadonly(playerEntityId, out var mana) && mana.CurrentMana >= ability.ManaCost);

    /// <summary>
    /// isUsable requires both a ConsumableEffect (e.g. excludes an Equipment/Tool item with no
    /// activated ability yet) and actual remaining stock -- InventoryActions.ConsumeItem removes
    /// the InventoryItemStackComponent entirely once Quantity hits 0 (see its own doc comment),
    /// so "no stack found" here means "used the last one." Greyed out exactly like a
    /// no-ConsumableEffect item, and for the same reason AbilityTargetingController.
    /// HandleItemSlotPress refuses to arm it: a user-friendly parallel to the inventory grid
    /// simply deleting the item once its uses run out, rather than leaving a slot that looks
    /// bound but silently does nothing when pressed.
    /// </summary>
    private void DrawItemSlot(SpriteBatch spriteBatch, Texture2D unitRectangle, int playerEntityId, ItemDefinition item, Rectangle bounds, Rectangle contentBounds)
    {
        var hasStack = InventoryQueries.TryGetStack(_inventoryStacks, playerEntityId, item.Id, out var stack);
        var isUsable = item.Consumable is not null && hasStack && stack.Quantity > 0;

        _radialFill.Sprite = item.SpriteName is not null && SpriteManifest.TryGet(item.SpriteName, out var sprite) ? sprite : null;
        _radialFill.SpriteTint = isUsable ? Color.White : DisabledItemTintColor;
        _radialFill.Glyph = item.Glyph;
        _radialFill.GlyphColor = isUsable ? BoundSlotGlyphColor : DisabledItemTintColor;
        // The whole square greys out, not just the icon -- UnboundSlotColor (dark) against
        // DisabledItemTintColor (mid gray) keeps the icon legible instead of the two blending
        // together the way two similar grays would.
        _radialFill.BackgroundColor = isUsable ? BoundSlotBackgroundColor : UnboundSlotColor;
        _radialFill.FillPercentage = isUsable ? ComputeItemFillPercentage(playerEntityId) : 0f;
        _radialFill.Draw(spriteBatch, unitRectangle, _font, contentBounds);

        if (hasStack)
        {
            ItemIconRenderer.DrawQuantityBadge(spriteBatch, _quantityFont, stack.Quantity, new Vector2(contentBounds.X, contentBounds.Y), new Vector2(contentBounds.Width, contentBounds.Height));
        }

        // Independent of isUsable/hasStack -- the cooldown is the consumer's own status, still
        // meaningful (and still shown on the status bar regardless) even once this particular
        // slot's item is out of stock, e.g. right after using the last one while abusing the
        // cooldown.
        if (item.Consumable is { Kind: ConsumableKind.Potion } &&
            _potionCooldowns.TryGetReadonly(playerEntityId, out var cooldown) && cooldown.FramesRemaining > 0)
        {
            DrawCountdownAboveSlot(spriteBatch, bounds, PotionCooldownEffects.RemainingSeconds(cooldown.FramesRemaining));
        }
    }

    /// <summary>No symbol here (contrast PlayerStatusEffectsContent's status-bar icon) -- just the number, centered above the slot it belongs to.</summary>
    private void DrawCountdownAboveSlot(SpriteBatch spriteBatch, Rectangle slotBounds, int remainingSeconds)
    {
        var text = remainingSeconds.ToString();
        var textSize = _countdownFont.MeasureString(text);
        var textPosition = new Vector2(slotBounds.Center.X - textSize.X / 2f, slotBounds.Y - textSize.Y - CountdownTextGap);

        ShadowedTextRenderer.Draw(spriteBatch, _countdownFont, text, textPosition);
    }

    /// <summary>
    /// The granted instance's own cooldown fraction (any category, if it has one) whenever it's
    /// actually counting down; only otherwise falls back to the shared ActionLock's fraction
    /// (Immediate/Delayed only -- FreeCast bypasses the shared lock entirely).
    ///
    /// The shared ActionLock is genuinely shared across every Immediate/Delayed ability, not
    /// scoped to whichever one actually set it -- so if this were always taken as
    /// Math.Max(lockFraction, cooldownFraction), using a *different* ability (e.g. Default
    /// Attack's short windup) while this one's own, longer, real cooldown is ticking down would
    /// make this icon spike to whatever fraction that unrelated windup happens to be at, then
    /// visibly snap back down once the windup ends -- confusing/wrong, since nothing about this
    /// ability's own readiness actually changed. Preferring cooldownFraction whenever it's
    /// nonzero avoids that: this ability's own cooldown is always the more specific, more
    /// correct signal once it exists, so the shared lock only ever matters for an ability with no
    /// cooldown of its own (e.g. Default Attack), where it's still the only fill signal available.
    /// </summary>
    private float ComputeAbilityFillPercentage(int playerEntityId, AbilityDefinition ability)
    {
        var cooldownFraction = 0f;
        if (ability.Timing.CooldownFrames is { } cooldownFrames &&
            cooldownFrames > 0 &&
            AbilityInstanceQueries.TryGet(_abilityInstances, playerEntityId, ability.Id, out var instance))
        {
            cooldownFraction = (float)instance.CooldownFramesRemaining / cooldownFrames;
        }

        if (cooldownFraction > 0f)
        {
            return cooldownFraction;
        }

        if (ability.Timing.Category != ActionTimingCategory.FreeCast &&
            _actionLocks.TryGetReadonly(playerEntityId, out var actionLock) &&
            actionLock.TotalLockFrames > 0)
        {
            return (float)actionLock.LockFramesRemaining / actionLock.TotalLockFrames;
        }

        return 0f;
    }

    /// <summary>
    /// Items have no per-instance cooldown the way abilities do -- ConsumableActivationSystem
    /// only ever sets the shared ActionLock (see ConsumableEffect.ActionLockFrames' own doc
    /// comment), so that's the only fill signal here. Deliberately not PotionCooldownComponent:
    /// that cooldown never blocks a second potion (see PotionCooldownEffects' own doc comment),
    /// so masking the slot as if it were on cooldown would be actively misleading -- it gets its
    /// own separate, informational display instead (see DrawItemSlot's countdown text).
    /// </summary>
    private float ComputeItemFillPercentage(int playerEntityId)
    {
        if (_actionLocks.TryGetReadonly(playerEntityId, out var actionLock) && actionLock.TotalLockFrames > 0)
        {
            return (float)actionLock.LockFramesRemaining / actionLock.TotalLockFrames;
        }

        return 0f;
    }
}
