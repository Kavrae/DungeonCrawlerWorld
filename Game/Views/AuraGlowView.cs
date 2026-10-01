using Engine.Math;
using Game.Modules.Auras;
using Microsoft.Xna.Framework;

namespace Game.Views;

/// <summary>The glow auras cast on the map, read from the same field gameplay applies them from.</summary>
public sealed class AuraGlowView(AuraField auraField)
{
    /// <summary>Changes whenever any aura source is added, removed or moved: a cached rendering of the glow is stale once this differs from the value it was made at.</summary>
    public int Version => auraField.Version;

    /// <inheritdoc cref="AuraField.TryGetGlow"/>
    public bool TryGetGlow(int x, int y, int mapLayer, out Color glowColor, out int totalStrength) =>
        auraField.TryGetGlow(new Vector3Int(x, y, mapLayer), out glowColor, out totalStrength);
}
