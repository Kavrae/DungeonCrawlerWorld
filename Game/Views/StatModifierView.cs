using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Views;

/// <summary>The stat modifiers acting on an entity, and the values they make effective.</summary>
public sealed class StatModifierView(ComponentManager componentManager)
{
    private readonly MultiComponentPool<StatModifierComponent> _statModifiers = componentManager.GetMultiPool<StatModifierComponent>();

    /// <summary>Replaces destination's contents with every modifier on entityId.</summary>
    public void CopyStatModifiers(int entityId, List<StatModifierComponent> destination)
    {
        destination.Clear();
        for (var denseIndex = _statModifiers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _statModifiers.GetNextDenseIndex(denseIndex))
        {
            destination.Add(_statModifiers.GetReadonlyByDenseIndex(denseIndex));
        }
    }

    /// <summary>Changes whenever a modifier on entityId is added, updated or removed.</summary>
    public uint GetVersion(int entityId) => _statModifiers.GetEntityVersion(entityId);

    /// <inheritdoc cref="StatModifierMath.GetEffectiveValue"/>
    public float GetEffectiveValue(int entityId, StatModifierTarget target, float baseValue, IReadOnlyList<Tag>? activeTags = null) =>
        StatModifierMath.GetEffectiveValue(_statModifiers, entityId, target, baseValue, activeTags);
}
