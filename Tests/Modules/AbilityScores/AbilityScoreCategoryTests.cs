using Game.Modules.AbilityScores;

namespace Tests.Modules.AbilityScores;

[TestClass]
public sealed class AbilityScoreCategoryTests
{
    [TestMethod]
    public void IsHidden_CoreTypes_ReturnsFalse()
    {
        foreach (var type in new[] { AbilityScoreType.Strength, AbilityScoreType.Intelligence, AbilityScoreType.Constitution, AbilityScoreType.Dexterity, AbilityScoreType.Charisma })
        {
            Assert.IsFalse(AbilityScoreCategory.IsHidden(type), $"{type} is Core and should not be Hidden.");
        }
    }

    [TestMethod]
    public void IsHidden_HiddenTypes_ReturnsTrue()
    {
        foreach (var type in new[] { AbilityScoreType.Luck, AbilityScoreType.Wisdom })
        {
            Assert.IsTrue(AbilityScoreCategory.IsHidden(type), $"{type} is Hidden and should not be Core.");
        }
    }

    /// <summary>Every AbilityScoreType must land in exactly one category -- guards against IsHidden's Core allowlist silently missing a new member (see IsHidden's own doc comment for why it's defined this way around).</summary>
    [TestMethod]
    public void IsHidden_EveryDeclaredType_IsClassified()
    {
        var coreCount = 0;
        var hiddenCount = 0;
        foreach (var type in Enum.GetValues<AbilityScoreType>())
        {
            if (AbilityScoreCategory.IsHidden(type))
            {
                hiddenCount++;
            }
            else
            {
                coreCount++;
            }
        }

        Assert.AreEqual(Enum.GetValues<AbilityScoreType>().Length, coreCount + hiddenCount);
        Assert.AreEqual(5, coreCount);
        Assert.AreEqual(2, hiddenCount);
    }
}
