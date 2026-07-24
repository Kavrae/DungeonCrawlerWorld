using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Energy.Components;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Race.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Races;

/// <summary>Their magic is stored in their wings.</summary>
public sealed class Fairy(MathUtility mathUtility) : IBlueprint
{
    private static readonly Guid RaceId = new("c22f6339-0a56-4528-b818-10052a831dc5");
    private const string RaceName = "Fairy";

    private static readonly string[] PersonalNameOptions = ["Fairy1", "Fairy2"];

    private const string Description = "TODO fairy description. Their magic is stored in their wings.";

    private const short MaximumEnergy = 100;
    private const short MinimumEnergyRecharge = 10;
    private const short MaximumEnergyRecharge = 100;

    private const short MaximumHealth = 100;
    private const short HealthRegen = 1;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new RaceComponent(RaceId, RaceName, Description));

        var personalName = PersonalNameOptions[mathUtility.Next(0, PersonalNameOptions.Length)];
        componentManager.Merge(entityId, new DisplayTextComponent($"{personalName} : {RaceName}", Description));

        componentManager.Merge(entityId, new EnergyComponent(
            (short)mathUtility.Next(0, MaximumEnergy),
            (short)mathUtility.Next(MinimumEnergyRecharge, MaximumEnergyRecharge),
            MaximumEnergy));

        componentManager.Merge(entityId, new GlyphComponent("f", Color.DeepPink));
        componentManager.Merge(entityId, new HealthComponent((short)mathUtility.Next(1, MaximumHealth + 1), HealthRegen, MaximumHealth));
        componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, 15, null, null));

        componentManager.Merge(entityId, new TransformComponent(
            new Vector3Int(0, 0, (int)MapLayer.Flying), new Vector2Byte(1, 1)));
    }
}