using Game.Areas;
using Game.Bootstrap;

namespace Builders.MapAreas;

/// <summary>What AreaMapView draws: a preview build's session, the area instances stamped into it, and the one area the view is about, if any.</summary>
public interface IMapPreviewScene
{
    GameSession Session { get; }

    /// <summary>The instances stamped, their spawns and markers.</summary>
    AreaStampResult Stamp { get; }

    /// <summary>The area the view shows on its own -- its cells hatched where transparent, its grid drawn -- or null for a neighborhood, where every stamped instance is outlined instead.</summary>
    AreaInstance? Area { get; }
}
