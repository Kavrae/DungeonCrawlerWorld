using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Analyzers.ComponentProperties;

/// <summary>Reads a [DescribedComponent] or [DescribedValue] struct into the model the emitter writes from, with its CAD0002 and CAD0003 findings.</summary>
internal static class ComponentPropertyModelBuilder
{
    public const string AttributeNamespace = "Engine.ECS.Properties.";
    public const string DescribedComponentName = AttributeNamespace + "DescribedComponentAttribute";
    public const string DescribedValueName = AttributeNamespace + "DescribedValueAttribute";
    public const string DescribedByHandName = AttributeNamespace + "DescribedByHandAttribute";
    private const string DescribedDefinitionName = AttributeNamespace + "DescribedDefinitionAttribute";
    private const string NotAPropertyName = AttributeNamespace + "NotAPropertyAttribute";
    private const string ComponentPropertyName = AttributeNamespace + "ComponentPropertyAttribute";
    private const string NotSavedName = AttributeNamespace + "NotSavedAttribute";
    private const string ReadOnlyPropertyName = AttributeNamespace + "ReadOnlyPropertyAttribute";
    private const string PropertyMeaningName = AttributeNamespace + "PropertyMeaningAttribute";
    private const string ComponentPropertyAccessName = AttributeNamespace + "ComponentPropertyAccess<T>";
    private const string ScheduledTimerName = "Engine.ECS.Components.IScheduledTimer";

    public static TypeModel Build(GeneratorAttributeSyntaxContext context, bool isComponent, CancellationToken cancellationToken)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        var diagnostics = new List<DiagnosticInfo>();
        var typeLocation = LocationInfo.From(type.Locations.FirstOrDefault());
        var canEmit = true;

        if (!IsPartialEverywhere(type))
        {
            diagnostics.Add(new DiagnosticInfo(ComponentPropertyTableGenerator.NotPartialId, $"{type.Name} must be partial to be described: the generator adds its table inside it", typeLocation));
            canEmit = false;
        }

        if (type.IsGenericType)
        {
            diagnostics.Add(new DiagnosticInfo(ComponentPropertyTableGenerator.NotPartialId, $"{type.Name} is generic, and the generator describes only closed types: describe it by hand", typeLocation));
            canEmit = false;
        }

        var containing = new List<string>();
        for (var outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            if (!IsPartialEverywhere(outer))
            {
                diagnostics.Add(new DiagnosticInfo(ComponentPropertyTableGenerator.NotPartialId, $"{type.Name} is nested in {outer.Name}, which must be partial for {type.Name} to be described", typeLocation));
                canEmit = false;
            }

            containing.Insert(0, $"partial {KeywordOf(outer)} {outer.Name}");
        }

        var members = new List<MemberModel>();
        var describedSyntax = new List<SyntaxNode>();
        foreach (var symbol in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol.IsStatic || symbol.IsImplicitlyDeclared || HasAttribute(symbol, NotAPropertyName))
            {
                continue;
            }

            var isOptedIn = HasAttribute(symbol, ComponentPropertyName);
            if (symbol.DeclaredAccessibility != Accessibility.Public && !isOptedIn)
            {
                continue;
            }

            ITypeSymbol memberType;
            MemberWrite write;
            var isDerived = false;
            var isReadOnly = false;
            switch (symbol)
            {
                case IFieldSymbol { IsConst: false } field:
                    memberType = field.Type;
                    write = field.IsReadOnly ? MemberWrite.None : MemberWrite.Assign;
                    break;
                case IPropertySymbol { IsIndexer: false, GetMethod: not null } property
                    when property.ExplicitInterfaceImplementations.IsEmpty && property.Name != "EqualityContract":
                    memberType = property.Type;
                    write = property.SetMethod switch
                    {
                        null => MemberWrite.None,
                        { IsInitOnly: true } => MemberWrite.With,
                        _ => MemberWrite.Assign,
                    };
                    isReadOnly = property.SetMethod is { DeclaredAccessibility: not Accessibility.Public } && !isOptedIn;
                    isDerived = property.SetMethod is null && !IsAutoProperty(property);
                    break;
                default:
                    continue;
            }

            if (write == MemberWrite.None && !isDerived && !HasAttribute(symbol, NotSavedName))
            {
                diagnostics.Add(new DiagnosticInfo(
                    ComponentPropertyTableGenerator.UndescribableId,
                    $"{type.Name}.{symbol.Name} can be set only by a constructor, so a snapshot can't restore it: make it {{ get; init; }}, with [ReadOnlyProperty] if it names the instance, or leave it out with [NotSaved] or [NotAProperty]",
                    LocationInfo.From(symbol.Locations.FirstOrDefault())));
            }

