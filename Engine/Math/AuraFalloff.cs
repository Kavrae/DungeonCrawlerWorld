namespace Engine.Math;

/// <summary>How an aura's value changes between its source and the edge of its size.</summary>
public enum AuraFalloff : byte
{
    /// <summary>Power at the source, falling in equal steps to about power / (size + 1) at the edge, rounded up so the edge is never 0.</summary>
    Linear,

    /// <summary>Full power at every distance up to the size.</summary>
    None,
}

public static class AuraFalloffExtensions
{
    /// <summary>The value at distance tiles from a source of this power and size: 0 past the size or for a negative distance.</summary>
    public static int ValueAt(this AuraFalloff falloff, int power, int size, int distance)
    {
        if (distance < 0 || distance > size || power <= 0)
        {
            return 0;
        }

        return falloff == AuraFalloff.None
            ? power
            : (power * (size + 1 - distance) + size) / (size + 1);
    }
}
