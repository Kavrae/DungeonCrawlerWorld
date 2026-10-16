using System.Reflection;
using UIEngine.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
public sealed class InputHookImplementationTests
{
    private static readonly Type[] HookInterfaces = [typeof(IUiInputHandler), typeof(IContentDropHandler)];

    /// <summary>
    /// The input hooks' members have default bodies, so a handler method whose signature drifts from the hook's (a
    /// parameter added or removed) still compiles -- as an unrelated method the controller never calls. Every public
    /// method a handler declares under a hook member's name must be that member's implementation.
    /// </summary>
    [TestMethod]
    public void EveryHandlerMethodNamedForAHookMember_ImplementsIt()
    {
        var assemblies = new[] { typeof(Element).Assembly, typeof(global::Presentation.UI.MapWindow).Assembly };
        var drifted = new List<string>();

        foreach (var type in assemblies.SelectMany(assembly => assembly.GetTypes()).Where(type => type is { IsClass: true, IsAbstract: false }))
        {
            foreach (var hook in HookInterfaces.Where(hook => hook.IsAssignableFrom(type)))
            {
                var implementations = type.GetInterfaceMap(hook).TargetMethods.ToHashSet();
                var hookMemberNames = hook.GetMethods().Select(method => method.Name).ToHashSet();

                drifted.AddRange(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(method => hookMemberNames.Contains(method.Name) && !implementations.Contains(method))
                    .Select(method => $"{type.Name}.{method.Name}"));
            }
        }

        Assert.IsEmpty(drifted, $"Not implementing their hook member: {string.Join(", ", drifted)}.");
    }
}
