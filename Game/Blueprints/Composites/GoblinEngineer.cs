using Engine.ECS.Systems;
using Game.Blueprints.Classes;
using Game.Blueprints.Races;
using Game.Modules.StatModifiers;
using Game.World;

namespace Game.Blueprints.Composites;

/// <summary>
/// What makes a goblin engineer more than a goblin with the Engineer class: its own description,
/// and a further action-lock reduction on top of Engineer's own.
/// </summary>
/// <remarks>Its ActionLockFrames modifier adds to Engineer's rather than compounding with it, as every multiplicative modifier does. Its name is the one Goblin and Engineer compose -- a goblin's name followed by "Engineer".</remarks>
public static class GoblinEngineer
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000106");

    public const string Name = "Goblin Engineer";

    private const string Description = "Engineers. The incels of the goblin world. They have a hard time finding a date, which makes them extra angry. If there are any females in you party, they will attack them first.";

    /// <summary>The ActionLockFrames multiplier a goblin engineer grants on top of Engineer's.</summary>
    private const float ActionLockMultiplier = -0.1f;

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Includes = [Goblin.Id, Engineer.Id],
        Build = Build,
        Appearance = new() { Description = Description }
    };

    private static void Build(BlueprintContext context) =>
        StatModifierEffects.Apply(context.ComponentManager, context.EntityId, StatModifierTarget.ActionLockFrames, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: false, magnitude: ActionLockMultiplier, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin);
}
