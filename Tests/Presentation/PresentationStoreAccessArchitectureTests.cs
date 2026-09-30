using System.Reflection;
using Engine.ECS.Components;
using Engine.ECS.Entities;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Presentation reads the game through Game.Views and changes it through the command services
/// (PLAN-shell-composition-cleanup.md): no field, property, parameter, return value, local variable or
/// generic argument anywhere in Presentation -- closures and state machines included -- may name the
/// store itself: ComponentManager, EntityManager or a component pool. Component values are allowed;
/// they're what the views hand back.
/// </summary>
[TestClass]
public sealed class PresentationStoreAccessArchitectureTests
{
    private static readonly Assembly PresentationAssembly = typeof(MapWindow).Assembly;

    private const BindingFlags AllMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>The types still reading the store, each for its own reason.</summary>
    private static readonly HashSet<string> ExemptTypeNames =
    [
        // Splats aura sources from their pools into the map's tint grid; the map-view work owns it.
        nameof(MapTintGrid),
        // Its admin and rebuild paths read the staging world's store by design.
        "InspectionWindowContent",
        // Reports entity and pool counts for the F3 window.
        "DiagnosticsWindow",
    ];

    [TestMethod]
    public void Presentation_ReferencesNoStoreOutsideItsExemptions()
    {
        var violations = PresentationAssembly.GetTypes()
            .Where(static type => !ExemptTypeNames.Contains(OutermostType(type).Name))
            .SelectMany(FindStoreReferences)
            .ToList();

        Assert.IsEmpty(violations, $"Presentation must read through Game.Views and write through the command services, not the store:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    /// <summary>The scanner must catch every place the store can hide, or the test above proves nothing.</summary>
    [TestMethod]
    public void Scanner_FindsTheStoreInFieldsParametersLocalsAndGenericArguments()
    {
        var violations = FindStoreReferences(typeof(Violations)).ToList();

        CollectionAssert.IsSubsetOf(
            new[] { "field _componentManager", "field _transforms", "method ReadsEntityManager parameter", "method ReadsLocal local", "method ReturnsPools return" },
            violations.Select(static violation => violation[(violation.IndexOf(": ", StringComparison.Ordinal) + 2)..]).ToList());
    }

    private static Type OutermostType(Type type)
    {
        while (type.DeclaringType is { } declaringType)
        {
            type = declaringType;
        }

        return type;
    }

    private static IEnumerable<string> FindStoreReferences(Type type)
    {
        foreach (var field in type.GetFields(AllMembers))
        {
            if (IsStore(field.FieldType))
            {
                yield return $"{type.FullName}: field {field.Name}";
            }
        }

        foreach (var property in type.GetProperties(AllMembers))
        {
            if (IsStore(property.PropertyType))
            {
                yield return $"{type.FullName}: property {property.Name}";
            }
        }

        foreach (var method in type.GetMethods(AllMembers).Cast<MethodBase>().Concat(type.GetConstructors(AllMembers)))
        {
            if (method.GetParameters().Any(static parameter => IsStore(parameter.ParameterType)))
            {
                yield return $"{type.FullName}: method {method.Name} parameter";
            }

            if (method is MethodInfo methodInfo && IsStore(methodInfo.ReturnType))
            {
                yield return $"{type.FullName}: method {method.Name} return";
            }

            if (method.GetMethodBody()?.LocalVariables.Any(static local => IsStore(local.LocalType)) == true)
            {
                yield return $"{type.FullName}: method {method.Name} local";
            }
        }
    }

    /// <summary>ComponentManager, EntityManager or any component pool, or anything built from one (an array, a nullable, a generic argument, a ref).</summary>
    private static bool IsStore(Type type)
    {
        if (type.HasElementType)
        {
            return IsStore(type.GetElementType()!);
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition && type.GetGenericArguments().Any(IsStore))
        {
            return true;
        }

        return type == typeof(ComponentManager) || type == typeof(EntityManager) || typeof(IComponentPool).IsAssignableFrom(type);
    }

    private sealed class Violations
    {
#pragma warning disable CS0169, CS0649
        private ComponentManager? _componentManager;
        private List<Engine.ECS.Components.Stores.DirectComponentPool<Game.Modules.Core.Components.TransformComponent>>? _transforms;
#pragma warning restore CS0169, CS0649

        public static int ReadsEntityManager(EntityManager entityManager) => entityManager.LivingEntityCount;

        public static int ReadsLocal()
        {
            var pool = new Engine.ECS.Components.Stores.MultiComponentPool<Game.Modules.Core.Components.TransformComponent>(entityCapacity: 1, initialCapacity: 1);
            return pool.Count;
        }

        public static IComponentPool[] ReturnsPools() => [];
    }
}
