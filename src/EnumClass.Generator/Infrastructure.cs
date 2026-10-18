using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace System.Runtime.CompilerServices
{
    // Polyfill so records compile against netstandard2.0.
    internal static class IsExternalInit { }
}

namespace EnumClass.Generator
{
    /// <summary>Value-equality wrapper so incremental-pipeline models cache correctly.</summary>
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
        where T : IEquatable<T>
    {
        private readonly T[]? _items;

        public EquatableArray(T[] items) => _items = items;

        public int Length => _items?.Length ?? 0;
        public T this[int index] => _items![index];
        public T[] ToArray() => _items is null ? Array.Empty<T>() : (T[])_items.Clone();

        public bool Equals(EquatableArray<T> other)
        {
            if (Length != other.Length) return false;
            var cmp = EqualityComparer<T>.Default;
            for (int i = 0; i < Length; i++)
                if (!cmp.Equals(_items![i], other._items![i])) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is EquatableArray<T> o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                if (_items is not null)
                    foreach (var item in _items) h = h * 31 + (item?.GetHashCode() ?? 0);
                return h;
            }
        }

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? Array.Empty<T>())).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static class EquatableArray
    {
        public static EquatableArray<T> From<T>(IEnumerable<T> items) where T : IEquatable<T> => new(items.ToArray());
    }

    /// <summary>Location without a reference to the SyntaxTree (keeps the pipeline cache-friendly).</summary>
    internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
    {
        public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

        public static LocationInfo? From(Location? location) =>
            location is null || location.SourceTree is null
                ? null
                : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> Args)
    {
        public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location? location, params object[] args) =>
            new(descriptor, LocationInfo.From(location),
                EquatableArray.From(args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture) ?? "")));

        public Diagnostic ToDiagnostic() =>
            Diagnostic.Create(Descriptor, Location?.ToLocation(), Args.ToArray().Cast<object>().ToArray());
    }

    internal sealed class Writer
    {
        private readonly StringBuilder _sb = new();
        private int _indent;

        public Writer Line(string text = "")
        {
            if (text.Length > 0) _sb.Append(' ', _indent * 4).Append(text);
            _sb.Append('\n');
            return this;
        }

        public Writer Open(string header)
        {
            Line(header).Line("{");
            _indent++;
            return this;
        }

        public Writer Close(string suffix = "")
        {
            _indent--;
            return Line("}" + suffix);
        }

        public override string ToString() => _sb.ToString();
    }
}
