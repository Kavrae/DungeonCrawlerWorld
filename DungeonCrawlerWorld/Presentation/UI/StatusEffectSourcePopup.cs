using Game.Modules.StatusEffects;
using Game.Views;
using Game.World;
using UIEngine.UI;

namespace Presentation.UI;

/// <summary>The hover popup for one status effect on an entity: the effect, how long it has left and what caused it -- the same wherever the effect is shown (a Health window row, a HUD icon).</summary>
internal static class StatusEffectSourcePopup
{
    /// <summary>Everything the popup is built from, as its cache key: the entity holding the effect, the source, the effect with no duration, the remaining duration as shown (whole seconds, so it is rebuilt once a second) and whether the source is the entity itself.</summary>
    public readonly record struct Key(int EntityId, ActionSource Source, HealthWindow.StatusEffectRow Effect, int? RemainingSeconds, bool IsOwnSource);

    /// <summary>The popup's key for effectType on entityId as of now. False when the effect isn't active or records no source.</summary>
    public static bool TryDescribe(StatusEffectDisplayRegistry statusEffectDisplays, ActionSourceNaming actionSourceNaming, int entityId, StatusEffectType effectType, long now, out Key key)
    {
        if (!statusEffectDisplays.TryGet(effectType, out var display)
            || display.GetStackCount(entityId) <= 0
            || display.GetSource(entityId) is not { } source)
        {
            key = default;
            return false;
        }

        var effect = HealthWindow.StatusEffectRowOf(display, effectType, entityId, now) with { RemainingSeconds = null };
        key = new Key(entityId, source, effect, ModifierDisplayFormatting.RemainingSecondsShown(display.GetRemainingDurationFrames(entityId, now)), actionSourceNaming.IsOwnSource(source, entityId));
        return true;
    }

    public static (string Body, IReadOnlyList<TooltipRow> Rows) Build(Key key, StatusEffectDisplayRegistry statusEffectDisplays, ActionSourceNaming actionSourceNaming) =>
        ModifierDisplayFormatting.SourcePopupShowingSeconds(actionSourceNaming, key.Source, key.EntityId, HealthWindow.FormatStatusEffectRow(key.Effect, statusEffectDisplays), key.RemainingSeconds);
}
