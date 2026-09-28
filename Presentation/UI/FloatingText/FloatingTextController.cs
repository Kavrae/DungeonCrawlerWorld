using Engine.ECS.Systems;
using Engine.Events;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;

namespace Presentation.UI.FloatingText;

/// <summary>Turns FloatingTextEvents into the floating texts on screen: queues them per entity, releases them one at a time, and ages them out.</summary>
/// <remarks>
/// Each entity has two lanes (FloatingTextLayout.GetLane): numbers appear on the left half of the entity and statuses on
/// the right. Rising text appears at the top of the entity's footprint and falling text at the bottom. Each lane releases its oldest waiting text every ReleaseIntervalFrames, so simultaneous events fall in a
/// waterfall rather than drawing on top of each other. Released texts live in a ring buffer in the order they appeared; every text
/// has the same lifetime, so the oldest is always the next to expire, and reading the buffer from oldest to newest is the
/// draw order (newest on top).
///
/// Time is simulation frames, read from the clock on every Update: while the simulation is paused, texts neither move
/// nor fade, and nothing new is released.
///
/// A text is anchored where the event happened, not to its entity, so a queue outliving its entity (or an entity id
/// being reused) only continues a waterfall at the spot each event recorded.
/// </remarks>
public sealed class FloatingTextController
{
    private readonly SimulationClock _simulationClock;
    private readonly Random _random;
    private readonly Dictionary<(int EntityId, FloatingTextLane Lane), FloatingTextEntityQueue> _queuesByEntityLane = [];
    private readonly Stack<FloatingTextEntityQueue> _spareQueues = [];
    private readonly List<(int EntityId, FloatingTextLane Lane)> _drainedEntityLanes = [];
    private readonly FloatingTextInstance[] _activeTexts;
    private int _oldestActiveTextIndex;
    private int _activeTextCount;
    private long _lastSimulationFrame;

    public FloatingTextController(EventBus eventBus, SimulationClock simulationClock, Random? random = null)
    {
        _simulationClock = simulationClock;
        _random = random ?? new Random();
        _activeTexts = new FloatingTextInstance[FloatingTextChrome.MaxActiveTexts];
        _lastSimulationFrame = simulationClock.CurrentFrame;

        eventBus.Subscribe<FloatingTextEvent>(Enqueue);
    }

    public int ActiveTextCount => _activeTextCount;

    /// <summary>An active text by age order: 0 is the oldest.</summary>
    public ref readonly FloatingTextInstance GetActiveText(int indexFromOldest) =>
        ref _activeTexts[(_oldestActiveTextIndex + indexFromOldest) % _activeTexts.Length];

    public int GetWaitingCount(int entityId, FloatingTextLane lane = FloatingTextLane.Numbers) =>
        _queuesByEntityLane.TryGetValue((entityId, lane), out var queue) ? queue.WaitingCount : 0;

    public void Update()
    {
        var currentFrame = _simulationClock.CurrentFrame;
        var elapsedFrames = (int)(currentFrame - _lastSimulationFrame);
        _lastSimulationFrame = currentFrame;

        if (elapsedFrames <= 0)
        {
            return;
        }

        AgeActiveTexts(elapsedFrames);
        ReleaseWaitingTexts(elapsedFrames);
    }

    private void Enqueue(FloatingTextEvent text)
    {
        var entityLane = (text.EntityId, FloatingTextLayout.GetLane(text.Kind));
        if (!_queuesByEntityLane.TryGetValue(entityLane, out var queue))
        {
            queue = _spareQueues.Count > 0 ? _spareQueues.Pop() : new FloatingTextEntityQueue();
            _queuesByEntityLane.Add(entityLane, queue);
        }

        queue.Add(text, FloatingTextChrome.MaxBacklogPerEntity);
    }

    private void AgeActiveTexts(int elapsedFrames)
    {
        for (var index = 0; index < _activeTextCount; index++)
        {
            _activeTexts[(_oldestActiveTextIndex + index) % _activeTexts.Length].AgeFrames += elapsedFrames;
        }

        while (_activeTextCount > 0 && _activeTexts[_oldestActiveTextIndex].AgeFrames >= FloatingTextChrome.LifetimeFrames)
        {
            RetireOldestActiveText();
        }
    }

    private void ReleaseWaitingTexts(int elapsedFrames)
    {
        foreach (var (entityLane, queue) in _queuesByEntityLane)
        {
            while (queue.FramesUntilNextRelease <= 0 && queue.WaitingCount > 0)
            {
                Spawn(queue.TakeOldest());
                queue.FramesUntilNextRelease += FloatingTextChrome.ReleaseIntervalFrames;
            }

            queue.FramesUntilNextRelease -= elapsedFrames;

            if (queue.WaitingCount == 0 && queue.FramesUntilNextRelease <= 0)
            {
                _drainedEntityLanes.Add(entityLane);
            }
        }

        foreach (var entityLane in _drainedEntityLanes)
        {
            _queuesByEntityLane.Remove(entityLane, out var queue);
            queue!.Clear();
            _spareQueues.Push(queue);
        }

        _drainedEntityLanes.Clear();
    }

    private void Spawn(FloatingTextEvent text)
    {
        var footprintCenterX = text.Position.X + text.Size.X / 2f;
        var laneSide = FloatingTextLayout.GetLane(text.Kind) == FloatingTextLane.Numbers ? -1f : 1f;
        var jitterTiles = laneSide * (float)_random.NextDouble() * FloatingTextChrome.SpawnJitterTiles;
        var horizontalDirection = (sbyte)(_random.Next(2) == 0 ? -1 : 1);
        var verticalDirection = FloatingTextLayout.GetVerticalDirection(text.Kind);
        var spawnY = verticalDirection > 0 ? text.Position.Y + text.Size.Y : text.Position.Y;

        if (_activeTextCount == _activeTexts.Length)
        {
            RetireOldestActiveText();
        }

        _activeTexts[(_oldestActiveTextIndex + _activeTextCount) % _activeTexts.Length] = new FloatingTextInstance(
            new Vector2(footprintCenterX + jitterTiles, spawnY),
            text.Position.Z,
            text.Kind,
            text.Amount,
            text.EffectType,
            text.Flags,
            AgeFrames: 0,
            horizontalDirection,
            verticalDirection);
        _activeTextCount++;
    }

    private void RetireOldestActiveText()
    {
        _oldestActiveTextIndex = (_oldestActiveTextIndex + 1) % _activeTexts.Length;
        _activeTextCount--;
    }
}
