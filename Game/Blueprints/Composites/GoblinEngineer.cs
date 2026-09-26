using Engine.Math;
using Game.Blueprints.Classes;
using Game.Blueprints.Races;
using Game.Modules.Core.Components;

namespace Game.Blueprints.Composites;

/// <summary>
/// What makes a goblin engineer more than a goblin with the Engineer class: its own description,
/// and a further action-cooldown reduction on top of Engineer's own.
/// </summary>
/// <remarks>Its build step runs after the Goblin and Engineer it includes, so the lock it shortens is already Engineer's. Its name is the one Goblin and Engineer compose -- a goblin's name followed by "Engineer".</remarks>
public static class GoblinEngineer
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000106");

    public const string Name = "Goblin Engineer";

    private const string Description = "Engineers. The incels of the goblin world. They have a hard time finding a date, which makes them extra angry. If there are any females in you party, they will attack them first.";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Includes = [Goblin.Id, Engineer.Id],
        Build = Build,
        Appearance = new() { Description = Description }
    };

    private static void Build(BlueprintContext context)
    {
        context.ComponentManager.TryUpdate(context.EntityId, static (ref ActionLockComponent actionLock) =>
        {
            actionLock.StandardLockFrames = MathUtility.ClampUShort((ushort)(actionLock.StandardLockFrames * 0.9m), 1, ushort.MaxValue);
        });
    }
}
