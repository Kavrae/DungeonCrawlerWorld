using System.Runtime.CompilerServices;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;

namespace Tests.Modules.AbilityScores;

[TestClass]
public sealed class AbilityScoresComponentTests
{
    [TestMethod]
    public void Count_CoversEveryAbilityScoreType()
    {
        Assert.HasCount(AbilityScoresComponent.Count, Enum.GetValues<AbilityScoreType>());
        Assert.AreEqual(AbilityScoresComponent.Count - 1, (int)Enum.GetValues<AbilityScoreType>().Max(), "AbilityScoreType must stay a gapless 0-based enum -- the component indexes by its value.");
    }

    [TestMethod]
    public void Size_IsOneFixedStructPerEntity()
    {
        Assert.AreEqual(30, Unsafe.SizeOf<AbilityScoresComponent>(), "7 bases + 7 totals + the granted mask, padded to ushort alignment.");
    }

    [TestMethod]
    public void UngrantedScore_IsNotReturned()
    {
        var scores = default(AbilityScoresComponent);
        scores.Set(AbilityScoreType.Strength, 7, 9);

        Assert.IsFalse(scores.Has(AbilityScoreType.Dexterity));
        Assert.IsFalse(scores.TryGet(AbilityScoreType.Dexterity, out _));
        Assert.IsTrue(scores.TryGet(AbilityScoreType.Strength, out var strength));
        Assert.AreEqual(new AbilityScoreValue(AbilityScoreType.Strength, 7, 9), strength);
    }

    [TestMethod]
    public void EveryScore_KeepsItsOwnValues()
    {
        var scores = default(AbilityScoresComponent);
        foreach (var type in Enum.GetValues<AbilityScoreType>())
        {
            scores.Set(type, (ushort)(10 + (int)type), (ushort)(100 + (int)type));
        }

        foreach (var type in Enum.GetValues<AbilityScoreType>())
        {
            Assert.IsTrue(scores.TryGet(type, out var score));
            Assert.AreEqual((ushort)(10 + (int)type), score.BaseValue);
            Assert.AreEqual((ushort)(100 + (int)type), score.Total);
        }
    }

    [TestMethod]
    public void SetTotal_LeavesTheBaseAlone_AndNoOpsForAnUngrantedScore()
    {
        var scores = default(AbilityScoresComponent);
        scores.Set(AbilityScoreType.Constitution, 12, 12);

        scores.SetTotal(AbilityScoreType.Constitution, 30);
        scores.SetTotal(AbilityScoreType.Luck, 30);

        Assert.IsTrue(scores.TryGet(AbilityScoreType.Constitution, out var constitution));
        Assert.AreEqual((ushort)12, constitution.BaseValue);
        Assert.AreEqual((ushort)30, constitution.Total);
        Assert.IsFalse(scores.Has(AbilityScoreType.Luck));
    }

    [TestMethod]
    public void MergeFrom_OverwritesGrantedScoresOnly()
    {
        var existing = default(AbilityScoresComponent);
        existing.Set(AbilityScoreType.Strength, 5, 5);
        existing.Set(AbilityScoreType.Wisdom, 5, 5);

        var incoming = default(AbilityScoresComponent);
        incoming.Set(AbilityScoreType.Strength, 20, 25);
        incoming.Set(AbilityScoreType.Charisma, 3, 3);

        existing.MergeFrom(incoming);

        Assert.IsTrue(existing.TryGet(AbilityScoreType.Strength, out var strength));
        Assert.AreEqual(new AbilityScoreValue(AbilityScoreType.Strength, 20, 25), strength);
        Assert.IsTrue(existing.TryGet(AbilityScoreType.Wisdom, out var wisdom));
        Assert.AreEqual(new AbilityScoreValue(AbilityScoreType.Wisdom, 5, 5), wisdom);
        Assert.IsTrue(existing.TryGet(AbilityScoreType.Charisma, out var charisma));
        Assert.AreEqual(new AbilityScoreValue(AbilityScoreType.Charisma, 3, 3), charisma);
    }
}
