using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Analyzers.ComponentProperties;

/// <summary>An array compared by its items, so the generator's models compare by value between runs and an unchanged struct is never re-emitted.</summary>
internal readonly struct EquatableArray<T>(T[] items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items = items;

    public int Count => _items?.Length ?? 0;

    public T this[int index] => _items![index];

    public bool Equals(EquatableArray<T> other)
    {
        var mine = _items ?? [];
        var theirs = other._items ?? [];
        if (mine.Length != theirs.Length)
        {
            return false;
        }

        for (var index = 0; index < mine.Length; index++)
        {
            if (!mine[index].Equals(theirs[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in _items ?? [])
        {
            hash = (hash * 31) + item.GetHashCode();
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Where a diagnostic points, kept as values so the model stays comparable.</summary>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationInfo? From(Location? location) =>
        location is { IsInSource: true } ? new LocationInfo(location.SourceTree!.FilePath, location.SourceSpan, location.GetLineSpan().Span) : null;
}

internal sealed record DiagnosticInfo(string Id, string Message, LocationInfo? Location);

internal enum MemberWrite
{
    None,
    Assign,
    With,
}

/// <summary>One member of a described type, as the emitter needs it.</summary>
internal sealed record MemberModel(
    int Index,
    string Name,
    string Kind,
    string ValueType,
    string? Meaning,
    bool IsOptional,
    bool ValueIsValueType,
    MemberWrite Write,
    bool IsReadOnly,
    bool IsDerived,
    bool IsNotSaved,
    string? NestedAccess,
    string IntegerForm,
    string? FlagsMask);

/// <summary>One described struct: where it is declared, its members, and what is wrong with it.</summary>
internal sealed record TypeModel(
    string HintName,
    string? Namespace,
    EquatableArray<string> ContainingDeclarations,
    string Declaration,
    string FullName,
    bool IsComponent,
    bool CanEmit,
    EquatableArray<MemberModel> Members,
    EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>A struct whose access is hand-written: registered by the module initializer when it is a component's.</summary>
internal sealed record HandWrittenModel(string FullName, string AccessFullName, bool IsComponent);

internal static class EquatableArrayExtensions
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items)
        where T : IEquatable<T> => new(items.ToArray());
}
