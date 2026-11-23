using Engine.ECS.Entities;
using Engine.ECS.Properties;
using Engine.Math;
using Engine.Tags;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Spawning;
using Game.World;

namespace Tests.GameTests.ComponentProperties;

/// <summary>One value of each of five components chosen to exercise every shape -- optional nested values, a definition, a hand-written nested source, a readonly record struct, flags, keys -- varied by <c>i</c>.</summary>
internal static class ComponentPropertySamples
{
    private static readonly Guid ItemId = new("b0a0c2a1-0000-4000-8000-000000000001");

    public static MovementComponent Movement(int i) =>
        new(i % 3 == 0 ? null : new Vector3Int(i, i + 1, 0), new Vector3Int(i + 2, i, 0));

    public static InventoryItemStackComponent Stack(int i) => new(ItemId, (ushort)(1 + i % 20), cooldownReadyAtFrame: (uint)(1000 + i));

    public static DeadComponent Dead(int i) => new(ActionSource.FromTerrain((ushort)(i % 7)), 5000 + i, new EntityKey((ulong)(i + 1)));

    public static SpawnRecordComponent Spawn(int i) => new((ushort)(i % 400), i % 2 == 0 ? SpawnFlags.Crawler : SpawnFlags.None, (uint)(i * 7919));

    public static PendingWindupComponent Windup(int i) => PendingWindupComponent.ForAction(
        TestContent.Action(BuiltInActionIds.MagicMissile),
        new TargetSelection(TargetingMode.Target, new Vector3Int(i, i, 0), new EntityKey((ulong)i + 3), new Vector2Byte(1, 0), 6, 1),
        (uint)(2000 + i)) with { ActivatedFromSlot = i % 2 == 0 ? HotkeySlot.Base2 : null };

    public static BodyPartStateComponent BodyParts(int i)
    {
        var state = new BodyPartStateComponent();
        for (var part = 0; part < 6; part++)
        {
            state.SetCurrentHealth(part, 10 + part + i % 5);
            state.SetRegenLockedUntilFrame(part, (uint)(i + part));
            state.SetDisabled(part, (i + part) % 4 == 0);
        }

        return state;
    }
}

/// <summary>Every leaf a traversal reaches, with the path that writes it back and the value as it would be written; and each instance's version, for a pool's read.</summary>
internal sealed class RecordingVisitor : ComponentPropertyVisitor
{
    private readonly List<int> _path = [];
    private readonly Stack<int> _nextElement = new();
    private readonly Stack<bool> _readOnlyLevels = new();

    /// <summary>Each leaf, and whether the table should take it written back: neither it nor any level above it is read-only.</summary>
    public List<(int Instance, int[] Path, ComponentPropertyDescriptor Property, ComponentPropertyValue Value, bool IsWritable)> Leaves { get; } = [];

    public List<uint> InstanceVersions { get; } = [];

    private int _instance;

    public override void BeginInstance(ComponentPropertyTable table, int instance, uint version)
    {
        _instance = instance;
        InstanceVersions.Add(version);
    }

    public override void Integer(ComponentPropertyDescriptor property, long value) => Add(property, ComponentPropertyValue.FromInteger(value));

    public override void Number(ComponentPropertyDescriptor property, double value) => Add(property, ComponentPropertyValue.FromNumber(value));

    public override void Boolean(ComponentPropertyDescriptor property, bool value) => Add(property, ComponentPropertyValue.FromBoolean(value));

    public override void Text(ComponentPropertyDescriptor property, string? value) => Add(property, ComponentPropertyValue.FromText(value));

    public override void Enum(ComponentPropertyDescriptor property, long value) => Add(property, ComponentPropertyValue.FromEnum(value));

    public override void Guid(ComponentPropertyDescriptor property, Guid value) => Add(property, ComponentPropertyValue.FromGuid(value));

    public override void Color(ComponentPropertyDescriptor property, uint packedValue) => Add(property, ComponentPropertyValue.FromColor(packedValue));

    public override void Tag(ComponentPropertyDescriptor property, GameplayTag value) => Add(property, ComponentPropertyValue.FromTag(value.IsNone ? "" : value.Name));

    public override void TagQuery(ComponentPropertyDescriptor property, GameplayTagQuery? value) => Add(property, ComponentPropertyValue.FromText(value?.ToString()));

    public override void Definition(ComponentPropertyDescriptor property, object? value) => Add(property, ComponentPropertyValue.FromText(value?.ToString()));

    public override void Absent(ComponentPropertyDescriptor property) => Add(property, ComponentPropertyValue.Absent);

    public override void BeginNested(ComponentPropertyDescriptor property) => Begin(property, -1);

    public override void EndNested(ComponentPropertyDescriptor property) => End();

    public override void BeginIndexed(ComponentPropertyDescriptor property, int count) => Begin(property, 0);

    public override void EndIndexed(ComponentPropertyDescriptor property) => End();

    private void Begin(ComponentPropertyDescriptor property, int firstElement)
    {
        _path.Add(property.Index);
        _nextElement.Push(firstElement);
        _readOnlyLevels.Push(property.IsReadOnly);
    }

    private void End()
    {
        _readOnlyLevels.Pop();
        _nextElement.Pop();
        _path.RemoveAt(_path.Count - 1);
    }

    private void Add(ComponentPropertyDescriptor property, ComponentPropertyValue value)
    {
        var path = new List<int>(_path);
        if (_nextElement.Count > 0 && _nextElement.Peek() >= 0)
        {
            var element = _nextElement.Pop();
            path.Add(element);
            _nextElement.Push(element + 1);
        }
        else
        {
            path.Add(property.Index);
        }

        Leaves.Add((_instance, path.ToArray(), property, value, !property.IsReadOnly && !_readOnlyLevels.Contains(true)));
    }
}
