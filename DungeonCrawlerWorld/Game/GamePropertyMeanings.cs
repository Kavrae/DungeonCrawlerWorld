namespace Game;

/// <summary>The meanings the game names for its components' properties, beside the engine's (ComponentPropertyMeaningNames); each is resolved for display, choices and saves by what it names.</summary>
/// <remarks>In the Game namespace so every component under it names them without a using.</remarks>
public static class GamePropertyMeanings
{
    /// <summary>A session-local blueprint id (BlueprintRegistry), saved as its Guid.</summary>
    public const string BlueprintId = nameof(BlueprintId);

    /// <summary>A session-local aura id (AuraCatalog), saved as its Guid.</summary>
    public const string AuraId = nameof(AuraId);

    /// <summary>A session-local item handle (ItemIds), saved as its Guid.</summary>
    public const string ItemId = nameof(ItemId);

    /// <summary>An item definition's Guid.</summary>
    public const string ItemDefinitionId = nameof(ItemDefinitionId);

    /// <summary>An action definition's Guid.</summary>
    public const string ActionId = nameof(ActionId);

    /// <summary>An achievement's Guid.</summary>
    public const string AchievementId = nameof(AchievementId);

    /// <summary>A session-local terrain type id (TerrainRegistry), saved as its key.</summary>
    public const string TerrainTypeId = nameof(TerrainTypeId);

    /// <summary>A body part's id, named by the entity's race.</summary>
    public const string BodyPartId = nameof(BodyPartId);

    /// <summary>An ability score, by AbilityScoreType.</summary>
    public const string AbilityScoreType = nameof(AbilityScoreType);

    /// <summary>A session-local behavior set id, saved as what it was built from.</summary>
    public const string BehaviorSetId = nameof(BehaviorSetId);

    /// <summary>An item stack's instance id: session state, remapped when saved.</summary>
    public const string StackInstanceId = nameof(StackInstanceId);

    /// <summary>The key a toggle holds its effects under: session state, remapped when saved.</summary>
    public const string HeldGrantKey = nameof(HeldGrantKey);

    /// <summary>A sprite manifest name.</summary>
    public const string SpriteName = nameof(SpriteName);

    /// <summary>A UTC time in ticks, shown as a date.</summary>
    public const string UtcTicks = nameof(UtcTicks);

    /// <summary>A floor number.</summary>
    public const string Floor = nameof(Floor);
}
