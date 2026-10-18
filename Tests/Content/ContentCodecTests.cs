using System.Text;
using System.Text.Json;
using Engine.Content;
using Game.Bootstrap;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Game.Modules.Inventory;

namespace Tests.Content;

/// <summary>Every built-in action, item and aura survives being written and read back, and the format refuses what it can't mean.</summary>
[TestClass]
public sealed class ContentCodecTests
{
    private static readonly Lazy<GameBuildPassResult> Build = new(() => BuiltInTestModules.Build(GameBuildPass.CreatePlaceholderMap()));

    private static ContentTypeRegistry Types => Build.Value.Context.ContentTypes;

    private sealed class AuraReferences(IEnumerable<AuraDefinition> auras) : IContentReferences
    {
        private readonly Dictionary<Guid, AuraDefinition> _aurasById = auras.ToDictionary(aura => aura.Id);

        public bool TryResolve<T>(Guid id, out T value)
            where T : class
        {
            if (typeof(T) == typeof(AuraDefinition) && _aurasById.TryGetValue(id, out var aura))
            {
                value = (T)(object)aura;
                return true;
            }

            value = null!;
            return false;
        }
    }

    private static List<AuraDefinition> AllAuras()
    {
        var auras = Build.Value.Context.Auras;
        return [.. Enumerable.Range(0, auras.Count).Select(auraId => auras.Get((byte)auraId))];
    }

    private static IContentReferences References { get; } = new AuraReferences(AllAuras());

    private static T ReadBack<T>(byte[] bytes)
        where T : notnull
    {
        using var document = JsonDocument.Parse(bytes);
        return ContentObject.Read<T>(document.RootElement, Types, References);
    }

    private static void AssertRoundTrips<T>(IEnumerable<T> values, Func<T, string> nameOf)
        where T : notnull
    {
        var checkedAny = false;
        foreach (var value in values)
        {
            checkedAny = true;
            var written = ContentWriter.WriteRecord(Types, value);
            var rewritten = ContentWriter.WriteRecord(Types, ReadBack<T>(written));
            Assert.AreEqual(Encoding.UTF8.GetString(written), Encoding.UTF8.GetString(rewritten), $"{nameOf(value)} doesn't round-trip.");
        }

        Assert.IsTrue(checkedAny, $"No {typeof(T).Name} to check.");
    }

    private static string TableText(string tableName, params string[] recordLines) =>
        ContentTable.Header(tableName) + "\n" + string.Concat(recordLines.Select(line => line + "\n"));

    private static string ActionLine(ActionDefinition action) => Encoding.UTF8.GetString(ContentWriter.WriteRecord(Types, action));

    [TestMethod]
    public void EveryBuiltInAction_RoundTripsToTheSameBytes() =>
        AssertRoundTrips(Build.Value.Context.Actions.Definitions, action => action.Name);

    [TestMethod]
    public void EveryBuiltInItem_RoundTripsToTheSameBytes() =>
        AssertRoundTrips(Build.Value.Context.Items.Definitions, item => item.Name);

    [TestMethod]
    public void EveryAura_RoundTripsToTheSameBytes() =>
        AssertRoundTrips(AllAuras(), aura => aura.Name);

    [TestMethod]
    public void ReadingBack_KeepsDeclaredTags_AndAddsTheActivatorsImpliedTagsAgain()
    {
        foreach (var item in Build.Value.Context.Items.Definitions)
        {
            var readBack = ReadBack<ItemDefinition>(ContentWriter.WriteRecord(Types, item));

            Assert.IsTrue(readBack.DeclaredTags.HasAllExact(item.DeclaredTags) && item.DeclaredTags.HasAllExact(readBack.DeclaredTags), $"{item.Name}'s declared tags changed.");
            Assert.IsTrue(readBack.Tags.HasAllExact(item.Tags) && item.Tags.HasAllExact(readBack.Tags), $"{item.Name}'s tags changed.");
        }
    }

