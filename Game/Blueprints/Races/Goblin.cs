using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Energy.Components;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Race.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Races;

/// <summary>
/// Small, green and smart. Takes MathUtility by constructor injection rather than creating a
/// fresh, unseeded Random per Build call, which would be both wasteful and untestable.
/// </summary>
public sealed class Goblin(MathUtility mathUtility) : IBlueprint
{
    private static readonly Guid RaceId = new("1aa7b1c2-0b54-4745-b616-8aaff734a7d6");
    private const string RaceName = "Goblin";

    private static readonly string[] PersonalNameOptions = ["TestName1", "TestName2"];

    private const string Description = "Small, green and smart. What Goblins lack in physical strength they make up in pure spunk.";

    private const short MaximumEnergy = 100;
    private const short MinimumEnergyRecharge = 5;
    private const short MaximumEnergyRecharge = 10;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new RaceComponent(RaceId, RaceName, Description));

        var personalName = PersonalNameOptions[mathUtility.Next(0, PersonalNameOptions.Length)];
        componentManager.Merge(entityId, new DisplayTextComponent($"{personalName} : {RaceName}", Description));

        componentManager.Merge(entityId, new EnergyComponent(
            (short)mathUtility.Next(0, MaximumEnergy),
            (short)mathUtility.Next(MinimumEnergyRecharge, MaximumEnergyRecharge),
            MaximumEnergy));

        componentManager.Merge(entityId, new GlyphComponent("g", Color.DarkGreen, new Point(3, -2)));
        componentManager.Merge(entityId, new HealthComponent(100, 10, 200));
        componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, 40, null, null));

        componentManager.Merge(entityId, new TransformComponent(
            new Vector3Int(-1, -1, (int)MapHeight.Standing), new Vector3Byte(1, 1, 1)));
    }
}
