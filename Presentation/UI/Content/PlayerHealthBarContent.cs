using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Content;

/// <summary>
/// Permanent top-right HUD readout for the player's health -- unlike MapWindow's per-entity
/// tile bars (which hide at full health), this always renders since it's a persistent HUD
/// element, not a transient overlay. Hosted via IWindowContent/SetContent (see
/// ShellBootstrapper), the same pattern DebugWindowContent uses, rather than living inside
/// MapWindow -- it belongs to the HUD tier (screen-absolute coordinates), not the map's own
/// local content-viewport space. Light grey full-width fill when the player has no
/// HealthComponent at all -- reserved for a future temporarily/permanently-immortal player
/// state, rather than hiding the bar outright.
/// </summary>
public sealed class PlayerHealthBarContent(World world, ComponentManager componentManager) : IElementContent
{
    public static readonly Vector2 Size = new(HudMetrics.EntrySize.X * 4.5f, HudMetrics.EntrySize.Y * 0.75f);

    private static readonly Color NoHealthColor = Color.LightGray;
    private static readonly float[] MajorTickFractions = [0.25f, 0.5f, 0.75f];
    private static readonly float[] MinorTickFractions = [0.125f, 0.375f, 0.625f, 0.875f];

    private readonly PackedComponentPool<HealthComponent> _healthPool = componentManager.GetPackedPool<HealthComponent>();

    // Optional -- see StatModifierMath.GetEffectiveValue's own doc comment for why a null pool
    // (StatModifiersModule not registered) is treated the same as "no active modifiers."
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers = componentManager.IsRegistered<StatModifierComponent>()
        ? componentManager.GetMultiPool<StatModifierComponent>()
        : null;

    private Window _hostWindow = null!;

    private bool _hasHealth;
    private float _healthFraction = 1f;

    public void Initialize(Window hostWindow) => _hostWindow = hostWindow;

    /// <summary>Whether the player currently has a HealthComponent and what fraction of effective max health remains is decided here -- Draw only reads the cached fraction and maps it to a fill color/width.</summary>
    public void Update(GameTime gameTime)
    {
        var playerEntityId = world.PlayerEntityId;
        if (playerEntityId < 0 || !_healthPool.TryGetReadonly(playerEntityId, out var health) || health.MaximumHealth <= 0)
        {
            _hasHealth = false;
            _healthFraction = 1f;
            return;
        }

        _hasHealth = true;
        var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(_statModifiers, playerEntityId, StatModifierTarget.MaximumHealth, health.MaximumHealth);
        _healthFraction = effectiveMaximumHealth > 0
            ? MathHelper.Clamp(health.CurrentHealth / effectiveMaximumHealth, 0f, 1f)
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
        spriteBatch.Draw(unitRectangle, outerRectangle, HealthBarPalette.OutlineColor);

        var fillColor = _hasHealth ? HealthBarPalette.FractionColor(_healthFraction) : NoHealthColor;

        var innerWidth = (int)((outerRectangle.Width - 2) * _healthFraction);
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

        spriteBatch.Draw(unitRectangle, new Rectangle(tickX, tickY, 1, tickHeight), HealthBarPalette.OutlineColor);
    }
}
