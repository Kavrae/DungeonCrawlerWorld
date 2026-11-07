using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Analyzers;

/// <summary>CAD0001: a call returning a type marked MustBeDisposedByUsing must be the resource of a using, or be returned.</summary>
/// <remarks>
/// The attribute is matched by name in any namespace, so a mod's own scope type can opt in. A returned value is allowed because
/// the call that received it is checked in turn; a discarded one, a plain local, a chained member access or an argument is not.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustBeDisposedByUsingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CAD0001";

    private const string AttributeName = "MustBeDisposedByUsingAttribute";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Hold this value in a using",
        "'{0}' must be held in a using: its Dispose is what completes it",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A component write scope versions and announces its write when it is disposed, so a scope never disposed leaves a write no observer hears of.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var returnedType = invocation.TargetMethod.ReturnType;
        if (invocation.TargetMethod.ReturnsByRef || invocation.TargetMethod.ReturnsByRefReadonly || !IsMarked(returnedType))
        {
            return;
        }

        if (IsHeldByUsingOrReturned(invocation))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), returnedType.Name));
    }

    private static bool IsMarked(ITypeSymbol type)
    {
        foreach (var attribute in type.OriginalDefinition.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == AttributeName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHeldByUsingOrReturned(IOperation operation)
    {
        var parent = operation.Parent;
        while (parent is IConversionOperation or IParenthesizedOperation or IConditionalOperation)
        {
            parent = parent.Parent;
        }

        switch (parent)
        {
            case IUsingOperation:
            case IReturnOperation:
                return true;
            case IVariableInitializerOperation initializer:
                return IsDeclaredByUsing(initializer);
            default:
                return false;
        }
    }

    private static bool IsDeclaredByUsing(IVariableInitializerOperation initializer)
    {
        var current = initializer.Parent;
        while (current is IVariableDeclaratorOperation or IVariableDeclarationOperation)
        {
            current = current.Parent;
        }

        return current is IVariableDeclarationGroupOperation group && group.Parent is IUsingOperation or IUsingDeclarationOperation;
    }
}
