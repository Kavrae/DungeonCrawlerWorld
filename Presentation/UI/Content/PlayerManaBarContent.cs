using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Rendering;
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

        ResourceBarRenderer.Draw(spriteBatch, unitRectangle, outerRectangle, _manaFraction, _hasMana, ManaBarPalette.OutlineColor, ManaBarPalette.FractionColor);
    }
}
