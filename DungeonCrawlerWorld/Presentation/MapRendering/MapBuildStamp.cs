namespace Presentation.MapRendering;

/// <summary>What something built for the map view was last built for -- a grid and a version of what it read -- so its owner can tell when it is stale.</summary>
/// <remarks>Stale before the first build, for any other grid or version, and once told (Invalidate) of a change neither can see.</remarks>
public sealed class MapBuildStamp
{
    private MapImageKey _builtKey;
    private int _builtVersion;
    private bool _isBuilt;

    /// <summary>Whether the last build was for key and version and nothing has invalidated it since.</summary>
    public bool IsCurrentFor(MapImageKey key, int version = 0) => _isBuilt && key == _builtKey && version == _builtVersion;

    /// <summary>Records a build for key and version.</summary>
    public void Record(MapImageKey key, int version = 0)
    {
        _builtKey = key;
        _builtVersion = version;
        _isBuilt = true;
    }

    /// <summary>Marks the last build stale.</summary>
    public void Invalidate() => _isBuilt = false;
}
