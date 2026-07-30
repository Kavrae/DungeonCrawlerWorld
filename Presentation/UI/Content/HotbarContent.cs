using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using FontStashSharp;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Input;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// Renders one slot per HotkeySlot value (all 10, not just bound ones), laid out in the QE /
/// RFV / 12345 visual clusters from HotkeySlotLayout. An unbound slot is an empty dark-gray
/// square -- no glyph, background, or radial fill -- reserved for a future binding rather than
/// simply omitted. A bound slot reuses RadialFillRenderer exactly as ActionLockContent already
/// does, with FillPercentage taking the greater of the shared ActionLock's fraction (Immediate/
/// Delayed abilities only -- FreeCast bypasses the shared lock entirely) and the granted
/// instance's own cooldown fraction (any category, if AbilityTiming.CooldownFrames is set) --
/// an ability with neither simply never shows a mask, since RadialFillRenderer already no-ops
/// at FillPercentage &lt;= 0. The currently-armed slot (MapViewState.ArmedSlot) gets a distinct
/// outline drawn first, with the slot's normal content inset within it. Implements TODO.md's
/// "Inventory and spell hotbar" and "Player attack button or key" items.
/// </summary>
public sealed class HotbarContent(World world, MapViewState mapViewState, ComponentManager componentManager, AbilityCatalog abilityCatalog, FontService fontService) : IWindowContent
{
    public static readonly Vector2 SlotSize = new(HudMetrics.EntrySize.Y * 1.5f, HudMetrics.EntrySize.Y * 1.5f);

    private const float GlyphSizeFraction = 0.75f;
    private const int ContentInset = 2;
    private const int ArmedOutlineThickness = 3;
    private const float SlotGap = 4f;
    private const float GroupGap = 16f;

    private static readonly Color UnboundSlotColor = new(48, 48, 48);
    private static readonly Color BoundSlotBackgroundColor = Color.WhiteSmoke;
    private static readonly Color BoundSlotGlyphColor = Color.Black;
    private static readonly Color ArmedSlotOutlineColor = Color.Gold;

    public static readonly Vector2 Size = ComputeTotalSize();

    private readonly MultiComponentPool<HotkeyBindingComponent> _hotkeyBindings = componentManager.GetMultiPool<HotkeyBindingComponent>();
    private readonly MultiComponentPool<AbilityInstanceComponent> _abilityInstances = componentManager.GetMultiPool<AbilityInstanceComponent>();
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks = componentManager.GetPackedPool<ActionLockComponent>();
    private readonly RadialFillRenderer _radialFill = new(new GlyphRenderer());

    private Window _hostWindow = null!;
    private SpriteFontBase _font = null!;

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
    }

    public void Update(GameTime gameTime) { }

    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        var origin = _hostWindow.ContentAbsolutePosition;
        var playerEntityId = world.PlayerEntityId;
        var x = origin.X;

        foreach (var group in HotkeySlotLayout.VisualGroups)
        {
            foreach (var slot in group)
            {
                DrawSlot(spriteBatch, unitRectangle, playerEntityId, slot, new Vector2(x, origin.Y));
                x += SlotSize.X + SlotGap;
            }

            // The trailing intra-group gap just added becomes the wider group gap instead.
            x += GroupGap - SlotGap;
        }
    }

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

        if (!HotkeyBindingQueries.TryGet(_hotkeyBindings, playerEntityId, slot, out var abilityId) ||
            !abilityCatalog.TryGet(abilityId, out var ability))
        {
            spriteBatch.Draw(unitRectangle, contentBounds, UnboundSlotColor);
            return;
        }

        _radialFill.Glyph = ability.Glyph;
        _radialFill.GlyphColor = BoundSlotGlyphColor;
        _radialFill.BackgroundColor = BoundSlotBackgroundColor;
        _radialFill.FillPercentage = ComputeFillPercentage(playerEntityId, ability);
        _radialFill.Draw(spriteBatch, unitRectangle, _font, contentBounds);
    }

    /// <summary>The greater of the shared ActionLock's fraction (Immediate/Delayed only) and the granted instance's own cooldown fraction (any category, if it has one) -- see this class's own doc comment.</summary>
    private float ComputeFillPercentage(int playerEntityId, AbilityDefinition ability)
    {
        var lockFraction = 0f;
        if (ability.Timing.Category != ActionTimingCategory.FreeCast &&
            _actionLocks.TryGetReadonly(playerEntityId, out var actionLock) &&
            actionLock.TotalLockFrames > 0)
        {
            lockFraction = (float)actionLock.LockFramesRemaining / actionLock.TotalLockFrames;
        }

        var cooldownFraction = 0f;
        if (ability.Timing.CooldownFrames is { } cooldownFrames &&
            cooldownFrames > 0 &&
            AbilityInstanceQueries.TryGet(_abilityInstances, playerEntityId, ability.Id, out var instance))
        {
            cooldownFraction = (float)instance.CooldownFramesRemaining / cooldownFrames;
        }

        return System.Math.Max(lockFraction, cooldownFraction);
    }
}
