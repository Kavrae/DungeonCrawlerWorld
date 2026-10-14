using System.Reflection;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Presentation.MapRendering;
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
    public void FloatingTextController_ReferencesNoGameComponentType() =>
        AssertNoComponentReferences(typeof(global::Presentation.UI.FloatingText.FloatingTextController));

    [TestMethod]
    public void FloatingTextRenderer_ReferencesNoGameComponentType() =>
        AssertNoComponentReferences(typeof(global::Presentation.UI.FloatingText.FloatingTextRenderer));

    [TestMethod]
    public void MapRenderer_ReferencesNoGameComponentType() =>
        AssertNoComponentReferences(typeof(MapRenderer));

    [TestMethod]
    public void MapGameOverlay_ReferencesNoGameComponentType() =>
        AssertNoComponentReferences(typeof(MapGameOverlay));

    [TestMethod]
    public void MapCamera_ReferencesNoGameComponentType() =>
        AssertNoComponentReferences(typeof(MapCamera));

    /// <summary>The renderer and camera read the world only through views, so a map editor can hand them an authored layout: never the World, the store or the event bus.</summary>
    [TestMethod]
    public void MapRendererAndCamera_ReferenceNoWorldStoreOrEventBus()
    {
        var violations = new[] { typeof(MapRenderer), typeof(MapCamera) }.SelectMany(static type => FindReferences(type, IsWorldStoreOrEventBus)).ToList();
        Assert.IsEmpty(violations, $"The map renderer and camera must read the world through Game.Views:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [TestMethod]
    public void Scanner_FindsTheWorldStoreAndEventBus()
    {
        var violations = FindReferences(typeof(WorldStoreOrEventBusViolations), IsWorldStoreOrEventBus).ToList();

        Assert.HasCount(3, violations);
    }

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

    private static IEnumerable<string> FindComponentReferences(Type root) => FindReferences(root, IsComponent);

    private static IEnumerable<string> FindReferences(Type root, Func<Type, bool> isForbidden)
    {
        foreach (var type in SelfAndNestedTypes(root))
        {
            foreach (var field in type.GetFields(AllMembers))
            {
                if (isForbidden(field.FieldType))
                {
                    yield return $"{type.Name}: field {field.Name}";
                }
            }

            foreach (var property in type.GetProperties(AllMembers))
            {
                if (isForbidden(property.PropertyType))
                {
                    yield return $"{type.Name}: property {property.Name}";
                }
            }

            foreach (var method in type.GetMethods(AllMembers).Cast<MethodBase>().Concat(type.GetConstructors(AllMembers)))
            {
                if (method.GetParameters().Any(parameter => isForbidden(parameter.ParameterType)))
                {
                    yield return $"{type.Name}: method {method.Name} parameter";
                }

                if (method is MethodInfo methodInfo && isForbidden(methodInfo.ReturnType))
                {
                    yield return $"{type.Name}: method {method.Name} return";
                }

                if (method.GetMethodBody()?.LocalVariables.Any(local => isForbidden(local.LocalType)) == true)
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

    /// <summary>The World, the component store or the event bus, or anything built from one.</summary>
    private static bool IsWorldStoreOrEventBus(Type type)
    {
        if (type.HasElementType)
        {
            return IsWorldStoreOrEventBus(type.GetElementType()!);
        }

        if (type.IsGenericType && type.GetGenericArguments().Any(IsWorldStoreOrEventBus))
        {
            return true;
        }

        return type == typeof(Game.World.World) || type == typeof(Engine.ECS.Components.ComponentManager) || type == typeof(Engine.Events.EventBus);
    }

    private sealed class Violations
    {
#pragma warning disable CS0169, CS0649
        private Engine.ECS.Components.Stores.DirectComponentPool<TransformComponent>? _transforms;
#pragma warning restore CS0169, CS0649

        public static int ReadsParameter(GlyphComponent glyph) => glyph.Glyph.Length;

        public static int ReadsLocal()
        {
            var sprite = new SpriteComponent("sheet", 0);
            return sprite.SpriteName.Length;
        }

        public static MovementComponent ReturnsComponent() => default;

        public static Func<int> CapturesInClosure()
        {
            var display = new DisplayTextComponent("name", "description");
            return () => display.Name.Length;
        }
    }

    private sealed class WorldStoreOrEventBusViolations
    {
#pragma warning disable CS0169, CS0649
        private Game.World.World? _world;
#pragma warning restore CS0169, CS0649

        public static int ReadsParameter(Engine.Events.EventBus[] eventBuses) => eventBuses.Length;

        public static int ReadsLocal()
        {
            var componentManagers = new List<Engine.ECS.Components.ComponentManager>();
            return componentManagers.Count;
        }
    }
}
