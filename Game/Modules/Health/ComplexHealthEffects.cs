using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Health.Components;

namespace Game.Modules.Health;

/// <summary>Write surface for granting a Complex entity's body parts -- mirrors AbilityScoreEffects/StatModifierEffects' static style.</summary>
/// <remarks>
/// Called at blueprint Build time by any race that wants ComplexHealth instead of
/// componentManager.Merge(entityId, new SimpleHealthComponent(...)) -- each part's starting health
/// is independently rolled between its own template's Minimum/MaximumHealth, mirroring how the old
/// flat SimpleHealthComponent roll worked before per-part splitting.
/// </remarks>
public static class ComplexHealthEffects
{
    public static void GrantBodyParts(ComponentManager componentManager, int entityId, MathUtility mathUtility, IReadOnlyList<BodyPartTemplate> parts)
    {
        var bodyParts = componentManager.GetMultiPool<BodyPartComponent>();
        for (var partId = 0; partId < parts.Count; partId++)
        {
            var part = parts[partId];
            var startingHealth = mathUtility.Next(part.MinimumHealth, part.MaximumHealth + 1);
            bodyParts.Add(entityId, new BodyPartComponent(part.Name, part.Type, (byte)partId, part.VerticalPosition, startingHealth, part.MaximumHealth, part.IsVital));
        }
    }
}
