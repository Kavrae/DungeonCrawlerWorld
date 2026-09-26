using Game.Modules.Movement.Components;

namespace Game.Blueprints.Parts;

/// <summary>Takes away whatever movement an entity's earlier parts gave it, leaving it where it was placed.</summary>
/// <remarks>
/// A part rather than a call at the spawner: an entity that is stationary by what it is stays stationary
/// when its spawn record is rebuilt (see EntityFactory). Listed after the parts whose MovementComponent
/// it removes -- a hybrid of two races with no single coherent movement mode, or a fixture that should
/// hold its cell despite its race's own wandering baseline.
/// </remarks>
public static class StationaryPart
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000107");

    public const string Name = "Stationary";

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = Build
    };

    private static void Build(BlueprintContext context) =>
        context.ComponentManager.GetPackedPool<MovementComponent>().Remove(context.EntityId);
}
