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
/// Three fixed HotkeyCategory groups laid out left to right (see
/// HotkeySlotLayout) -- Base (3 slots, always present), DefaultAttack (1 slot, always present),
/// and Expansion (up to 20 slots as two 2x5 pages placed side by side -- page 2 to the right of
/// page 1, not stacked below -- growing as HotkeyExpansionUnlockComponent.UnlockedSlotCount
/// increases; see GetExpansionRowsVisible/GetExpansionPagesVisible). Base/DefaultAttack are
/// always vertically centered against Expansion's current height, and the whole bar's Size
/// changes as rows/pages are revealed, so this window resizes/repositions itself at runtime (see
/// RefreshLayoutIfChanged) rather than having a fixed Size the way most HUD windows do. Every
/// slot always renders (bound, unbound, or not-yet-unlocked alike) with a permanent
/// BorderStyle.FlatContrast border; the armed slot additionally gets GlowRenderer's outward glow
/// (see NotificationCenter's own unread-glow for the same primitive). A slot that's disabled for
/// any reason -- an unaffordable ability, an unusable/out-of-stock item, or a not-yet-unlocked
/// Expansion slot in an already-revealed row/page -- draws its border, icon, and text overlays
/// all at DisabledSlotAlpha; the radial cooldown/lock wedge (RadialFillRenderer) stays scoped to
/// the icon itself regardless. Implements TODO.md's "Inventory and spell hotbar" and "Player
/// attack button or key" items.
/// </summary>
public sealed class HotbarContent(
    World world,
    MapViewState mapViewState,
    ComponentManager componentManager,
    AbilityCatalog abilityCatalog,
    ItemCatalog itemCatalog,
    FontService fontService,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    Vector2 screenSize) : IElementContent
{
    public static readonly Vector2 SlotSize = new(HudMetrics.EntrySize.Y * 2.25f, HudMetrics.EntrySize.Y * 2.25f);

    private const int BaseSlotCount = 3;
    private const int ExpansionColumnsPerRow = 5;
    private const int MaxExpansionRows = 2;
    private const int MaxExpansionPages = 2;
    private const int SlotsPerExpansionPage = ExpansionColumnsPerRow * MaxExpansionRows;

    /// <summary>Fallback only -- every real entity that can open a hotbar gets one from PlayerBlueprint. Matches Expansion's old fixed slot count, so a missing component still shows a sensible bar rather than an empty/degenerate one.</summary>
    private const short DefaultUnlockedExpansionSlots = 10;

    private const float GlyphSizeFraction = 0.75f;
    private const float OverlayFontFraction = 0.3f;
    private const float CountdownFontFraction = 0.4375f;
    private Vector2 OverlayPadding = new(4f, 2f);
    private const float SlotGap = 1f;
    private const float GroupGap = 10f;
    private const float ExpansionPageGap = GroupGap;
    private const float CountdownTextGap = 2f;
    private const float DisabledSlotAlpha = 0.5f;
    private static readonly BorderThickness SlotBorderThickness = BorderThickness.Uniform(new Vector2(2, 2));

    private static readonly Color UnboundSlotColor = new(48, 48, 48);
    private static readonly Color BoundSlotBackgroundColor = Color.WhiteSmoke;
    private static readonly Color BoundSlotGlyphColor = Color.Black;
    private static readonly Color ArmedGlowColor = Color.Gold;
    private static readonly Color DragDropTargetGlowColor = Color.Gold;

    /// <summary>Width for the Armed Hotkey Summary window: 3 slots plus the 2 inter-slot gaps
    /// between them (see ArmedHotkeySummaryWindow, which centers this over whichever single slot
    /// it's currently showing).</summary>
    internal const int SummarySlotSpan = 3;
    internal static readonly float SummaryWidth = SummarySlotSpan * SlotSize.X + (SummarySlotSpan - 1) * SlotGap;

    /// <summary>
    /// Set by UiInputController while a content-drag (an inventory item cell, or an already-
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
    private readonly PackedComponentPool<HotkeyExpansionUnlockComponent> _hotkeyExpansionUnlocks = componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>();
    private readonly RadialFillRenderer _radialFill = new(new GlyphRenderer(), spriteSheetService, spriteRenderer);

    private Window _hostWindow = null!;
    private SpriteFontBase _font = null!;
    private SpriteFontBase _overlayFont = null!;
    private SpriteFontBase _countdownFont = null!;

    /// <summary>Whether each slot is currently active (usable) -- refreshed once per Update (see RefreshSlotActiveStates), not recomputed during Draw. Deciding *whether* a slot is disabled (locked, unaffordable, out of stock) is state/game logic; Draw only ever asks "is this slot active" and independently decides how that reads visually (see AlphaFor) -- the two are deliberately kept separate rather than DrawAbilitySlot/DrawItemSlot each computing and returning their own alpha for the rest of DrawSlot to reuse.</summary>
    private readonly Dictionary<HotkeySlot, bool> _slotActiveStates = [];

    /// <summary>Read directly from the pool at construction (not deferred to Initialize/Update) specifically so GameShellBootstrapper can read a correct Size immediately after `new HotbarContent(...)`, before the host Window -- which needs that Size to construct itself -- exists at all. Field initializers can't reference another instance field (only the primary constructor's own parameters), hence re-resolving the pool from componentManager here rather than reusing _hotkeyExpansionUnlocks.</summary>
    private short _unlockedExpansionSlots = GetUnlockedExpansionSlots(componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>(), world.PlayerEntityId);

    private int _lastLayoutRowsVisible = GetExpansionRowsVisible(GetUnlockedExpansionSlots(componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>(), world.PlayerEntityId));
    private int _lastLayoutPagesVisible = GetExpansionPagesVisible(GetUnlockedExpansionSlots(componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>(), world.PlayerEntityId));

    /// <summary>The bar's current total bounding size -- depends on how many Expansion rows/pages are currently revealed, so unlike most HUD content's Size this is an instance property, not a static constant.</summary>
    public Vector2 Size => ComputeSize(GetExpansionRowsVisible(_unlockedExpansionSlots), GetExpansionPagesVisible(_unlockedExpansionSlots));

    private static Vector2 ComputeSize(int rowsVisible, int pagesVisible)
    {
        var baseWidth = BaseSlotCount * SlotSize.X + (BaseSlotCount - 1) * SlotGap;
        var defaultAttackWidth = SlotSize.X;
        var pageWidth = ExpansionColumnsPerRow * SlotSize.X + (ExpansionColumnsPerRow - 1) * SlotGap;
        var expansionWidth = pagesVisible * pageWidth + (pagesVisible - 1) * ExpansionPageGap;
        var totalWidth = baseWidth + GroupGap + defaultAttackWidth + GroupGap + expansionWidth;

        var expansionHeight = rowsVisible * SlotSize.Y + (rowsVisible - 1) * SlotGap;
        return new Vector2(totalWidth, expansionHeight);
    }

    /// <summary>Page 1 (Slot1-10) fills top-to-bottom first -- 1 row until more than a row's worth is unlocked, then 2 (the max -- see this class's own doc comment on pages sitting side by side instead of stacking further).</summary>
    private static int GetExpansionRowsVisible(short unlockedSlots) =>
        unlockedSlots > ExpansionColumnsPerRow ? MaxExpansionRows : 1;

    /// <summary>Page 2 (Slot11-20) only appears once page 1 is entirely unlocked.</summary>
    private static int GetExpansionPagesVisible(short unlockedSlots) =>
        unlockedSlots > SlotsPerExpansionPage ? MaxExpansionPages : 1;

    /// <summary>playerEntityId can still be World's unset sentinel (-1) here -- GameShellBootstrapper constructs this class (and reads its Size) before GameLoop's first Update actually spawns the player (see FloorBuilder.CreatePlayer), so a negative id must fall back the same as "no component" rather than indexing the pool with it.</summary>
    private static short GetUnlockedExpansionSlots(PackedComponentPool<HotkeyExpansionUnlockComponent> hotkeyExpansionUnlocks, int playerEntityId) =>
        playerEntityId >= 0 && hotkeyExpansionUnlocks.TryGetReadonly(playerEntityId, out var unlock) ? unlock.UnlockedSlotCount : DefaultUnlockedExpansionSlots;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont((int)(SlotSize.Y * GlyphSizeFraction));
        _overlayFont = fontService.GetFont((int)(SlotSize.Y * OverlayFontFraction));
        _countdownFont = fontService.GetFont((int)(SlotSize.Y * CountdownFontFraction));
    }

    public void Update(GameTime gameTime)
    {
        _unlockedExpansionSlots = GetUnlockedExpansionSlots(_hotkeyExpansionUnlocks, world.PlayerEntityId);
        RefreshLayoutIfChanged();
        RefreshSlotActiveStates();
    }

    /// <summary>
    /// Whether each slot is disabled -- locked (see IsSlotLocked), an unaffordable ability, or an
    /// unusable/out-of-stock item -- is entirely decided here, once per frame, not inside Draw.
    /// playerEntityId can still be -1 on the very first Update (see GetUnlockedExpansionSlots'
    /// own doc comment on why) -- skip entirely rather than indexing the component pools with it;
    /// DrawSlot's own GetValueOrDefault(slot, true) fallback keeps every slot looking active for
    /// that one frame, which is harmless since nothing meaningful is bound before the player
    /// exists anyway.
    /// </summary>
    private void RefreshSlotActiveStates()
    {
        var playerEntityId = world.PlayerEntityId;
        if (playerEntityId < 0)
        {
            return;
        }

        foreach (var entry in HotkeySlotLayout.Entries)
        {
            _slotActiveStates[entry.Slot] = ComputeIsSlotActive(playerEntityId, entry.Slot);
        }
    }

    private bool ComputeIsSlotActive(int playerEntityId, HotkeySlot slot)
    {
        if (IsSlotLocked(slot))
        {
            return false;
        }

        if (ActionHotkeyBindingQueries.TryGet(_actionHotkeyBindings, playerEntityId, slot, out var abilityId) && abilityCatalog.TryGet(abilityId, out var ability))
        {
            return HasEnoughMana(playerEntityId, ability);
        }

        if (ItemHotkeyBindingQueries.TryGet(_itemHotkeyBindings, playerEntityId, slot, out var itemDefinitionId) && itemCatalog.TryGet(itemDefinitionId, out var item))
        {
            return IsItemUsable(playerEntityId, item);
        }

        // Unbound but unlocked -- empty, not disabled (nothing to fade).
        return true;
    }

    /// <summary>Unlocking more Expansion slots is rare and permanent (see HotkeyExpansionEffects.Grant), so this only actually resizes/repositions the host window on the frame the row/page count changes, not every frame -- centered horizontally and anchored to the same bottom margin as before, recomputed against the new Size so the bar grows upward as rows are added and rightward as page 2 appears (see GameShellBootstrapper, which sizes/positions the window identically at construction).</summary>
    private void RefreshLayoutIfChanged()
    {
        var rowsVisible = GetExpansionRowsVisible(_unlockedExpansionSlots);
        var pagesVisible = GetExpansionPagesVisible(_unlockedExpansionSlots);
        if (rowsVisible == _lastLayoutRowsVisible && pagesVisible == _lastLayoutPagesVisible)
        {
            return;
        }

        _lastLayoutRowsVisible = rowsVisible;
        _lastLayoutPagesVisible = pagesVisible;
        var newSize = ComputeSize(rowsVisible, pagesVisible);
        var newPosition = ComputeBottomCenteredPosition(newSize, screenSize);
        _hostWindow.SetBounds(newPosition, newSize);
    }

    /// <summary>Shared by GameShellBootstrapper (initial placement) and RefreshLayoutIfChanged (every subsequent resize) so the two can never drift into disagreeing formulas.</summary>
    public static Vector2 ComputeBottomCenteredPosition(Vector2 size, Vector2 screenSize) =>
        new((screenSize.X - size.X) / 2f, screenSize.Y - size.Y - HudMetrics.Margin.Y * 1.5f);

    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        var playerEntityId = world.PlayerEntityId;

        foreach (var (slot, bounds) in EnumerateSlotBounds())
        {
            DrawSlot(spriteBatch, unitRectangle, playerEntityId, slot, bounds);

            if (IsAcceptingDrag)
            {
                GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, DragDropTargetGlowColor);
            }
        }
    }

    /// <summary>
    /// Every slot's bounds, in the same order DrawContent walks -- the shared layout math behind
    /// drawing, the drag-drop glow above, and TryGetSlotAt below, so all three can never drift
    /// out of sync with each other the way three independent copies of this walk risked. Left to
    /// right: Base (horizontal row) -> gap -> DefaultAttack (single slot) -> gap -> Expansion,
    /// itself up to two 2x5 pages side by side (page 2 to the right of page 1, with its own
    /// ExpansionPageGap) -- only entry.Row &lt; rowsVisible and entry.Page &lt; pagesVisible slots
    /// are drawn at all; a not-yet-revealed row/page doesn't just render dim, it isn't laid out or
    /// hit-testable yet. Base/DefaultAttack are vertically centered against Expansion's current
    /// height; Expansion itself sits flush at the content origin's Y, which is what makes the bar
    /// grow upward as rows are added (see RefreshLayoutIfChanged's own doc comment).
    /// </summary>
    private IEnumerable<(HotkeySlot Slot, Rectangle Bounds)> EnumerateSlotBounds()
    {
        var rowsVisible = GetExpansionRowsVisible(_unlockedExpansionSlots);
        var pagesVisible = GetExpansionPagesVisible(_unlockedExpansionSlots);
        var expansionHeight = ComputeSize(rowsVisible, pagesVisible).Y;
        var origin = _hostWindow.ContentAbsolutePosition;
        var baseWidth = BaseSlotCount * SlotSize.X + (BaseSlotCount - 1) * SlotGap;
        var centeredRowY = origin.Y + (expansionHeight - SlotSize.Y) / 2f;

        var x = origin.X;
        for (var i = 0; i < BaseSlotCount; i++)
        {
            var slot = (HotkeySlot)((int)HotkeySlot.Base1 + i);
            yield return (slot, new Rectangle((int)x, (int)centeredRowY, (int)SlotSize.X, (int)SlotSize.Y));
            x += SlotSize.X + SlotGap;
        }

        x = origin.X + baseWidth + GroupGap;
        yield return (HotkeySlot.DefaultAttack, new Rectangle((int)x, (int)centeredRowY, (int)SlotSize.X, (int)SlotSize.Y));

        var expansionOriginX = x + SlotSize.X + GroupGap;
        var pageWidth = ExpansionColumnsPerRow * SlotSize.X + (ExpansionColumnsPerRow - 1) * SlotGap;

        foreach (var entry in HotkeySlotLayout.Entries)
        {
            if (entry.Category != HotkeyCategory.Expansion || entry.Row >= rowsVisible || entry.Page >= pagesVisible)
            {
                continue;
            }

            var pageOriginX = expansionOriginX + entry.Page * (pageWidth + ExpansionPageGap);
            var slotX = pageOriginX + entry.Column * (SlotSize.X + SlotGap);
            var slotY = origin.Y + entry.Row * (SlotSize.Y + SlotGap);
            yield return (entry.Slot, new Rectangle((int)slotX, (int)slotY, (int)SlotSize.X, (int)SlotSize.Y));
        }
    }

    /// <summary>Screen-position hit test for exactly which hotbar slot (if any) screenPosition falls within -- UiInputController's content-drag path uses this both as a bind-drop target and, combined with TryGetBoundItemId, to detect a drag starting on an already-bound slot.</summary>
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

    /// <summary>The item (if any) currently bound to slot -- UiInputController's content-drag path reads this at press time to capture the payload of a drag starting on an already-bound hotbar slot.</summary>
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
    /// real assignment path, driven by UiInputController's content-drag drop resolution. A
    /// not-yet-unlocked Expansion slot silently refuses the binding -- it isn't a valid drop
    /// target (see this class's own doc comment on the disabled-alpha treatment) -- rather than
    /// UiInputController needing its own separate lock-awareness.
    /// </summary>
    internal void BindItem(HotkeySlot slot, Guid itemDefinitionId)
    {
        if (IsSlotLocked(slot))
        {
            return;
        }

        var playerEntityId = world.PlayerEntityId;
        ActionHotkeyBindingQueries.Unbind(_actionHotkeyBindings, playerEntityId, slot);
        ItemHotkeyBindingQueries.Unbind(_itemHotkeyBindings, playerEntityId, slot);
        _itemHotkeyBindings.Add(playerEntityId, new ItemHotkeyBindingComponent(slot, itemDefinitionId));
    }

    /// <summary>Removes slot's item binding, if any -- dragging a bound item off the hotbar entirely (see UiInputController's content-drag path).</summary>
    internal void UnbindItemSlot(HotkeySlot slot) =>
        ItemHotkeyBindingQueries.Unbind(_itemHotkeyBindings, world.PlayerEntityId, slot);

    /// <summary>Only Expansion slots can ever be locked -- Base/DefaultAttack are always exactly 3+1 and never grow (see this class's own doc comment). globalIndex is 1-based (page*10 + row*5 + column + 1) to match UnlockedSlotCount's own "how many are unlocked" counting, which still runs Slot1..Slot20 in order regardless of page 2 sitting to the right rather than below.</summary>
    private bool IsSlotLocked(HotkeySlot slot)
    {
        var entry = HotkeySlotLayout.GetEntry(slot);
        if (entry.Category != HotkeyCategory.Expansion)
        {
            return false;
        }

        var globalIndex = entry.Page * SlotsPerExpansionPage + entry.Row * ExpansionColumnsPerRow + entry.Column + 1;
        return globalIndex > _unlockedExpansionSlots;
    }

    private void DrawSlot(SpriteBatch spriteBatch, Texture2D unitRectangle, int playerEntityId, HotkeySlot slot, Rectangle bounds)
    {
        var contentBounds = BorderThickness.Inset(bounds, SlotBorderThickness);
        var isActive = _slotActiveStates.GetValueOrDefault(slot, true);
        var alpha = AlphaFor(isActive);
        string? manaCostText = null;
        string? stackText = null;

        if (ActionHotkeyBindingQueries.TryGet(_actionHotkeyBindings, playerEntityId, slot, out var abilityId) && abilityCatalog.TryGet(abilityId, out var ability))
        {
            DrawAbilitySlot(spriteBatch, unitRectangle, playerEntityId, ability, contentBounds, isActive);
            if (ability.ManaCost > 0)
            {
                manaCostText = ability.ManaCost.ToString();
            }
        }
        else if (ItemHotkeyBindingQueries.TryGet(_itemHotkeyBindings, playerEntityId, slot, out var itemDefinitionId) && itemCatalog.TryGet(itemDefinitionId, out var item))
        {
            DrawItemSlot(spriteBatch, unitRectangle, playerEntityId, item, bounds, contentBounds, isActive, out var stackQuantity);
            if (stackQuantity > 1)
            {
                stackText = $"x{stackQuantity}";
            }
        }
        else
        {
            // Unbound -- either genuinely empty (isActive true) or a not-yet-unlocked Expansion
            // slot (isActive false, see ComputeIsSlotActive) -- either way, just the flat tint.
            spriteBatch.Draw(unitRectangle, contentBounds, UnboundSlotColor * alpha);
        }

        var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(bounds, SlotBorderThickness);
        BorderRenderer.Draw(spriteBatch, unitRectangle, BorderStyle.FlatContrast, Color.White, top, bottom, left, right, alpha);

        if (mapViewState.ArmedSlot == slot)
        {
            GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, ArmedGlowColor);
        }

        DrawKeyLabel(spriteBatch, bounds, slot, alpha);

        if (manaCostText is not null)
        {
            DrawBottomLeftText(spriteBatch, bounds, manaCostText, alpha);
        }

        if (stackText is not null)
        {
            DrawBottomCenterText(spriteBatch, bounds, stackText, alpha);
        }
    }

    /// <summary>The one place isActive turns into an opacity -- every draw call in DrawSlot (border, icon, every text overlay) goes through this same mapping, rather than each piece deciding its own alpha.</summary>
    private static float AlphaFor(bool isActive) => isActive ? 1f : DisabledSlotAlpha;

    /// <summary>isActive (see RefreshSlotActiveStates -- an Update-time decision) drives both the icon's opacity and whether the cooldown/lock wedge shows at all: an inactive (unaffordable) ability suppresses the radial fill entirely (0f) rather than showing a mask that would read as "almost ready" when it's actually just unaffordable.</summary>
    private void DrawAbilitySlot(SpriteBatch spriteBatch, Texture2D unitRectangle, int playerEntityId, AbilityDefinition ability, Rectangle contentBounds, bool isActive)
    {
        _radialFill.Sprite = ability.SpriteName is not null && SpriteManifest.TryGet(ability.SpriteName, out var sprite) ? sprite : null;
        _radialFill.SpriteTint = Color.White;
        _radialFill.Glyph = ability.Glyph;
        _radialFill.GlyphColor = ability.GlyphColor;
        _radialFill.BackgroundColor = BoundSlotBackgroundColor;
        _radialFill.FillPercentage = isActive ? ComputeAbilityFillPercentage(playerEntityId, ability) : 0f;
        _radialFill.Draw(spriteBatch, unitRectangle, _font, contentBounds, AlphaFor(isActive));
    }

    /// <summary>Mirrors AbilityActivationSystem/ActionTargetingController's own gate (see either's doc comment) -- a zero-cost ability (e.g. Punch) always passes.</summary>
    private bool HasEnoughMana(int playerEntityId, AbilityDefinition ability) =>
        ability.ManaCost <= 0 || (_mana.TryGetReadonly(playerEntityId, out var mana) && mana.CurrentMana >= ability.ManaCost);

    /// <summary>Requires both a ConsumableEffect (e.g. excludes an Equipment/Tool item with no activated ability yet) and actual remaining stock -- InventoryActions.ConsumeItem removes the InventoryItemStackComponent entirely once Quantity hits 0 (see its own doc comment), so "no stack found" here means "used the last one."</summary>
    private bool IsItemUsable(int playerEntityId, ItemDefinition item) =>
        item.Consumable is not null && InventoryQueries.TryGetStack(_inventoryStacks, playerEntityId, item.Id, out var stack) && stack.Quantity > 0;

    /// <summary>
    /// isActive (see RefreshSlotActiveStates -- an Update-time decision, via IsItemUsable) drives
    /// both the icon's opacity and whether the cooldown/lock wedge shows at all, the same as
    /// DrawAbilitySlot. quantity is always returned (0 if no stack at all) so the caller can
    /// decide whether to draw the "x{n}" overlay without a second inventory lookup.
    /// </summary>
    private void DrawItemSlot(SpriteBatch spriteBatch, Texture2D unitRectangle, int playerEntityId, ItemDefinition item, Rectangle bounds, Rectangle contentBounds, bool isActive, out int quantity)
    {
        var hasStack = InventoryQueries.TryGetStack(_inventoryStacks, playerEntityId, item.Id, out var stack);
        quantity = hasStack ? stack.Quantity : 0;

        _radialFill.Sprite = item.SpriteName is not null && SpriteManifest.TryGet(item.SpriteName, out var sprite) ? sprite : null;
        _radialFill.SpriteTint = Color.White;
        _radialFill.Glyph = item.Glyph;
        _radialFill.GlyphColor = BoundSlotGlyphColor;
        _radialFill.BackgroundColor = BoundSlotBackgroundColor;
        _radialFill.FillPercentage = isActive ? ComputeItemFillPercentage(playerEntityId) : 0f;
        _radialFill.Draw(spriteBatch, unitRectangle, _font, contentBounds, AlphaFor(isActive));

        // Independent of isActive/hasStack -- the cooldown is the consumer's own status, still
        // meaningful (and still shown on the status bar regardless) even once this particular
        // slot's item is out of stock, e.g. right after using the last one while abusing the
        // cooldown. Always drawn at full alpha -- informational, not gated by this slot's own
        // disabled state (see PotionCooldownEffects' own doc comment: the cooldown never blocks a
        // second potion in the first place).
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

        ContrastTextRenderer.Draw(spriteBatch, _countdownFont, text, textPosition);
    }

    /// <summary>Top-left of every slot -- the bind key from HotkeySlotLayout (e.g. "Q", "1", "↑1" for a Shift-page Expansion slot). Drawn even on a locked slot (dimmed like everything else there), a preview of what the key will do once unlocked.</summary>
    private void DrawKeyLabel(SpriteBatch spriteBatch, Rectangle bounds, HotkeySlot slot, float alpha)
    {
        var text = HotkeySlotLayout.GetKeyLabel(slot);
        var position = new Vector2(bounds.X + OverlayPadding.X, bounds.Y + OverlayPadding.Y);
        ContrastTextRenderer.Draw(spriteBatch, _overlayFont, text, position, alpha);
    }

    /// <summary>Bottom-left -- an ability's mana cost, only ever called when ManaCost > 0.</summary>
    private void DrawBottomLeftText(SpriteBatch spriteBatch, Rectangle bounds, string text, float alpha)
    {
        var textSize = _overlayFont.MeasureString(text);
        var position = new Vector2(bounds.X + OverlayPadding.X, bounds.Bottom - textSize.Y - OverlayPadding.Y);
        ContrastTextRenderer.Draw(spriteBatch, _overlayFont, text, position, alpha);
    }

    /// <summary>Bottom-center -- an item stack's quantity as "x{n}", only ever called when quantity > 1. Replaces the old bottom-right ItemIconRenderer.DrawQuantityBadge call -- ItemIconRenderer itself is untouched, InventoryGridContent/InventoryItemStackCell still use it for the inventory grid's own cells.</summary>
    private void DrawBottomCenterText(SpriteBatch spriteBatch, Rectangle bounds, string text, float alpha)
    {
        var textSize = _overlayFont.MeasureString(text);
        var position = new Vector2(bounds.Center.X - textSize.X / 2f, bounds.Bottom - textSize.Y - OverlayPadding.Y);
        ContrastTextRenderer.Draw(spriteBatch, _overlayFont, text, position, alpha);
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
