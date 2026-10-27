using System.Reflection;
using Engine.Math;
using Game;
using Game.Areas;
using Game.Modules;
using Game.World;

namespace Tests.Architecture;

/// <summary>World generation reads only derived seeds: none of its types can reach a session's random streams, so no gameplay roll can change what a neighborhood generates.</summary>
[TestClass]
public sealed class WorldGenerationRandomnessArchitectureTests
{
    private static readonly Type[] GenerationTypes = [typeof(NeighborhoodRecords), typeof(NeighborhoodRecord), typeof(TestMapBuilder), typeof(AreaStamp)];

    private static readonly Type[] SessionRandomnessTypes = [typeof(RandomStreams), typeof(MathUtility), typeof(Random), typeof(GameModuleContext)];

    private static bool IsSessionRandomness(Type type) => SessionRandomnessTypes.Any(randomness => randomness.IsAssignableFrom(type));

    [TestMethod]
    public void GenerationTypes_HoldAndTakeNoSessionRandomness()
    {
        var violations = new List<string>();
        foreach (var type in GenerationTypes)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => IsSessionRandomness(field.FieldType))
                .Select(field => $"{type.Name}.{field.Name} ({field.FieldType.Name})");
            var parameters = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => IsSessionRandomness(parameter.ParameterType))
                .Select(parameter => $"{type.Name}({parameter.Name}: {parameter.ParameterType.Name})");
            violations.AddRange(fields);
            violations.AddRange(parameters);
        }

        Assert.IsEmpty(violations, $"World generation must derive its seeds (NeighborhoodSeeds, AreaSeeds), never hold a session's random sequence:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }
}
