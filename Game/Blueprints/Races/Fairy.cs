using Engine.ECS.Components;
using Engine.Math;
using Game.Blueprints.NPCs;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Race.Components;
using Microsoft.Xna.Framework;

namespace Game.Blueprints.Races;

/// <summary>Their magic is stored in their wings.</summary>
public sealed class Fairy(MathUtility mathUtility) : IBlueprint
{
    public static readonly Guid RaceId = new("c22f6339-0a56-4528-b818-10052a831dc5");
    private const string RaceName = "Fairy";

    private static readonly string[] PersonalNameOptions = ["Fairy1", "Fairy2"];

    private const string Description = "TODO fairy description. Their magic is stored in their wings.";

    private static readonly string[] DisplayNames = DisplayNameCache.BuildDisplayNames(PersonalNameOptions, RaceName);

    private const ushort MaximumHealth = 100;

    /// <summary>Hardcoded stopgap until the Additive/Multiplicative bonuses system exists -- see TODO.md.</summary>
    private const ushort PunchDamage = 3;

    /// <summary>Flat default for every NPC race, adjustable in a later balance pass -- see TODO.md's Stats entry.</summary>
    private const ushort DefaultAbilityScoreBaseValue = 5;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new RaceComponent(RaceId, RaceName, Description));

        componentManager.Merge(entityId, new DisplayTextComponent(DisplayNames[mathUtility.Next(0, DisplayNames.Length)], Description));

        componentManager.Merge(entityId, new GlyphComponent("f", Color.DeepPink));
        componentManager.Merge(entityId, new SimpleHealthComponent((ushort)mathUtility.Next(1, MaximumHealth + 1), MaximumHealth));
        componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, null, null));
        componentManager.Merge(entityId, new ActionLockComponent(standardLockFrames: 48, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));

        componentManager.Merge(entityId, new TransformComponent(
            new Vector3Int(0, 0, (int)MapLayer.Flying), new Vector2Byte(1, 1)));

        var punchOverride = ActionOverrideEffects.OverrideFlatDamage(PunchAction.Build(), PunchDamage);
        componentManager.Merge(entityId, new ActionInstanceComponent(PunchAction.Id, punchOverride, cooldownFramesRemaining: 0));

        TemporaryNpcLootGrant.GrantRandomStartingLoot(componentManager, entityId, mathUtility);
        StartingCurrencyGrant.GrantRandomStartingGoldAndCredits(componentManager, entityId, mathUtility);

        AbilityScoreEffects.GrantDefaults(componentManager, entityId, DefaultAbilityScoreBaseValue);
    }
}