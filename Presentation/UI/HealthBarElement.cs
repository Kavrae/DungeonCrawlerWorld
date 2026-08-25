using Engine.ECS.Components.Stores;
using Game.Modules.Health;
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
/// draws nothing at all when the configured entity has neither a SimpleHealthComponent nor a
/// BodyPartComponent (terrain, or an entity genuinely missing both) -- InspectionWindow only
/// ever creates this element when it already knows the subject has one, so there's no
/// "hasHealth: false" light-grey case to show.
/// </summary>
public sealed class HealthBarElement(
    FontService fontService,
    ElementPoolService elementPoolService,
    LabelRenderer labelRenderer,
    PackedComponentPool<SimpleHealthComponent> healthPool,
    MultiComponentPool<BodyPartComponent> bodyParts,
    MultiComponentPool<StatModifierComponent>? statModifiers)
    : Element(fontService, elementPoolService, labelRenderer)
{
    private int _entityId;

    public void Configure(int entityId) => _entityId = entityId;

    public override void DrawContent(GameTime gameTime)
    {
        if (!HealthQueries.TryGetTotals(healthPool, bodyParts, _entityId, out var currentHealth, out var maximumHealth) || maximumHealth <= 0)
        {
            return;
        }

        var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(statModifiers, _entityId, StatModifierTarget.MaximumHealth, maximumHealth);
        var healthFraction = effectiveMaximumHealth > 0
            ? MathHelper.Clamp(currentHealth / effectiveMaximumHealth, 0f, 1f)
            : 1f;

        var origin = ContentAbsolutePosition;
        var size = ContentSize;
        var bar = new Rectangle((int)origin.X, (int)origin.Y, (int)size.X, (int)size.Y);

        ResourceBarRenderer.Draw(ElementPoolService.SpriteBatch, ElementPoolService.UnitRectangle, bar, healthFraction, hasResource: true, HealthBarPalette.OutlineColor, HealthBarPalette.FractionColor);
    }
}
