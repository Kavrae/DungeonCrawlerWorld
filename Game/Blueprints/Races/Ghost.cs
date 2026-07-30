using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.Modules.Melee;
using Game.Modules.Movement.Components;
using Game.Modules.Race.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Races;

/// <summary>
/// A test fixture race for exercising melee status effects, deliberately with no
/// HealthComponent.
/// </summary>
public sealed class Ghost(MathUtility mathUtility) : IBlueprint
{
    private static readonly Guid RaceId = new("7e6d6a3a-6b8f-4f0a-9f2a-7c9b1e6f2a3d");
    private const string RaceName = "Ghost";

    private static readonly string[] PersonalNameOptions = ["Ghost1", "Ghost2"];

    private const string Description = "A wandering spirit with no physical form. Used to test melee status effects against a target with no Health to damage.";

    /// <summary>Hardcoded stopgap until the Additive/Multiplicative bonuses system exists -- see TODO.md.</summary>
    private const short DefaultAttackDamage = 5;

    /// <summary>ᗣ (U+15A3, Canadian Aboriginal Syllabics). Requires Symbola-Emoji.ttf loaded as a fallback font (see FontService)</summary>
    private const string Glyph = "ᗣ";

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new RaceComponent(RaceId, RaceName, Description));

        var personalName = PersonalNameOptions[mathUtility.Next(0, PersonalNameOptions.Length)];
        componentManager.Merge(entityId, new DisplayTextComponent($"{personalName} : {RaceName}", Description));

        componentManager.Merge(entityId, new GlyphComponent(Glyph, Color.Blue));
        componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, 48, null, null));
        componentManager.Merge(entityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        componentManager.Merge(entityId, new TransformComponent(
    new Vector3Int(0, 0, (int)MapLayer.Ground), new Vector2Byte(1, 1)));

        componentManager.Merge(entityId, new OccupancyComponent(isTiny: false, isPhasing: true));
        componentManager.Merge(entityId, new AbilityInstanceComponent(MeleeModule.DefaultAttackId, damageAmount: DefaultAttackDamage, cooldownFramesRemaining: 0));
    }
}
