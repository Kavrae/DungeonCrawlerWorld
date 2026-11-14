using Engine.Decisions;
using Engine.ECS.Entities;
using Engine.Modules;
using Engine.Tags;

namespace Tests.Decisions;

/// <summary>What a fake decision records and is told: the rolls it hands out, the runtime multipliers per tag, and every call made to it.</summary>
internal sealed class DecisionLog(int seed = 1)
{
    private readonly Random _random = new(seed);

    public int Draws { get; private set; }

    public int RuntimeMultiplierCalls { get; private set; }

    public Dictionary<GameplayTag, float> RuntimeMultipliers { get; } = [];

    public float NextRoll()
    {
        Draws++;
        return (float)_random.NextDouble();
    }

    public float RuntimeMultiplier(GameplayTagSet tags)
    {
        RuntimeMultiplierCalls++;
        var multiplier = 1f;
        foreach (var (tag, factor) in RuntimeMultipliers)
        {
            if (tags.Has(tag))
            {
                multiplier *= factor;
            }
        }

        return multiplier;
    }
}

/// <summary>A decision context as a game's would be: a ref struct over what it reads, here only a log.</summary>
internal ref struct FakeContext(DecisionLog log, long now) : IDecisionContext
{
    public readonly DecisionLog Log = log;

    public readonly long Now => now;

    public EntityKey CommitmentTarget { get; set; }

    public readonly float NextUnitRandom() => Log.NextRoll();

    public readonly float RuntimeWeightMultiplier(GameplayTagSet behaviorTags) => Log.RuntimeMultiplier(behaviorTags);
}

/// <summary>A behavior whose evaluation the test sets, given the target it is handed, and which counts every call.</summary>
internal sealed class FakeBehavior(string name, GameplayTagSet tags = default) : IBehavior<FakeContext>
{
    public Guid Id { get; } = Guid.NewGuid();

    public string Name => name;

    public GameplayTagSet Tags { get; } = tags;

    /// <summary>What Evaluate returns, given the target the context hands it.</summary>
    public Func<EntityKey, BehaviorEvaluation> Evaluation { get; set; } = _ => BehaviorEvaluation.DoesNotApply;

    /// <summary>What Act leaves as the commitment target.</summary>
    public EntityKey TargetToCommitTo { get; set; }

    public int EvaluateCalls { get; private set; }

    public int ActCalls { get; private set; }

    public List<EntityKey> TargetsSeenByEvaluate { get; } = [];

    public List<EntityKey> TargetsSeenByAct { get; } = [];

    public BehaviorEvaluation Evaluate(ref FakeContext context, BehaviorGrant<FakeContext> grant)
    {
        EvaluateCalls++;
        TargetsSeenByEvaluate.Add(context.CommitmentTarget);
        return Evaluation(context.CommitmentTarget);
    }

    public void Act(ref FakeContext context, BehaviorGrant<FakeContext> grant)
    {
        ActCalls++;
        TargetsSeenByAct.Add(context.CommitmentTarget);
        if (!TargetToCommitTo.IsNone)
        {
            context.CommitmentTarget = TargetToCommitTo;
        }
    }

    public FakeBehavior Applying(byte rank, float weight)
    {
        Evaluation = _ => new BehaviorEvaluation(rank, weight);
        return this;
    }
}

/// <summary>A consideration returning a fixed factor and counting its calls.</summary>
internal sealed class FakeConsideration(float factor) : IBehaviorConsideration<FakeContext>
{
    public int Calls { get; private set; }

    public float Evaluate(ref FakeContext context)
    {
        Calls++;
        return factor;
    }
}

/// <summary>The tags and domain the decision tests tag behaviors with, and helpers that build catalogs, sets and arbiters.</summary>
internal static class DecisionTestKit
{
    public static readonly GameplayTag Root = GameplayTag.Get("DecisionTests");
    public static readonly GameplayTag Drive = GameplayTag.Get("DecisionTests.Drive");
    public static readonly GameplayTag Caution = GameplayTag.Get("DecisionTests.Drive.Caution");
    public static readonly GameplayTag Aggression = GameplayTag.Get("DecisionTests.Drive.Aggression");
    public static readonly GameplayTag OutsideTheDomain = GameplayTag.Get("DecisionTests.Other");

    public static readonly GameplayTagDomain BehaviorDomain = new("Test behavior");

    public static GameplayTagRegistry DecisionTags { get; } = GameplayTagRegistry.Declare([new TagModule()]);

    public static BehaviorCatalog<FakeContext> FrozenCatalog(params FakeBehavior[] behaviors)
    {
        var catalog = new BehaviorCatalog<FakeContext>();
        foreach (var behavior in behaviors)
        {
            catalog.Register(behavior);
        }

        catalog.Freeze(DecisionTags, BehaviorDomain);
        return catalog;
    }

    public static BehaviorGrant<FakeContext> Grant(FakeBehavior behavior, byte rank, float weight = 1f, ushort commitFrames = 0, params IBehaviorConsideration<FakeContext>[] considerations) =>
        new(behavior.Id, rank, weight, CommitFrames: commitFrames, Considerations: considerations.Length == 0 ? null : considerations);

    public static BehaviorSet<FakeContext> Set(BehaviorCatalog<FakeContext> catalog, params BehaviorGrant<FakeContext>[] grants) =>
        BehaviorSet<FakeContext>.Resolve(catalog, [grants], []);

    public static BehaviorArbiter<FakeContext> Arbiter() => new(new BehaviorArbiterOptions());

    private sealed class TagModule : IModule
    {
        public Guid Id => new("9e0a7e10-0000-4000-8000-000000000001");

        public void DeclareTags(GameplayTagDeclarations tags)
        {
            tags.DeclareRoot(Root, "Tags of the decision tests.");
            tags.Declare(Drive, "A drive.");
            tags.Declare(Caution, "Caution.");
            tags.Declare(Aggression, "Aggression.");
            tags.Declare(OutsideTheDomain, "Not a drive.");
            tags.DeclareDomain(BehaviorDomain, "What a test behavior is.", extensible: false, [Drive]);
        }

        public void RegisterComponents(ComponentRegistration registration)
        {
        }
    }
}
