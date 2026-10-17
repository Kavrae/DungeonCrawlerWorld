using Game.World;

namespace Presentation.UI.FloatingText;

/// <summary>One entity's floating texts in one lane, waiting their turn to appear, oldest first.</summary>
/// <remarks>Past maxBacklog waiting texts, a new text is added into the newest waiting one of the same kind, effect and flags instead of queued, so the totals stay exact and the queue can't fall far behind what is happening.</remarks>
internal sealed class FloatingTextEntityQueue
{
    private readonly List<FloatingTextEvent> _waitingTexts = [];

    /// <summary>Frames left before the next waiting text may appear; at or below zero, the next one appears on the next update that advances time.</summary>
    public int FramesUntilNextRelease { get; set; }

    public int WaitingCount => _waitingTexts.Count;

    public void Add(FloatingTextEvent text, int maxBacklog)
    {
        if (_waitingTexts.Count >= maxBacklog)
        {
            for (var index = _waitingTexts.Count - 1; index >= 0; index--)
            {
                var waiting = _waitingTexts[index];
                if (waiting.Kind == text.Kind && waiting.EffectType == text.EffectType && waiting.Flags == text.Flags)
                {
                    _waitingTexts[index] = text with { Amount = (ushort)System.Math.Min(waiting.Amount + text.Amount, ushort.MaxValue) };
                    return;
                }
            }
        }

        _waitingTexts.Add(text);
    }

    public FloatingTextEvent TakeOldest()
    {
        var oldest = _waitingTexts[0];
        _waitingTexts.RemoveAt(0);
        return oldest;
    }

    public void Clear()
    {
        _waitingTexts.Clear();
        FramesUntilNextRelease = 0;
    }
}