            describedSyntax.AddRange(symbol.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken)));
            var shape = Classify(memberType);
            if (shape is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    ComponentPropertyTableGenerator.UndescribableId,
                    $"{type.Name}.{symbol.Name} is a {memberType.ToDisplayString()}, which a property table can't describe: describe that type, or leave the member out with [NotAProperty]",
                    LocationInfo.From(symbol.Locations.FirstOrDefault())));
                continue;
            }

            if (write == MemberWrite.None || HasAttribute(symbol, ReadOnlyPropertyName) || shape.Kind is "Definition" or "TagQuery")
            {
                isReadOnly = true;
            }

            var meaning = AttributeArgument(symbol, PropertyMeaningName) ?? (shape.Kind == "Key" ? "EntityKey" : null);
            members.Add(new MemberModel(
                members.Count,
                symbol.Name,
                shape.Kind,
                Full(shape.ValueType),
                meaning,
                shape.IsOptional,
                shape.ValueType.IsValueType,
                write,
                isReadOnly,
                isDerived,
                isDerived || HasAttribute(symbol, NotSavedName),
                shape.NestedAccess,
                IntegerForm(shape.ValueType),
                shape.FlagsMask));
        }

        ReportUndescribedPrivateState(context, type, describedSyntax, diagnostics, cancellationToken);

        return new TypeModel(
            HintName: Full(type).Replace("global::", "").Replace('<', '_').Replace('>', '_') + ".PropertyTable.g.cs",
            Namespace: type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            ContainingDeclarations: containing.ToEquatableArray(),
            Declaration: $"{(type.IsReadOnly ? "readonly " : "")}partial {KeywordOf(type)} {type.Name}",
            FullName: Full(type),
            IsComponent: isComponent,
            CanEmit: canEmit,
            Members: members.ToEquatableArray(),
            Diagnostics: diagnostics.ToEquatableArray());
    }

    public static HandWrittenModel? BuildHandWritten(GeneratorAttributeSyntaxContext context)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        var accessType = context.Attributes.FirstOrDefault()?.ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol;
        if (accessType is null)
        {
            return null;
        }

        var isComponent = false;
        for (var current = accessType.BaseType; current is not null; current = current.BaseType)
        {
            if (current.OriginalDefinition.ToDisplayString() == ComponentPropertyAccessName)
            {
                isComponent = true;
            }
        }

        return new HandWrittenModel(Full(type), Full(accessType), isComponent);
    }

    private sealed class Shape(string kind, bool isOptional, ITypeSymbol valueType, string? nestedAccess = null, string? flagsMask = null)
    {
        public string Kind { get; } = kind;
        public bool IsOptional { get; } = isOptional;
        public ITypeSymbol ValueType { get; } = valueType;
        public string? NestedAccess { get; } = nestedAccess;
        public string? FlagsMask { get; } = flagsMask;
    }

    private static Shape? Classify(ITypeSymbol memberType)
    {
        var isOptional = false;
        var valueType = memberType;
        if (memberType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            isOptional = true;
            valueType = nullable.TypeArguments[0];
        }
        else if (memberType.IsReferenceType && memberType.NullableAnnotation == NullableAnnotation.Annotated)
        {
            isOptional = true;
            valueType = memberType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        }

        switch (valueType.SpecialType)
        {
            case SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64:
                return new Shape("Integer", isOptional, valueType);
            case SpecialType.System_Single or SpecialType.System_Double:
                return new Shape("Number", isOptional, valueType);
            case SpecialType.System_Boolean:
                return new Shape("Boolean", isOptional, valueType);
            case SpecialType.System_String:
                return new Shape("Text", isOptional, valueType);
        }

        if (valueType is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            return HasAttribute(enumType, "System.FlagsAttribute")
                ? new Shape("Flags", isOptional, valueType, flagsMask: FlagsMask(enumType))
                : new Shape("Enum", isOptional, valueType);
        }

        switch (valueType.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString())
        {
            case "System.Guid":
                return new Shape("Guid", isOptional, valueType);
            case "Microsoft.Xna.Framework.Color":
                return new Shape("Color", isOptional, valueType);
            case "Engine.ECS.Entities.EntityKey":
                return new Shape("Key", isOptional, valueType);
            case "Engine.Tags.GameplayTag":
                return new Shape("Tag", isOptional, valueType);
            case "Engine.Tags.GameplayTagQuery":
                return new Shape("TagQuery", isOptional, valueType);
        }

        if (valueType.IsValueType)
        {
            if (HasAttribute(valueType, DescribedComponentName) || HasAttribute(valueType, DescribedValueName))
            {
                return new Shape("Nested", isOptional, valueType, Full(valueType) + ".PropertyAccess");
            }

            if (valueType.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == DescribedByHandName)
                    ?.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol handWrittenAccess)
            {
                return new Shape("Nested", isOptional, valueType, Full(handWrittenAccess));
            }

            return null;
        }

        for (var current = valueType as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (HasAttribute(current, DescribedDefinitionName))
            {
                return new Shape("Definition", isOptional, valueType);
            }
        }

        return null;
    }

    /// <summary>CAD0002 for private state nothing describes: a private instance field no described property's accessor reads, that isn't marked, and isn't a timer wheel's mark.</summary>
    private static void ReportUndescribedPrivateState(
        GeneratorAttributeSyntaxContext context,
        INamedTypeSymbol type,
        List<SyntaxNode> describedSyntax,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        var privateFields = type.GetMembers().OfType<IFieldSymbol>()
            .Where(field => !field.IsStatic && !field.IsConst && !field.IsImplicitlyDeclared
                            && field.DeclaredAccessibility != Accessibility.Public
                            && !HasAttribute(field, NotAPropertyName) && !HasAttribute(field, ComponentPropertyName))
            .ToList();
        if (privateFields.Count == 0)
        {
            return;
        }

        var readers = new List<SyntaxNode>(describedSyntax);
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.ExplicitInterfaceImplementations.Any(implemented =>
                    implemented.Name == "TimerWheelMark" && implemented.ContainingType.ToDisplayString() == ScheduledTimerName))
            {
                readers.AddRange(property.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken)));
            }
        }

        var read = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);
        foreach (var syntax in readers)
        {
            var model = syntax.SyntaxTree == context.SemanticModel.SyntaxTree
                ? context.SemanticModel
                : context.SemanticModel.Compilation.GetSemanticModel(syntax.SyntaxTree);
            foreach (var identifier in syntax.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (model.GetSymbolInfo(identifier, cancellationToken).Symbol is IFieldSymbol field)
                {
                    read.Add(field.OriginalDefinition);
                }
            }
        }

        foreach (var field in privateFields.Where(field => !read.Contains(field.OriginalDefinition)))
        {
            diagnostics.Add(new DiagnosticInfo(
                ComponentPropertyTableGenerator.UndescribableId,
                $"{type.Name}.{field.Name} is private state no described property reads: describe it with [ComponentProperty], or mark it [NotAProperty]",
                LocationInfo.From(field.Locations.FirstOrDefault())));
        }
    }

    private static bool IsAutoProperty(IPropertySymbol property)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax())
            {
                case ParameterSyntax:
                    return true;
                case PropertyDeclarationSyntax declaration:
                    return declaration.ExpressionBody is null
                           && declaration.AccessorList is not null
                           && declaration.AccessorList.Accessors.All(accessor => accessor.Body is null && accessor.ExpressionBody is null);
            }
        }

        return false;
    }

    private static bool IsPartialEverywhere(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.All(reference =>
            reference.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static string KeywordOf(INamedTypeSymbol type)
    {
        if (type.IsRecord)
        {
            return type.IsValueType ? "record struct" : "record";
        }

        return type.TypeKind switch
        {
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            _ => "class",
        };
    }

    private static string IntegerForm(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_UInt64 => "Unsigned64",
        SpecialType.System_Int64 => "Signed64",
        _ => "Checked",
    };

    private static string FlagsMask(INamedTypeSymbol enumType)
    {
        ulong mask = 0;
        foreach (var field in enumType.GetMembers().OfType<IFieldSymbol>().Where(field => field.HasConstantValue))
        {
            mask |= field.ConstantValue switch
            {
                byte value => value,
                ushort value => value,
                uint value => value,
                ulong value => value,
                sbyte value => unchecked((ulong)value),
                short value => unchecked((ulong)value),
                int value => unchecked((ulong)value),
                long value => unchecked((ulong)value),
                _ => 0,
            };
        }

        return mask.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName) =>
        symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == attributeName);

    private static string? AttributeArgument(ISymbol symbol, string attributeName) =>
        symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == attributeName)
            ?.ConstructorArguments.FirstOrDefault().Value as string;

    public static string Full(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
