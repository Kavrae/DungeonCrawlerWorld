using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Analyzers;

/// <summary>CAD0004: a test that reaches a serial test resource, directly or through helpers in its assembly, must be [DoNotParallelize].</summary>
/// <remarks>
/// Runs only in a compilation that can see TestSupport.SerialTestResourceAttribute. Reach follows the members each member
/// names, its lambdas included, and a test's class's constructors, TestInitialize and TestCleanup count as part of every
/// test. It doesn't follow a virtual or interface call to its implementations, or a call into another assembly.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SerialTestResourceAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CAD0004";

    private const string ResourceAttributeMetadataName = "TestSupport.SerialTestResourceAttribute";
    private const string MemberAttributeMetadataName = "TestSupport.SerialTestMemberAttribute";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "A test reaching a serial resource must not run in parallel",
        "'{0}' reaches the serial test resource '{1}' ({2}), so it or its class must be [DoNotParallelize]",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A serial test resource is process-wide state that isn't safe to use from two tests at once.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var resourceAttribute = start.Compilation.GetTypeByMetadataName(ResourceAttributeMetadataName);
            if (resourceAttribute is null)
            {
                return;
            }

            var analysis = new ReachAnalysis(resourceAttribute, start.Compilation.GetTypeByMetadataName(MemberAttributeMetadataName));
            start.RegisterSemanticModelAction(analysis.Collect);
            start.RegisterCompilationEndAction(analysis.Report);
        });
    }

    /// <summary>What one member declaration names.</summary>
    private sealed class DeclaredMember(ISymbol member, bool isInstanceInitializer, List<ISymbol> references, string? resource)
    {
        public ISymbol Member { get; } = member;

        public bool IsInstanceInitializer { get; } = isInstanceInitializer;

        public List<ISymbol> References { get; } = references;

        public string? Resource { get; } = resource;
    }

    /// <summary>A member that reaches a resource, and the next member on its way there; null when it names the resource itself.</summary>
    private readonly struct Reach(string resource, ISymbol? next)
    {
        public string Resource { get; } = resource;

        public ISymbol? Next { get; } = next;
    }

    private sealed class ReachAnalysis
    {
        private readonly INamedTypeSymbol _resourceAttribute;
        private readonly Dictionary<ISymbol, string> _markedMembers = new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<INamedTypeSymbol, string?> _resourceByType = new(SymbolEqualityComparer.Default);
        private readonly ConcurrentBag<DeclaredMember> _declaredMembers = [];
        private readonly ConcurrentDictionary<INamedTypeSymbol, byte> _declaredTypes = new(SymbolEqualityComparer.Default);

        public ReachAnalysis(INamedTypeSymbol resourceAttribute, INamedTypeSymbol? memberAttribute)
        {
            _resourceAttribute = resourceAttribute;
            if (memberAttribute is null)
            {
                return;
            }

            foreach (var attribute in memberAttribute.ContainingAssembly.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, memberAttribute) || attribute.ConstructorArguments.Length != 3)
                {
                    continue;
                }

                if (attribute.ConstructorArguments[0].Value is string resourceName
                    && attribute.ConstructorArguments[1].Value is INamedTypeSymbol type
                    && attribute.ConstructorArguments[2].Value is string memberName)
                {
                    foreach (var member in type.OriginalDefinition.GetMembers(memberName))
                    {
                        _markedMembers[member.OriginalDefinition] = resourceName;
                    }
                }
            }
        }

        public void Collect(SemanticModelAnalysisContext context)
        {
            var model = context.SemanticModel;
            foreach (var node in model.SyntaxTree.GetRoot(context.CancellationToken).DescendantNodes())
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                switch (node)
                {
                    case ClassDeclarationSyntax classDeclaration when model.GetDeclaredSymbol(classDeclaration, context.CancellationToken) is { } type:
                        _declaredTypes.TryAdd(type, 0);
                        break;
                    case BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax:
                    case VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax }:
                        if (model.GetDeclaredSymbol(node, context.CancellationToken) is { } member)
                        {
                            _declaredMembers.Add(Describe(model, node, member, context));
                        }

                        break;
                }
            }
        }

        public void Report(CompilationAnalysisContext context)
        {
            var reach = PropagateReach();
            foreach (var type in _declaredTypes.Keys)
            {
                if (!HasAttribute(type, "TestClassAttribute") || HasAttribute(type, "DoNotParallelizeAttribute"))
                {
                    continue;
                }

                var methods = type.GetMembers().OfType<IMethodSymbol>().ToList();
                var sharedByEveryTest = methods.Where(static method => method.MethodKind == MethodKind.Constructor || HasAttribute(method, "TestInitializeAttribute") || HasAttribute(method, "TestCleanupAttribute")).ToList();
                foreach (var method in methods)
                {
                    if (!(HasAttribute(method, "TestMethodAttribute") || HasAttribute(method, "DataTestMethodAttribute")) || HasAttribute(method, "DoNotParallelizeAttribute"))
                    {
                        continue;
                    }

                    var reaching = reach.ContainsKey(method) ? method : sharedByEveryTest.FirstOrDefault(reach.ContainsKey);
                    if (reaching is not null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rule, method.Locations[0], $"{type.Name}.{method.Name}", reach[reaching].Resource, PathFrom(reaching, reach)));
                    }
                }
            }
        }

        private DeclaredMember Describe(SemanticModel model, SyntaxNode declaration, ISymbol member, SemanticModelAnalysisContext context)
        {
            var references = new List<ISymbol>();
            string? resource = null;
            foreach (var node in declaration.DescendantNodes())
            {
                if (node is not (SimpleNameSyntax or BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax))
                {
                    continue;
                }

                var info = model.GetSymbolInfo(node, context.CancellationToken);
                if ((info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is not { } referenced || Normalize(referenced) is not { } normalized)
                {
                    continue;
                }

                resource ??= ResourceOf(normalized);
                if (normalized is not INamedTypeSymbol && normalized.Locations.Any(static location => location.IsInSource))
                {
                    references.Add(normalized);
                }
            }

            var isInstanceInitializer = !member.IsStatic && declaration switch
            {
                VariableDeclaratorSyntax variable => variable.Initializer is not null,
                PropertyDeclarationSyntax property => property.Initializer is not null,
                _ => false,
            };
            return new DeclaredMember(member.OriginalDefinition, isInstanceInitializer, references, resource);
        }

        private Dictionary<ISymbol, Reach> PropagateReach()
        {
            var referencedBy = new Dictionary<ISymbol, List<ISymbol>>(SymbolEqualityComparer.Default);
            var reach = new Dictionary<ISymbol, Reach>(SymbolEqualityComparer.Default);
            var pending = new Queue<ISymbol>();
            foreach (var declared in _declaredMembers)
            {
                foreach (var referenced in declared.References)
                {
                    AddReference(referencedBy, referenced, declared.Member);
                }

                if (declared.IsInstanceInitializer)
                {
                    foreach (var constructor in declared.Member.ContainingType.InstanceConstructors)
                    {
                        AddReference(referencedBy, declared.Member, constructor.OriginalDefinition);
                    }
                }

                if (declared.Resource is { } resource && !reach.ContainsKey(declared.Member))
                {
                    reach[declared.Member] = new Reach(resource, null);
                    pending.Enqueue(declared.Member);
                }
            }

            while (pending.Count > 0)
            {
                var member = pending.Dequeue();
                if (!referencedBy.TryGetValue(member, out var callers))
                {
                    continue;
                }

                foreach (var caller in callers)
                {
                    if (!reach.ContainsKey(caller))
                    {
                        reach[caller] = new Reach(reach[member].Resource, member);
                        pending.Enqueue(caller);
                    }
                }
            }

            return reach;
        }

        private static void AddReference(Dictionary<ISymbol, List<ISymbol>> referencedBy, ISymbol referenced, ISymbol referencing)
        {
            if (!referencedBy.TryGetValue(referenced, out var callers))
            {
                referencedBy[referenced] = callers = [];
            }

            callers.Add(referencing);
        }

        private static string PathFrom(ISymbol member, Dictionary<ISymbol, Reach> reach)
        {
            var names = new List<string>();
            for (ISymbol? current = member; current is not null; current = reach[current].Next)
            {
                names.Add($"{current.ContainingType.Name}.{current.Name}");
            }

            return string.Join(" -> ", names);
        }

        private string? ResourceOf(ISymbol symbol)
        {
            if (_markedMembers.TryGetValue(symbol, out var resource))
            {
                return resource;
            }

            for (var type = symbol as INamedTypeSymbol ?? symbol.ContainingType; type is not null; type = type.ContainingType)
            {
                if (_resourceByType.GetOrAdd(type.OriginalDefinition, ResourceDeclaredOn) is { } declared)
                {
                    return declared;
                }
            }

            return null;
        }

        private string? ResourceDeclaredOn(INamedTypeSymbol type)
        {
            foreach (var attribute in type.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _resourceAttribute))
                {
                    return attribute.ConstructorArguments.FirstOrDefault().Value as string;
                }
            }

            return null;
        }

        private static ISymbol? Normalize(ISymbol symbol) => symbol switch
        {
            IMethodSymbol { AssociatedSymbol: IPropertySymbol or IEventSymbol } accessor => accessor.AssociatedSymbol!.OriginalDefinition,
            IMethodSymbol { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction } => null,
            IMethodSymbol method => (method.ReducedFrom ?? method).OriginalDefinition,
            IPropertySymbol or IFieldSymbol or IEventSymbol or INamedTypeSymbol => symbol.OriginalDefinition,
            _ => null,
        };

        private static bool HasAttribute(ISymbol symbol, string attributeName) =>
            symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == attributeName);
    }
}
