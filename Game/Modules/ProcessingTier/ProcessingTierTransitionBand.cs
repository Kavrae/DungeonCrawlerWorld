namespace Game.Modules.ProcessingTier;

/// <summary>Which of ProcessingTierTransitionQueue's bands a queued neighborhood drains in, in drain order.</summary>
public enum ProcessingTierTransitionBand : byte
{
    /// <summary>Becoming simulated: what the player is walking into.</summary>
    Thawing,

    /// <summary>Leaving simulation, or any other change that crosses the simulated boundary.</summary>
    Freezing,

    /// <summary>Unsimulated before and after (Borough and Beyond): nothing is visited either way, so it only has to be exact eventually.</summary>
    Unsimulated,
}
