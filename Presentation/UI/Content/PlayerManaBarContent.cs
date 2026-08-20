using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Content;

/// <summary>
/// Mirrors PlayerHealthBarContent exactly (see its own doc comment for the full rationale) --
/// a permanent top-right HUD readout, hosted below the health bar (see ShellBootstrapper),
/// that always renders rather than hiding at full mana. Light grey full-width fill when the
/// player has no ManaComponent at all -- not an error state, just "hasn't gained a mana-costing
/// ability yet" (see ManaGrant.EnsureManaComponentExists), the same fallback treatment
/// PlayerHealthBarContent gives a hypothetical HealthComponent-less player.
/// </summary>
public sealed class PlayerManaBarContent(World world, ComponentManager componentManager) : IElementContent
{
    public static readonly Vector2 Size = PlayerHealthBarContent.Size;

    private static readonly Color NoManaColor = Color.LightGray;
    private static readonly float[] MajorTickFractions = [0.25f, 0.5f, 0.75f];
    private static readonly float[] MinorTickFractions = [0.125f, 0.375f, 0.625f, 0.875f];

    private readonly PackedComponentPool<ManaComponent> _manaPool = componentManager.GetPackedPool<ManaComponent>();

    // Optional -- see StatModifierMath.GetEffectiveValue's own doc comment for why a null pool
    // (StatModifiersModule not registered) is treated the same as "no active modifiers."
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers = componentManager.IsRegistered<StatModifierComponent>()
        ? componentManager.GetMultiPool<StatModifierComponent>()
        : null;

    private Window _hostWindow = null!;

    private bool _hasMana;
    private float _manaFraction = 1f;

    public void Initialize(Window hostWindow) => _hostWindow = hostWindow;

    /// <summary>Whether the player currently has a ManaComponent and what fraction of effective max mana remains is decided here -- Draw only reads the cached fraction and maps it to a fill color/width.</summary>
    public void Update(GameTime gameTime)
    {
        var playerEntityId = world.PlayerEntityId;
        if (playerEntityId < 0 || !_manaPool.TryGetReadonly(playerEntityId, out var mana) || mana.MaximumMana <= 0)
        {
            _hasMana = false;
            _manaFraction = 1f;
            return;
        }

        _hasMana = true;
        var effectiveMaximumMana = StatModifierMath.GetEffectiveValue(_statModifiers, playerEntityId, StatModifierTarget.MaximumMana, mana.MaximumMana);
        _manaFraction = effectiveMaximumMana > 0
            ? MathHelper.Clamp(mana.CurrentMana / effectiveMaximumMana, 0f, 1f)
            : 1f;
    }

    public void DrawContent(GameTime gameTime)
    {
        var spriteBatch = _hostWindow.ElementPoolService.SpriteBatch;
        var unitRectangle = _hostWindow.ElementPoolService.UnitRectangle;

        // ContentSize, not the static Size -- Size is the window's outer bounds (used by
        // ShellBootstrapper to position/size the host window itself); the actual drawable
        // area is whatever's left after its border insets that, so the bar has to size itself
        // off ContentSize to fit inside the border rather than drawing over it.
        var origin = _hostWindow.ContentAbsolutePosition;
        var contentSize = _hostWindow.ContentSize;
        var outerRectangle = new Rectangle((int)origin.X, (int)origin.Y, (int)contentSize.X, (int)contentSize.Y);
        spriteBatch.Draw(unitRectangle, outerRectangle, ManaBarPalette.OutlineColor);

        var fillColor = _hasMana ? ManaBarPalette.FractionColor(_manaFraction) : NoManaColor;

        var innerWidth = (int)((outerRectangle.Width - 2) * _manaFraction);
        if (innerWidth > 0)
        {
            spriteBatch.Draw(unitRectangle, new Rectangle(outerRectangle.X + 1, outerRectangle.Y + 1, innerWidth, outerRectangle.Height - 2), fillColor);
        }

        DrawTicks(spriteBatch, unitRectangle, outerRectangle);
    }

    /// <summary>Major ticks (half bar height) at the 1/4, 1/2, 3/4 marks; minor ticks (quarter bar height) at the 1/8, 3/8, 5/8, 7/8 marks -- both flush with the bar's bottom edge (ruler-style graduations), drawn over the fill.</summary>
    private static void DrawTicks(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle outerRectangle)
    {
        foreach (var fraction in MajorTickFractions)
        {
            DrawTick(spriteBatch, unitRectangle, outerRectangle, fraction, outerRectangle.Height / 2);
        }

        foreach (var fraction in MinorTickFractions)
        {
            DrawTick(spriteBatch, unitRectangle, outerRectangle, fraction, outerRectangle.Height / 4);
        }
    }

    private static void DrawTick(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle outerRectangle, float widthFraction, int tickHeight)
    {
        var tickX = outerRectangle.X + (int)(outerRectangle.Width * widthFraction);
        var tickY = outerRectangle.Bottom - tickHeight;

        spriteBatch.Draw(unitRectangle, new Rectangle(tickX, tickY, 1, tickHeight), ManaBarPalette.OutlineColor);
    }
}
