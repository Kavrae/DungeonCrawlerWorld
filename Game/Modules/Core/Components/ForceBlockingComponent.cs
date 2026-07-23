namespace Game.Modules.Core.Components;

/// <summary>
/// One instance = one independent source (e.g. an effect forcing a normally-incorporeal
/// entity solid) currently overriding its entity to be blocking. Takes precedence over
/// NonBlockingComponent when both are present -- see IMapQuery.IsBlocking. Multi-pooled for
/// the same overlapping-sources reason as NonBlockingComponent.
/// </summary>
public struct ForceBlockingComponent;