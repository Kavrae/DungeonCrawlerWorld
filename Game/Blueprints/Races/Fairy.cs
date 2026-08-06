using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
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

    private const short MaximumHealth = 100;
    private const short HealthRegen = 1;

    /// <summary>Hardcoded stopgap until the Additive/Multiplicative bonuses system exists -- see TODO.md.</summary>
    private const short PunchDamage = 3;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new RaceComponent(RaceId, RaceName, Description));

        var personalName = PersonalNameOptions[mathUtility.Next(0, PersonalNameOptions.Length)];
        componentManager.Merge(entityId, new DisplayTextComponent($"{personalName} : {RaceName}", Description));

        componentManager.Merge(entityId, new GlyphComponent("f", Color.DeepPink));
        componentManager.Merge(entityId, new HealthComponent((short)mathUtility.Next(1, MaximumHealth + 1), HealthRegen, MaximumHealth));
        componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, 48, null, null));
        componentManager.Merge(entityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));

        componentManager.Merge(entityId, new TransformComponent(
            new Vector3Int(0, 0, (int)MapLayer.Flying), new Vector2Byte(1, 1)));

        componentManager.Merge(entityId, new AbilityInstanceComponent(CoreAbilitiesModule.PunchId, damageAmount: PunchDamage, cooldownFramesRemaining: 0));
    }
}