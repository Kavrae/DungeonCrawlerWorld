namespace Game.World;

/// <summary>
/// Published by ScrollMasteryEffects.RecordUsage the moment a scroll's SpellId crosses
/// ScrollMasteryEffects.MasteryThreshold uses -- fires once per spell mastered (mastering two
/// different scrolls fires this twice, independently). The spell itself has already been granted
/// by the time this fires; consumers (MostBoringLibrarianAchievement) are purely observational.
/// </summary>
public readonly record struct ScrollMasteredEvent(int EntityId, Guid SpellId);
