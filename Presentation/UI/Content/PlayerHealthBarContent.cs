using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Content;

/// <summary>
/// Permanent top-right HUD readout for the player's health -- unlike MapWindow's per-entity
/// tile bars (which hide at full health), this always renders since it's a persistent HUD
/// element, not a transient overlay. Hosted via IWindowContent/SetContent (see
/// ShellBootstrapper), the same pattern DebugWindowContent uses, rather than living inside
/// MapWindow -- it belongs to the HUD tier (screen-absolute coordinates), not the map's own
/// local content-viewport space. Light grey full-width fill when the player has no
/// SimpleHealthComponent at all -- reserved for a future temporarily/permanently-immortal player
/// state, rather than hiding the bar outright.
/// </summary>
public sealed class PlayerHealthBarContent(World world, ComponentManager componentManager) : IElementContent
{
    public static readonly Vector2 Size = new(HudMetrics.EntrySize.X * 4.5f, HudMetrics.EntrySize.Y * 0.75f);

    private readonly PackedComponentPool<SimpleHealthComponent> _healthPool = componentManager.GetPackedPool<SimpleHealthComponent>();
    private readonly MultiComponentPool<BodyPartComponent> _bodyParts = componentManager.GetMultiPool<BodyPartComponent>();

    // Optional -- see StatModifierMath.GetEffectiveValue's own doc comment for why a null pool
    // (StatModifiersModule not registered) is treated the same as "no active modifiers."
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers = componentManager.IsRegistered<StatModifierComponent>()
        ? componentManager.GetMultiPool<StatModifierComponent>()
        : null;

    private Window _hostWindow = null!;

    private bool _hasHealth;
    private float _healthFraction = 1f;

    public void Initialize(Window hostWindow) => _hostWindow = hostWindow;

    /// <summary>Whether the player currently has a SimpleHealthComponent and what fraction of effective max health remains is decided here -- Draw only reads the cached fraction and maps it to a fill color/width.</summary>
    public void Update(GameTime gameTime)
    {
        var playerEntityId = world.PlayerEntityId;
        if (playerEntityId < 0 || !HealthQueries.TryGetTotals(_healthPool, _bodyParts, playerEntityId, out var currentHealth, out var maximumHealth) || maximumHealth <= 0)
        {
            _hasHealth = false;
            _healthFraction = 1f;
            return;
        }

        _hasHealth = true;
        var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(_statModifiers, playerEntityId, StatModifierTarget.MaximumHealth, maximumHealth);
        _healthFraction = effectiveMaximumHealth > 0
            ? MathHelper.Clamp(currentHealth / effectiveMaximumHealth, 0f, 1f)
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

        ResourceBarRenderer.Draw(spriteBatch, unitRectangle, outerRectangle, _healthFraction, _hasHealth, HealthBarPalette.OutlineColor, HealthBarPalette.FractionColor);
    }
}
