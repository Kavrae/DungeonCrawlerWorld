using Engine.ECS.Components;
using Game.Modules.Health.Components;

namespace Game.Modules.Health;

/// <summary>Write surface for granting a Complex entity's body parts -- mirrors AbilityScoreEffects/StatModifierEffects' static style.</summary>
/// <remarks>
/// Called at blueprint Build time by any race that wants ComplexHealth instead of
/// componentManager.Merge(entityId, new SimpleHealthComponent(...)). Every part starts at its
/// template's full health, the same rule the Simple path follows.
/// </remarks>
public static class ComplexHealthEffects
{
    public static void GrantBodyParts(ComponentManager componentManager, int entityId, IReadOnlyList<BodyPartTemplate> parts)
    {
        var bodyParts = componentManager.GetMultiPool<BodyPartComponent>();
        for (var partId = 0; partId < parts.Count; partId++)
        {
            var part = parts[partId];
            bodyParts.Add(entityId, new BodyPartComponent(part.Name, part.Type, (byte)partId, part.VerticalPosition, part.MaximumHealth, part.MaximumHealth, part.IsVital));
        }
    }
}
