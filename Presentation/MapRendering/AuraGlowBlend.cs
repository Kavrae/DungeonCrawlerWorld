using Game.Views;
using Microsoft.Xna.Framework;

namespace Presentation.MapRendering;

/// <summary>The glow auras cast on a map cell: the colours of the auras reaching it, blended by each one's power there.</summary>
/// <remarks>
/// BeginPass reads the auras in the field and their colours once, so a pass over many cells reads each aura's
/// definition once rather than once per cell. A definition replaced since is picked up by the next pass, which
/// AuraPowerView.Version changing is what starts.
/// </remarks>
public sealed class AuraGlowBlend(AuraPowerView auraPower)
{
    private readonly List<(byte AuraId, Color GlowColor)> _aurasInPass = [];

    /// <summary>Reads the auras that can reach anything, and their colours, for the TryGetGlow calls that follow.</summary>
    public void BeginPass()
    {
        _aurasInPass.Clear();

        foreach (var auraId in auraPower.AuraIdsInField)
        {
            _aurasInPass.Add((auraId, auraPower.GlowColorOf(auraId)));
        }
    }

    /// <summary>The glow at a cell: the colours of the auras reaching it, averaged by each one's power there, and their summed power.</summary>
    /// <remarks>False where no aura reaches. Two auras overlapping -- from one source or several -- blend toward whichever is stronger at the cell.</remarks>
    public bool TryGetGlow(int x, int y, int mapLayer, out Color glowColor, out int totalPower)
    {
        glowColor = default;
        totalPower = 0;

        if (!auraPower.AnyAuraReaches(x, y, mapLayer))
        {
            return false;
        }

        var red = 0f;
        var green = 0f;
        var blue = 0f;

        foreach (var (auraId, auraGlowColor) in _aurasInPass)
        {
            var power = auraPower.PowerAt(x, y, mapLayer, auraId);
            if (power <= 0)
            {
                continue;
            }

            red += auraGlowColor.R * power;
            green += auraGlowColor.G * power;
            blue += auraGlowColor.B * power;
            totalPower += power;
        }

        if (totalPower <= 0)
        {
            return false;
        }

        glowColor = new Color((byte)(red / totalPower), (byte)(green / totalPower), (byte)(blue / totalPower));
        return true;
    }
}
