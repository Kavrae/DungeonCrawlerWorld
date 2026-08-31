using Engine.ECS.Components;
using Engine.Math;
using Game.Blueprints.NPCs;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Race.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Races;

/// <summary>
/// A test fixture race for exercising melee status effects, deliberately with no
/// SimpleHealthComponent.
/// </summary>
public sealed class Ghost(MathUtility mathUtility) : IBlueprint
{
    private static readonly Guid RaceId = new("7e6d6a3a-6b8f-4f0a-9f2a-7c9b1e6f2a3d");
    private const string RaceName = "Ghost";

    private static readonly string[] PersonalNameOptions = ["Ghost1", "Ghost2"];

    private const string Description = "A wandering spirit with no physical form. Used to test melee status effects against a target with no Health to damage.";

    private static readonly string[] DisplayNames = DisplayNameCache.BuildDisplayNames(PersonalNameOptions, RaceName);

    /// <summary>Hardcoded stopgap until the Additive/Multiplicative bonuses system exists -- see TODO.md.</summary>
    private const ushort PunchDamage = 5;

    /// <summary>Flat default for every NPC race, adjustable in a later balance pass -- see TODO.md's Stats entry.</summary>
    private const ushort DefaultAbilityScoreBaseValue = 5;

    /// <summary>ᗣ (U+15A3, Canadian Aboriginal Syllabics). Requires Symbola-Emoji.ttf loaded as a fallback font (see FontService)</summary>
    private const string Glyph = "G";

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new RaceComponent(RaceId, RaceName, Description));

        componentManager.Merge(entityId, new DisplayTextComponent(DisplayNames[mathUtility.Next(0, DisplayNames.Length)], Description));

        componentManager.Merge(entityId, new GlyphComponent(Glyph, Color.Blue));
        componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, null, null));
        componentManager.Merge(entityId, new ActionLockComponent(standardLockFrames: 48, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(entityId, new TransformComponent(
    new Vector3Int(0, 0, (int)MapLayer.Ground), new Vector2Byte(1, 1)));

        componentManager.Merge(entityId, new NonBlockingComponent(NonBlockingKind.Phasing));
        var punchOverride = ActionOverrideEffects.OverrideFlatDamage(PunchAction.Build(), PunchDamage);
        componentManager.Merge(entityId, new ActionInstanceComponent(PunchAction.Id, punchOverride, cooldownFramesRemaining: 0));

        TemporaryNpcLootGrant.GrantRandomStartingLoot(componentManager, entityId, mathUtility);

        AbilityScoreEffects.GrantDefaults(componentManager, entityId, DefaultAbilityScoreBaseValue);
    }
}
