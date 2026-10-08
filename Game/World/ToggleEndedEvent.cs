namespace Game.World;

/// <summary>Why a toggle was switched off without its holder asking.</summary>
public enum ToggleEndReason : byte
{
    /// <summary>Its periodic effects could not be applied.</summary>
    PeriodicEffectsRefused,

    /// <summary>Its holder died.</summary>
    HolderDied,
}

/// <summary>A toggle on EntityId was switched off without its holder asking.</summary>
/// <param name="ToggleName">The name of the action or item that was on.</param>
public readonly record struct ToggleEndedEvent(int EntityId, string ToggleName, ToggleEndReason Reason);
