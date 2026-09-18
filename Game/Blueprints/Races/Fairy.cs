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
    private const ushort QuickAttackDamage = 3;

    /// <summary>Roughly double QuickAttackDamage, matching PowerAttackAction's own catalog ratio -- see TODO.md's Combat Overhaul: Dodge.</summary>
    private const ushort PowerAttackDamage = 6;

    /// <summary>Flat default for every NPC race, adjustable in a later balance pass -- see TODO.md's Stats entry.</summary>
    private const ushort DefaultAbilityScoreBaseValue = 5;

    /// <summary>One override shared by every creature of this race: an ActionDefinition is never changed in place, only replaced, and building it per creature was most of what a creature allocated.</summary>
    private static readonly ActionDefinition QuickAttackOverride = ActionOverrideEffects.OverrideFlatDamage(QuickAttackAction.Build(), QuickAttackDamage);

    /// <inheritdoc cref="QuickAttackOverride"/>
    private static readonly ActionDefinition PowerAttackOverride = ActionOverrideEffects.OverrideFlatDamage(PowerAttackAction.Build(), PowerAttackDamage);

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new RaceComponent(RaceId, RaceName, Description));

        componentManager.Merge(entityId, new DisplayTextComponent(DisplayNames[mathUtility.Next(0, DisplayNames.Length)], Description));

        componentManager.Merge(entityId, new GlyphComponent("f", Color.DeepPink));
        componentManager.Merge(entityId, new SimpleHealthComponent(MaximumHealth, MaximumHealth));
        componentManager.Merge(entityId, new MovementComponent(MovementMode.Random, null, null));
        componentManager.Merge(entityId, new ActionLockComponent(standardLockFrames: 48, currentLockTotalFrames: 0, unlockedAtFrame: 0));

        componentManager.Merge(entityId, new TransformComponent(TransformComponent.UnplacedOn(MapLayer.Flying), new Vector2Byte(1, 1)));

        componentManager.Merge(entityId, new ActionInstanceComponent(QuickAttackAction.Id, QuickAttackOverride));
        componentManager.Merge(entityId, new ActionInstanceComponent(PowerAttackAction.Id, PowerAttackOverride));

        componentManager.Merge(entityId, new ActionInstanceComponent(DodgeAction.Id, overrideDefinition: null));

        TemporaryNpcLootGrant.GrantRandomStartingLoot(componentManager, entityId, mathUtility);
        StartingCurrencyGrant.GrantRandomStartingGoldAndCredits(componentManager, entityId, mathUtility);

        AbilityScoreEffects.GrantDefaults(componentManager, entityId, DefaultAbilityScoreBaseValue);
    }
}