    [TestMethod]
    public void EveryConcreteEntryActivatorAndContentsType_HasACodec()
    {
        var gameAssembly = typeof(ActionDefinition).Assembly;
        foreach (var family in new[] { typeof(IEffectEntry), typeof(IActionActivator), typeof(IItemContents) })
        {
            var registered = Types.MembersOf(family).Select(member => member.Codec.ValueType).ToHashSet();
            var concreteTypes = gameAssembly.GetTypes().Where(type => family.IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false });
            foreach (var concreteType in concreteTypes)
            {
                Assert.IsTrue(registered.Contains(concreteType), $"{concreteType.Name} is a {family.Name} with no content codec.");
            }
        }
    }

    [TestMethod]
    public void FieldsAtTheirDefault_AreOmitted()
    {
        var text = Encoding.UTF8.GetString(ContentWriter.WriteRecord(Types, new Effect([new DirectDamage(3, 5)])));

        Assert.AreEqual("{\"e\":[{\"$\":\"DirectDamage\",\"mn\":3,\"mx\":5}]}", text);
    }

    [TestMethod]
    public void AnUnknownDiscriminator_FailsOnlyItsRecord()
    {
        var actions = Build.Value.Context.Actions.Definitions.Take(2).ToList();
        var broken = ActionLine(actions[0]).Replace("\"$\":\"Direct\"", "\"$\":\"NoSuchActivator\"").Replace("\"$\":\"Spell\"", "\"$\":\"NoSuchActivator\"");
        var failures = new List<ContentFailure>();

        var records = ContentTable.Read<ActionDefinition>(TableText("Actions", broken, ActionLine(actions[1])), "Actions.jsonl", "Actions", Types, References, failures);

        Assert.HasCount(1, records);
        Assert.AreEqual(actions[1].Id, records[0].Id);
        Assert.HasCount(1, failures);
        Assert.AreEqual(actions[0].Id, failures[0].RecordId);
        StringAssert.Contains(failures[0].Reason, "NoSuchActivator");
    }

    [TestMethod]
    public void AnUnknownKey_FailsTheRecord()
    {
        var action = Build.Value.Context.Actions.Definitions.First();
        var withUnknownKey = ActionLine(action).TrimEnd('}') + ",\"zz\":1}";
        var failures = new List<ContentFailure>();

        var records = ContentTable.Read<ActionDefinition>(TableText("Actions", withUnknownKey), "Actions.jsonl", "Actions", Types, References, failures);

        Assert.IsEmpty(records);
        Assert.HasCount(1, failures);
        StringAssert.Contains(failures[0].Reason, "zz");
    }

    [TestMethod]
    public void ATruncatedLastLine_IsIgnored_ButATruncatedMiddleLineIsReported()
    {
        var actions = Build.Value.Context.Actions.Definitions.Take(2).ToList();
        var whole = ActionLine(actions[0]);
        var cut = ActionLine(actions[1])[..20];

        var lastLineFailures = new List<ContentFailure>();
        var lastLineRecords = ContentTable.Read<ActionDefinition>(ContentTable.Header("Actions") + "\n" + whole + "\n" + cut, "Actions.jsonl", "Actions", Types, References, lastLineFailures);
        Assert.HasCount(1, lastLineRecords);
        Assert.IsEmpty(lastLineFailures);

        var middleLineFailures = new List<ContentFailure>();
        var middleLineRecords = ContentTable.Read<ActionDefinition>(TableText("Actions", cut, whole), "Actions.jsonl", "Actions", Types, References, middleLineFailures);
        Assert.HasCount(1, middleLineRecords);
        Assert.HasCount(1, middleLineFailures);
        Assert.AreEqual(2, middleLineFailures[0].Line);
    }

    [TestMethod]
    public void AnOutOfRangeNumber_FailsTheRecord()
    {
        var text = "{\"e\":[{\"$\":\"DirectDamage\",\"mn\":3,\"mx\":40000}]}";
        using var document = JsonDocument.Parse(text);

        Assert.Throws<ContentFormatException>(() => ContentObject.Read<Effect>(document.RootElement, Types, References));
    }

    [TestMethod]
    public void AHeaderForAnotherTable_FailsTheWholeFile()
    {
        var failures = new List<ContentFailure>();

        var records = ContentTable.Read<ActionDefinition>(TableText("Items", ActionLine(Build.Value.Context.Actions.Definitions.First())), "Actions.jsonl", "Actions", Types, References, failures);

        Assert.IsEmpty(records);
        Assert.HasCount(1, failures);
    }

    [TestMethod]
    public void ADisabledRecord_IsNotDecoded_AndKeepsItsReplacement()
    {
        var actions = Build.Value.Context.Actions.Definitions.Take(2).ToList();
        var disabledLine = ContentTable.WriteDisabled(actions[0], actions[1].Id, Types);
        var failures = new List<ContentFailure>();

        var records = ContentTable.Read<ActionDefinition>(TableText("Actions", disabledLine), "Actions.jsonl", "Actions", Types, References, failures);

        Assert.IsEmpty(failures);
        Assert.HasCount(1, records);
        Assert.IsTrue(records[0].IsDisabled);
        Assert.IsNull(records[0].Value);
        Assert.AreEqual(actions[1].Id, records[0].Replacement);
    }

    [TestMethod]
    public void AReplacementOnARecordThatIsNotDisabled_FailsTheRecord()
    {
        var action = Build.Value.Context.Actions.Definitions.First();
        var line = ActionLine(action).TrimEnd('}') + $",\"rep\":\"{ContentGuid.Format(Guid.NewGuid())}\"}}";
        var failures = new List<ContentFailure>();

        ContentTable.Read<ActionDefinition>(TableText("Actions", line), "Actions.jsonl", "Actions", Types, References, failures);

        Assert.HasCount(1, failures);
    }

    [TestMethod]
    public void Guids_AreTwentyTwoCharacterBase64Url_AndParseBack()
    {
        for (var index = 0; index < 50; index++)
        {
            var id = Guid.NewGuid();
            var text = ContentGuid.Format(id);

            Assert.AreEqual(22, text.Length);
            Assert.IsFalse(text.Contains('+') || text.Contains('/') || text.Contains('='));
            Assert.IsTrue(ContentGuid.TryParse(text, out var parsed));
            Assert.AreEqual(id, parsed);
        }
    }

    [TestMethod]
    public void AnAuraReferenceToNothing_FailsTheRecord()
    {
        var text = $"{{\"e\":[{{\"$\":\"AuraSourceGrant\",\"au\":\"{ContentGuid.Format(Guid.NewGuid())}\",\"p\":1,\"s\":1}}]}}";
        using var document = JsonDocument.Parse(text);

        Assert.Throws<ContentFormatException>(() => ContentObject.Read<Effect>(document.RootElement, Types, References));
    }
}
