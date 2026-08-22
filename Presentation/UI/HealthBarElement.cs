using Engine.ECS.Components.Stores;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>
/// One entity's HP bar at whatever rectangle this Element itself is sized/positioned to -- the
/// InspectionWindow counterpart to PlayerHealthBarContent's own always-player, always-visible
/// bar (see ResourceBarRenderer, the draw logic shared by both, and any other resource bar --
/// mana, a future soul bar -- that adopts the same shape). Unlike PlayerHealthBarContent,
/// draws nothing at all when the configured entity has no HealthComponent (terrain, or an
/// entity genuinely missing one) -- InspectionWindow only ever creates this element when it
/// already knows the subject has one, so there's no "hasHealth: false" light-grey case to show.
/// </summary>
public sealed class HealthBarElement(
    FontService fontService,
    ElementPoolService elementPoolService,
    GlyphRenderer glyphRenderer,
    PackedComponentPool<HealthComponent> healthPool,
    MultiComponentPool<StatModifierComponent>? statModifiers)
    : Element(fontService, elementPoolService, glyphRenderer)
{
    private int _entityId;

    public void Configure(int entityId) => _entityId = entityId;

    public override void DrawContent(GameTime gameTime)
    {
        if (!healthPool.TryGetReadonly(_entityId, out var health) || health.MaximumHealth <= 0)
        {
            return;
        }

        var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(statModifiers, _entityId, StatModifierTarget.MaximumHealth, health.MaximumHealth);
        var healthFraction = effectiveMaximumHealth > 0
            ? MathHelper.Clamp(health.CurrentHealth / effectiveMaximumHealth, 0f, 1f)
            : 1f;

        var origin = ContentAbsolutePosition;
        var size = ContentSize;
        var bar = new Rectangle((int)origin.X, (int)origin.Y, (int)size.X, (int)size.Y);

        ResourceBarRenderer.Draw(ElementPoolService.SpriteBatch, ElementPoolService.UnitRectangle, bar, healthFraction, hasResource: true, HealthBarPalette.OutlineColor, HealthBarPalette.FractionColor);
    }
}
