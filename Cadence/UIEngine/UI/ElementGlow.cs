using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UIEngine.Rendering;

namespace UIEngine.UI;

/// <summary>One glow an element draws: whether it is on, its colour, mode and animation, and the clock its pulse runs on.</summary>
/// <remarks>Element holds two, the glow around its border and the overlay glow over its content, which differ only in where and when they are drawn.</remarks>
internal struct ElementGlow
{
    private GlowPulseClock _pulse;

    public bool IsGlowing { get; private set; }

    public Color Color { get; private set; }

    public GlowMode Mode { get; private set; }

    public GlowAnimation Animation { get; private set; }

    /// <summary>Turns the glow off and back to gold, steady, in mode, with no pulse running: what a pooled element starts each use with.</summary>
    public void Reset(GlowMode mode)
    {
        IsGlowing = false;
        Color = Color.Gold;
        Mode = mode;
        Animation = GlowAnimation.Static;
        _pulse.Reset();
    }

    /// <summary>Turns the glow on or off; gold when no colour is given.</summary>
    /// <remarks>A pulse starts when the glow turns on or its animation changes; setting the same glow again leaves it running.</remarks>
    public void Set(bool isGlowing, Color? color, GlowMode mode, GlowAnimation animation)
    {
        var restartsPulse = (isGlowing && !IsGlowing) || animation != Animation;
        IsGlowing = isGlowing;
        Color = color ?? Color.Gold;
        Mode = mode;
        Animation = animation;
        if (restartsPulse)
        {
            _pulse.Restart();
        }
    }

    /// <summary>Starts the pulse over, from the next draw.</summary>
    public void RestartPulse() => _pulse.Restart();

    /// <summary>Starts the pulse at presentationTime if it is waiting to start.</summary>
    public void StartPendingPulseAt(TimeSpan presentationTime) => _pulse.StartPendingAt(presentationTime);

    /// <summary>The glow's strength at presentationTime; full while its pulse waits to start.</summary>
    public readonly float StrengthAt(TimeSpan presentationTime) => Animation.StrengthAt(_pulse.ElapsedAt(presentationTime));

    /// <summary>Draws the glow on bounds if it is on, starting a pulse that was waiting.</summary>
    public void Draw<TTarget>(TTarget target, Rectangle bounds, TimeSpan presentationTime)
        where TTarget : ISpriteDrawTarget
    {
        if (!IsGlowing)
        {
            return;
        }

        StartPendingPulseAt(presentationTime);
        GlowRenderer.Draw(target, bounds, Color, Mode, StrengthAt(presentationTime));
    }
}
