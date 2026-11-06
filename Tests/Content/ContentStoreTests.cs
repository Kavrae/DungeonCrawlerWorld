using Engine.Content;
using Engine.Persistence;
using Engine.Tags;
using Engine.Tiles;
using Game.Bootstrap;
using Game.Content;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Auras;

namespace Tests.Content;

/// <summary>A session's save: definitions created and overridden in it, its drafts, and what the next session over it loads.</summary>
[TestClass]
public sealed class ContentStoreTests
{
    private const string SaveName = "Test";

    private string _savesDirectory = null!;

    [TestInitialize]
    public void CreateDirectory() => _savesDirectory = Directory.CreateTempSubdirectory().FullName;

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_savesDirectory, recursive: true);

    private SaveFolder OpenSave() => SaveFolder.OpenOrCreate(_savesDirectory, SaveName);

    private static GameSession BuildOver(SaveFolder save) =>
        GameBootstrapper.Build(ValidatedMods.None, GameBuildPass.CreatePlaceholderMap(), TestContent.Layers, sessionSeed: 0, initialEntityCapacity: 16, initialComponentCapacity: 16, buildsStagingWorldOnFirstRebuild: true, save: save);

    private static ActionDefinition Renamed(Guid actionId, string name) => TestContent.Action(actionId) with { Name = name };

    /// <summary>Toxic Aura aimed at a single target: a toggle that isn't Self-targeted, which the toggle rules refuse.</summary>
    private static ActionDefinition IncompleteToggle()
    {
        var toxicAura = TestContent.Action(BuiltInActionIds.ToxicAura);
        return toxicAura with
        {
            Name = "Incomplete",
            Activator = (DirectAction)toxicAura.Activator with { Targeting = new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 4) },
        };
    }

    private static GameplayTagSet ReversedTags(GameplayTagSet tags)
    {
        var reversed = new List<GameplayTag>();
        foreach (var tag in tags)
        {
            reversed.Insert(0, tag);
        }

        return GameplayTagSet.Create([.. reversed]);
    }

    private static string[] RecordLines(string path) => File.Exists(path) ? [.. File.ReadAllLines(path).Skip(1)] : [];

    [TestMethod]
    public void TheDerivedGuid_IgnoresTheRecordsOwnId_AndItsTagOrder_AndAFieldSetToItsDefault()
    {
        using var save = OpenSave();
        var actions = BuildOver(save).ContentStore!.Actions;
        var action = Renamed(BuiltInActionIds.QuickAttack, "Copy");
        var reordered = new ActionDefinition(action.Id, action.Name, action.SpriteName, action.Glyph, action.GlyphColor, ReversedTags(action.DeclaredTags), action.Effects, action.Activator, action.Description, action.Summary, action.Toggle)
        {
            ActivationEffects = action.ActivationEffects,
        };

        var id = actions.DerivedIdOf(action);

        Assert.AreEqual(id, actions.DerivedIdOf(action with { Id = Guid.NewGuid() }));
        Assert.AreEqual(id, actions.DerivedIdOf(reordered));
        Assert.AreEqual(id, actions.DerivedIdOf(action with { ActivationEffects = [] }));
        Assert.AreNotEqual(id, actions.DerivedIdOf(action with { Name = "Another copy" }));
    }

    [TestMethod]
    public void CreatingTheSameDefinitionTwice_AppendsOneLine_AndRegistersOneDefinition()
    {
        using var save = OpenSave();
        var session = BuildOver(save);
        var actions = session.ContentStore!.Actions;

        var first = actions.CreatePersistent(Renamed(BuiltInActionIds.MagicMissile, "Copy"));
        var second = actions.CreatePersistent(Renamed(BuiltInActionIds.MagicMissile, "Copy") with { Id = Guid.NewGuid() });
        save.Writer.Flush();

        Assert.AreEqual(ContentStoreOutcome.Saved, first.Outcome);
        Assert.AreEqual(ContentStoreOutcome.AlreadySaved, second.Outcome);
        Assert.AreEqual(first.Id, second.Id);
        Assert.AreSame(first.Definition, session.Catalogs.ActionCatalog.Get(first.Id));
        Assert.HasCount(1, RecordLines(actions.DefinitionsPath));
    }

    [TestMethod]
    public void ACreatedDefinition_IsLoadedByTheNextSessionOverTheSave()
    {
        Guid id;
        using (var save = OpenSave())
        {
            id = BuildOver(save).ContentStore!.Actions.CreatePersistent(Renamed(BuiltInActionIds.MagicMissile, "Copy")).Id;
        }

        using var reopened = OpenSave();
        var session = BuildOver(reopened);

        Assert.AreEqual("Copy", session.Catalogs.ActionCatalog.Get(id).Name);
        Assert.IsTrue(session.Content.TryGetLocation(GameContent.ActionsTable, id, out var location));
        Assert.AreSame(reopened.DefinitionsLayer, location.Layer);
        Assert.AreEqual(new ContentTableCounts(1, 1, 0), session.Content.CountIn(reopened.DefinitionsLayer, GameContent.ActionsTable));
    }

    [TestMethod]
    public void AnOverride_TakesEffectInTheNextSession_AndSoDoesRemovingIt()
    {
        var quickAttack = BuiltInActionIds.QuickAttack;
        var masterName = TestContent.Action(quickAttack).Name;
        using (var save = OpenSave())
        {
            var session = BuildOver(save);
            var result = session.ContentStore!.Actions.Override(Renamed(quickAttack, "Overridden"));

            Assert.AreEqual(ContentStoreOutcome.Saved, result.Outcome);
            Assert.AreEqual(masterName, session.Catalogs.ActionCatalog.Get(quickAttack).Name);
        }

        using (var save = OpenSave())
        {
            var session = BuildOver(save);
            Assert.AreEqual("Overridden", session.Catalogs.ActionCatalog.Get(quickAttack).Name);
            CollectionAssert.Contains(session.ContentStore!.Actions.OverriddenIds.ToArray(), quickAttack);

            var removed = session.ContentStore.Actions.RemoveOverride(quickAttack);

            Assert.AreEqual(ContentStoreOutcome.Removed, removed.Outcome);
            Assert.AreEqual(masterName, removed.Definition!.Name);
            Assert.AreEqual("Overridden", session.Catalogs.ActionCatalog.Get(quickAttack).Name);
            Assert.AreEqual(ContentStoreOutcome.NothingToRemove, session.ContentStore.Actions.RemoveOverride(quickAttack).Outcome);
        }

        using (var save = OpenSave())
        {
            var session = BuildOver(save);
            Assert.AreEqual(TestContent.Action(quickAttack).Name, session.Catalogs.ActionCatalog.Get(quickAttack).Name);
            Assert.IsEmpty(session.ContentStore!.Actions.OverriddenIds);
        }
    }

    [TestMethod]
    public void OverridingAgainAfterARemoval_AppendsTheOverrideAgain_AndStartupCleanupCompactsItToOneLine()
    {
        var quickAttack = BuiltInActionIds.QuickAttack;
        using var save = OpenSave();
        var actions = BuildOver(save).ContentStore!.Actions;
        var removal = ContentTable.WriteRemoval(quickAttack);

        actions.Override(Renamed(quickAttack, "Overridden"));
        actions.RemoveOverride(quickAttack);
        var restored = actions.Override(Renamed(quickAttack, "Overridden"));
        var again = actions.Override(Renamed(quickAttack, "Overridden"));
        save.Writer.Flush();

        Assert.AreEqual(ContentStoreOutcome.Saved, restored.Outcome);
        Assert.AreEqual(ContentStoreOutcome.AlreadySaved, again.Outcome);
        var lines = RecordLines(actions.DefinitionsPath);
        Assert.HasCount(3, lines);
        StringAssert.Contains(lines[0], "Overridden");
        CollectionAssert.AreEqual(new[] { lines[0], removal, lines[0] }, lines);

        StartupCleanup.Run([], save);
        var next = BuildOver(save);

        CollectionAssert.AreEqual(new[] { lines[0] }, RecordLines(actions.DefinitionsPath));
        Assert.AreEqual("Overridden", next.Catalogs.ActionCatalog.Get(quickAttack).Name);
        CollectionAssert.AreEqual(new[] { quickAttack }, next.ContentStore!.Actions.OverriddenIds.ToArray());
    }

    [TestMethod]
    public void ASaveOverrideThatFailsToDecode_LeavesTheMasterRecordInItsPlace()
    {
        var quickAttack = BuiltInActionIds.QuickAttack;
        using var save = OpenSave();
        var masterName = TestContent.Action(quickAttack).Name;
        save.Writer.Append(save.DefinitionsPathOf(GameContent.ActionsFileName), ContentTable.Header(GameContent.ActionsTable), $"{{\"id\":\"{ContentGuid.Format(quickAttack)}\",\"n\":7}}");
        save.Writer.Flush();

        var session = BuildOver(save);

        Assert.AreEqual(masterName, session.Catalogs.ActionCatalog.Get(quickAttack).Name);
        Assert.IsTrue(session.ContentFailures.Any(failure => failure.Layer == save.DefinitionsLayer.Name && failure.RecordId == quickAttack));
    }

    [TestMethod]
    public void OverridingAGuidNoLowerLayerHas_IsRefused()
    {
        using var save = OpenSave();
        var actions = BuildOver(save).ContentStore!.Actions;

        var result = actions.Override(Renamed(BuiltInActionIds.QuickAttack, "Stray") with { Id = Guid.NewGuid() });

        Assert.AreEqual(ContentStoreOutcome.Refused, result.Outcome);
        Assert.IsFalse(File.Exists(actions.DefinitionsPath));
    }

    /// <summary>Lava's aura is content: overriding it changes it for the terrain that radiates it and for everything else naming it, from the next session.</summary>
    [TestMethod]
    public void AnOverriddenTerrainAura_IsWhatTheTerrainRadiatesInTheNextSession()
    {
        var original = TestContent.Aura(BuiltInAuraIds.Lava);
        using (var save = OpenSave())
        {
            var session = BuildOver(save);
            var result = session.ContentStore!.Auras.Override(original with { Name = "Overridden", GlowOpacityMultiplier = 0.25f });

            Assert.AreEqual(ContentStoreOutcome.Saved, result.Outcome);
            Assert.AreEqual(original.Name, session.Catalogs.Auras.Get(session.Catalogs.Auras.GetId(BuiltInAuraIds.Lava)).Name);
        }

        using (var save = OpenSave())
        {
            var auras = BuildOver(save).Catalogs.Auras;

            var lava = auras.Get(auras.GetId(BuiltInAuraIds.Lava));
            Assert.AreEqual("Overridden", lava.Name);
            Assert.AreEqual(0.25f, lava.GlowOpacityMultiplier);
        }
    }

    [TestMethod]
    public void ASaveRecordThatFailsItsChecks_IsLeftOutAtStartup_ForTheMasterRecordItOverrode()
    {
        var types = EcsBuilderTypes();
        using var save = OpenSave();
        File.WriteAllText(save.DefinitionsPathOf(GameContent.ActionsFileName), ContentTable.Header(GameContent.ActionsTable) + "\n" + ContentTable.WriteLine(IncompleteToggle(), types) + "\n");

        var session = BuildOver(save);

        Assert.IsTrue(session.Content.IsDecoded(GameContent.ActionsTable, BuiltInActionIds.ToxicAura));
        Assert.IsTrue(session.ContentFailures.Any(failure => failure.Layer == save.DefinitionsLayer.Name && failure.RecordId == BuiltInActionIds.ToxicAura));
        Assert.AreEqual(TestContent.Action(BuiltInActionIds.ToxicAura).Name, session.Catalogs.ActionCatalog.Get(BuiltInActionIds.ToxicAura).Name);
    }

    [TestMethod]
    public void ADefinitionThatFailsItsChecks_IsSavedAsADraft_NotRegistered_AndKeepsItsWorkingGuidWhenSavedAgain()
    {
        using var save = OpenSave();
        var session = BuildOver(save);
        var actions = session.ContentStore!.Actions;

        var draft = actions.CreatePersistent(IncompleteToggle());
        var savedAgain = actions.SaveDraft(draft.Definition! with { Description = "Still working on it" });
        save.Writer.Flush();

        Assert.AreEqual(ContentStoreOutcome.SavedAsDraft, draft.Outcome);
        StringAssert.Contains(draft.Problems.Single(), "is a toggle but is not Self-targeted");
        Assert.IsFalse(session.Catalogs.ActionCatalog.TryGet(draft.Id, out _));
        Assert.IsFalse(session.Catalogs.ActionCatalog.TryGet(actions.DerivedIdOf(IncompleteToggle()), out _));
        Assert.AreEqual(draft.Id, savedAgain.Id);
        CollectionAssert.AreEqual(new[] { draft.Id }, actions.DraftIds().ToArray());
        Assert.IsFalse(File.Exists(actions.DefinitionsPath));
        Assert.AreEqual(1, ContentStore.DraftCountIn(save, GameContent.ActionsFileName));
    }

    [TestMethod]
    public void APublishedDraft_TakesItsDerivedGuid_AndLeavesTheDrafts()
    {
        using var save = OpenSave();
        var session = BuildOver(save);
        var actions = session.ContentStore!.Actions;
        var draft = actions.CreatePersistent(IncompleteToggle());
        var toxicAura = TestContent.Action(BuiltInActionIds.ToxicAura);
        var fixedDraft = draft.Definition! with { Activator = toxicAura.Activator };
        actions.SaveDraft(fixedDraft);

        var published = actions.PublishDrafts().Single();

        Assert.AreEqual(ContentStoreOutcome.Saved, published.Outcome);
        Assert.AreEqual(actions.DerivedIdOf(fixedDraft), published.Id);
        Assert.AreNotEqual(draft.Id, published.Id);
        Assert.AreEqual("Incomplete", session.Catalogs.ActionCatalog.Get(published.Id).Name);
        Assert.IsEmpty(actions.DraftIds());
        Assert.HasCount(1, RecordLines(actions.DefinitionsPath));
    }

    [TestMethod]
    public void ADraftThatDoesntDecode_IsKeptAsWritten()
    {
        using var save = OpenSave();
        var actions = BuildOver(save).ContentStore!.Actions;
        var workingId = Guid.NewGuid();
        var brokenLine = $"{{\"id\":\"{ContentGuid.Format(workingId)}\",\"n\":\"Broken\",\"nonsense\":1}}";
        File.WriteAllText(actions.DraftsPath, ContentTable.Header(GameContent.ActionsTable) + "\n" + brokenLine + "\n");

        var result = actions.PublishDrafts().Single();

        Assert.AreEqual(ContentStoreOutcome.KeptAsDraft, result.Outcome);
        Assert.AreEqual(workingId, result.Id);
        CollectionAssert.AreEqual(new[] { brokenLine }, RecordLines(actions.DraftsPath));
    }

    [TestMethod]
    public void ASaveTablesTruncatedLastLine_LoadsTheRest()
    {
        var types = EcsBuilderTypes();
        var saved = Renamed(BuiltInActionIds.MagicMissile, "Saved") with { Id = Guid.NewGuid() };
        using var save = OpenSave();
        File.WriteAllText(save.DefinitionsPathOf(GameContent.ActionsFileName), ContentTable.Header(GameContent.ActionsTable) + "\n" + ContentTable.WriteLine(saved, types) + "\n{\"id\":\"cut");

        var session = BuildOver(save);

        Assert.AreEqual("Saved", session.Catalogs.ActionCatalog.Get(saved.Id).Name);
        Assert.IsEmpty(session.ContentFailures);
    }

    [TestMethod]
    public void ASaveRecordThatFailsAtLoad_IsLeftOutAndReported_AndTheSessionStillBuilds()
    {
        var types = EcsBuilderTypes();
        var saved = Renamed(BuiltInActionIds.MagicMissile, "Saved") with { Id = Guid.NewGuid() };
        using var save = OpenSave();
        File.WriteAllText(save.DefinitionsPathOf(GameContent.ActionsFileName), ContentTable.Header(GameContent.ActionsTable) + "\n{ not json\n" + ContentTable.WriteLine(saved, types) + "\n");

        var session = BuildOver(save);

        var failure = Assert.ContainsSingle(session.ContentFailures);
        Assert.AreEqual(save.DefinitionsLayer.Name, failure.Layer);
        Assert.AreEqual(2, failure.Line);
        Assert.AreEqual("Saved", session.Catalogs.ActionCatalog.Get(saved.Id).Name);
        Assert.AreEqual(new ContentTableCounts(1, 1, 1), session.Content.CountIn(save.DefinitionsLayer, GameContent.ActionsTable));
    }

    [TestMethod]
    public void AnActionLoadedAfterItsAuraWasOverridden_LoadsWithTheAuraThisSessionStartedWith()
    {
        using var save = OpenSave();
        var session = BuildOver(save);
        var original = TestContent.Aura(BuiltInAuraIds.ToxicAura);
        Assert.IsFalse(session.Content.IsDecoded(GameContent.ActionsTable, BuiltInActionIds.ToxicAura));

        session.ContentStore!.Auras.Override(original with { Name = "Overridden" });
        var action = session.Catalogs.ActionCatalog.Get(BuiltInActionIds.ToxicAura);

        Assert.AreEqual(BuiltInActionIds.ToxicAura, action.Id);
        Assert.AreEqual(original.Name, session.Catalogs.Auras.Get(session.Catalogs.Auras.GetId(original.Id)).Name);
    }

    [TestMethod]
    public void CreatingAnAuraAnEarlierSessionSaved_IsAlreadySaved_AndAppendsNothing()
    {
        var aura = TestContent.Action(BuiltInActionIds.ToxicAura).Effects.SelectMany(effect => effect.Entries).SelectMany(entry => entry.ReferencedAuras).First() with { Name = "Persistent copy" };
        Guid savedId;
        using (var save = OpenSave())
        {
            var created = BuildOver(save).ContentStore!.Auras.CreatePersistent(aura);
            Assert.AreEqual(ContentStoreOutcome.Saved, created.Outcome);
            savedId = created.Id;
        }

        using (var save = OpenSave())
        {
            var auras = BuildOver(save).ContentStore!.Auras;
            var again = auras.CreatePersistent(aura);
            save.Writer.Flush();

            Assert.AreEqual(ContentStoreOutcome.AlreadySaved, again.Outcome);
            Assert.AreEqual(savedId, again.Id);
            Assert.HasCount(1, RecordLines(auras.DefinitionsPath));
        }
    }

    [TestMethod]
    public void ASessionWithNoSave_HasNoStore()
    {
        var session = GameBootstrapper.Build(ValidatedMods.None, GameBuildPass.CreatePlaceholderMap(), TestContent.Layers, sessionSeed: 0, initialEntityCapacity: 16, initialComponentCapacity: 16, buildsStagingWorldOnFirstRebuild: true);

        Assert.IsNull(session.ContentStore);
    }

    private static ContentTypeRegistry EcsBuilderTypes() => Engine.Bootstrap.EcsBuilder.Declare(GameBootstrapper.BuiltInModules()).ContentTypes;
}
