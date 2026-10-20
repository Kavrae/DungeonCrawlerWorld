namespace Engine.Bootstrap;

/// <summary>What a build is for: only a Session build may be simulated.</summary>
/// <cleanupVersion>1</cleanupVersion>
public enum EcsBuildPurpose : byte
{
    /// <summary>A build a host may simulate (EcsContext.BeginSession), or read as a whole game.</summary>
    Session,

    /// <summary>A build made only to see whether it builds -- a mod's dry run.</summary>
    Trial,

    /// <summary>A build whose pools are written outside any simulation -- a world entities are rebuilt in to be read.</summary>
    Staging,
}
