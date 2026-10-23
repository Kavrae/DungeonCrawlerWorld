using Game.Views;
using Microsoft.Xna.Framework;

namespace Presentation.MapRendering;

/// <summary>The glow auras cast on a map cell: the colours of the auras reaching it, blended by each one's power there, and how opaque their glow is.</summary>
/// <remarks>
/// BeginPass reads the auras in the field, their colours and their glow opacity multipliers once, so a pass over many cells reads each
/// aura's definition once rather than once per cell. A definition replaced since is picked up by the next pass, which
/// AuraPowerView.Version changing is what starts.
/// </remarks>
public sealed class AuraGlowBlend(AuraPowerView auraPower)
{
    private readonly List<(byte AuraId, Color GlowColor, float GlowOpacityMultiplier)> _aurasInPass = [];

    /// <summary>Reads the auras that can reach anything, their colours and their glow opacity multipliers, for the TryGetGlow calls that follow.</summary>
    public void BeginPass()
    {
        _aurasInPass.Clear();

        foreach (var auraId in auraPower.AuraIdsInField)
        {
            _aurasInPass.Add((auraId, auraPower.GlowColorOf(auraId), Math.Clamp(auraPower.GlowOpacityMultiplierOf(auraId), 0f, 1f)));
        }
    }

    /// <summary>The glow at a cell: the colours of the auras reaching it and their glow opacity multipliers, each averaged by the aura's power there, and their summed power.</summary>
    /// <remarks>False where no aura reaches. Two auras overlapping -- from one source or several -- blend toward whichever is stronger at the cell.</remarks>
    public bool TryGetGlow(int x, int y, int mapLayer, out Color glowColor, out int totalPower, out float glowOpacityMultiplier)
    {
        glowColor = default;
        totalPower = 0;
        glowOpacityMultiplier = 0f;

        if (!auraPower.AnyAuraReaches(x, y, mapLayer))
        {
            return false;
        }

        var red = 0f;
        var green = 0f;
        var blue = 0f;
        var opacityMultiplier = 0f;

        foreach (var (auraId, auraGlowColor, auraGlowOpacityMultiplier) in _aurasInPass)
        {
            var power = auraPower.PowerAt(x, y, mapLayer, auraId);
            if (power <= 0)
            {
                continue;
            }

            red += auraGlowColor.R * power;
            green += auraGlowColor.G * power;
            blue += auraGlowColor.B * power;
            opacityMultiplier += auraGlowOpacityMultiplier * power;
            totalPower += power;
        }

        if (totalPower <= 0)
        {
            return false;
        }

        glowColor = new Color((byte)(red / totalPower), (byte)(green / totalPower), (byte)(blue / totalPower));
        glowOpacityMultiplier = opacityMultiplier / totalPower;
        return true;
    }
}
