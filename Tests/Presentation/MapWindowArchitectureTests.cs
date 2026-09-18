using System.Reflection;
using Game.Modules.Core.Components;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// The map draw path reads the world only through Game.Views (PLAN-presentation-data-layer.md,
/// Stage 2): no field, property, parameter, return value, local variable or generic argument
/// anywhere in these types -- including the closures and state machines the compiler generates
/// inside them -- may name a Game component type.
/// </summary>
[TestClass]
public sealed class MapWindowArchitectureTests
{
    private static readonly Assembly GameAssembly = typeof(TransformComponent).Assembly;

    private const BindingFlags AllMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [TestMethod]
    public void MapWindow_ReferencesNoGameComponentType() =>
        AssertNoComponentReferences(typeof(MapWindow));

    [TestMethod]
    public void MapBackgroundCache_ReferencesNoGameComponentType() =>
        AssertNoComponentReferences(typeof(MapBackgroundCache));

    /// <summary>The scanner itself must catch every place a component can hide, or the two tests above prove nothing.</summary>
    [TestMethod]
    public void Scanner_FindsComponentsInFieldsParametersLocalsAndClosures()
    {
        var violations = FindComponentReferences(typeof(Violations)).ToList();

        CollectionAssert.IsSubsetOf(
            new[] { "field _transforms", "method ReadsParameter parameter", "method ReadsLocal local", "method ReturnsComponent return" },
            violations.Select(static violation => violation[(violation.IndexOf(": ", StringComparison.Ordinal) + 2)..]).ToList());
        Assert.IsTrue(violations.Any(static violation => violation.Contains("<>c", StringComparison.Ordinal) || violation.Contains("DisplayClass", StringComparison.Ordinal)),
            "A component used only inside a lambda lives on a compiler-generated type and must still be found.");
    }

    private static void AssertNoComponentReferences(Type type)
    {
        var violations = FindComponentReferences(type).ToList();
        Assert.IsEmpty(violations, $"{type.Name} must read the world through Game.Views, not component types:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static IEnumerable<string> FindComponentReferences(Type root)
    {
        foreach (var type in SelfAndNestedTypes(root))
        {
            foreach (var field in type.GetFields(AllMembers))
            {
                if (IsComponent(field.FieldType))
                {
                    yield return $"{type.Name}: field {field.Name}";
                }
            }

            foreach (var property in type.GetProperties(AllMembers))
            {
                if (IsComponent(property.PropertyType))
                {
                    yield return $"{type.Name}: property {property.Name}";
                }
            }

            foreach (var method in type.GetMethods(AllMembers).Cast<MethodBase>().Concat(type.GetConstructors(AllMembers)))
            {
                if (method.GetParameters().Any(static parameter => IsComponent(parameter.ParameterType)))
                {
                    yield return $"{type.Name}: method {method.Name} parameter";
                }

                if (method is MethodInfo methodInfo && IsComponent(methodInfo.ReturnType))
                {
                    yield return $"{type.Name}: method {method.Name} return";
                }

                if (method.GetMethodBody()?.LocalVariables.Any(static local => IsComponent(local.LocalType)) == true)
                {
                    yield return $"{type.Name}: method {method.Name} local";
                }
            }
        }
    }

    private static IEnumerable<Type> SelfAndNestedTypes(Type type)
    {
        yield return type;

        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var descendant in SelfAndNestedTypes(nested))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>A Game type named *Component, or anything built from one (an array, a nullable, a pool of it, a ref to it).</summary>
    private static bool IsComponent(Type type)
    {
        if (type.HasElementType)
        {
            return IsComponent(type.GetElementType()!);
        }

        if (type.IsGenericType && type.GetGenericArguments().Any(IsComponent))
        {
            return true;
        }

        return type.Assembly == GameAssembly && type.Name.EndsWith("Component", StringComparison.Ordinal);
    }

    private sealed class Violations
    {
#pragma warning disable CS0169, CS0649
        private Engine.ECS.Components.Stores.DirectComponentPool<TransformComponent>? _transforms;
#pragma warning restore CS0169, CS0649

        public static int ReadsParameter(GlyphComponent glyph) => glyph.Glyph.Length;

        public static int ReadsLocal()
        {
            var sprite = new SpriteComponent("sheet", default);
            return sprite.SheetPath.Length;
        }

        public static BackgroundComponent ReturnsComponent() => default;

        public static Func<int> CapturesInClosure()
        {
            var display = new DisplayTextComponent("name", "description");
            return () => display.Name.Length;
        }
    }
}
