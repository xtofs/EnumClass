# EnumClass — sketch of a source generator for data-carrying enums

Status: **design sketch, not compiled.** No .NET SDK was reachable where this was
written. The perfect-hash construction was verified separately (bit-exact Python
port, 3 000 random key sets of 1–1 000 keys plus sequential `Item0…Item4999`;
every set built, every key round-tripped, case-folded lookups included, max seed 259).

## Authoring surface (C# 13+ partial properties)

```csharp
[DataEnum]
public readonly partial struct Planet
{
    public partial double Mass { get; }      // data, bound positionally
    public partial double Radius { get; }

    [Member(3.303e+23, 2.4397e6)] public static partial Planet Mercury { get; }
    [Member(5.976e+24, 6.37814e6)] public static partial Planet Earth { get; }
}
```

Why attributes: values must be **compile-time constants** so the generator can
(a) type-check them against the property types (EC004), (b) emit literal tables,
and (c) build the name hash at compile time. Non-constant data (a `TimeSpan`,
a reference to another data enum) goes into ordinary hand-written members that
switch on `ToTag()` or index by `Ordinal`.

## What is generated

| Piece                                                         | Shape                                                                                                                                                                                                 | Cost                      |
| ------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------- |
| Representation                                                | single `byte` (≤ 256 members) or `ushort` field                                                                                                                                                       | 1–2 bytes, no allocation  |
| Member access                                                 | `static partial Planet Earth { get => new(2); }`                                                                                                                                                      | constant                  |
| Data property                                                 | `get => s_Mass[_ordinal]` via `Unsafe.Add`, no bounds check                                                                                                                                           | one load                  |
| `Name` / `ToString()`                                         | member identifier (unless you declare `Name` yourself)                                                                                                                                                | one load                  |
| `Values`, `Count`, `Ordinal`, `FromOrdinal`, `TryFromOrdinal` |                                                                                                                                                                                                       |                           |
| `Tag`                                                         | nested plain `enum` mirror for constant-pattern `switch`                                                                                                                                              |                           |
| `TryParse` / `Parse`                                          | minimal perfect hash → one candidate → one comparison                                                                                                                                                 | 2 hash passes + 1 compare |
| `Set`                                                         | `k = ⌈n/64⌉` unrolled `ulong` words; `\| & ^ - ~`, `Contains`, `Count` (popcnt), subset/superset/overlap, ref-struct enumerator (tzcnt), collection-expression builder; `Bits`/`FromBits` when n ≤ 64 | branch-free for n ≤ 64    |

`Planet | Planet` yields a `Planet.Set`; a `Planet` converts implicitly to a
singleton `Set`, so `set | Planet.Earth` works too.

## Perfect hash

Hash-and-displace (CHD family, Hanov's simplification), minimal: n buckets, n slots.

```
b    = Reduce(H(key, 0), n)          // Lemire multiply-shift, no modulo
d    = G[b]
slot = d < 0 ? -d - 1 : Reduce(H(key, d), n)
```

`H` = seeded FNV-1a over UTF-16 code units + murmur3 `fmix32`. Keys are hashed
ASCII-case-folded, so one table serves both case-sensitive (ordinal compare) and
`ignoreCase` (`Ascii.EqualsIgnoreCase`) lookups. If two members differ only by
ASCII case, it falls back to an exact-key table plus a linear scan for
`ignoreCase` (EC008). `G` and the slot→ordinal map are `ReadOnlySpan<T>`
properties over constants, so they live in the PE image.

**Honest expectation:** Roslyn already compiles `switch` on strings/spans into
length- and character-based dispatch. A PHF wins mainly for case-insensitive
span lookup and larger n; benchmark against a plain `switch` before relying on it.

## Precise semantics worth knowing

- `default(Planet)` is the first member (like `enum` value 0). To make `default`
  invalid instead, store `ordinal + 1` and add a check in `GetAt`.
- Ordinals follow declaration order; EC006 warns if members span files.
- `ignoreCase` folds **ASCII letters only** — not `StringComparison.OrdinalIgnoreCase`
  (which also equates e.g. U+212A KELVIN SIGN with `k`).
- Value checks use `ClassifyCommonConversion`, so `int` → `byte` is rejected
  (no constant conversion); write `(byte)3`.
- `[Member(null)]` binds the `params` array in normal form; the generator treats
  it as one `null` value. A single `string[]` argument would also bind in normal
  form (array covariance) and be read as _several_ values — a known gotcha.
- Generated helpers (`EnumClass.Internal.PerfectHash`, attributes) are `internal`;
  two assemblies with `InternalsVisibleTo` between them will see CS0436 duplicates.
  On Roslyn ≥ 4.14, `AddEmbeddedAttributeDefinition()` + `[Embedded]` fixes that.

## Requirements

Generator: `netstandard2.0`, Microsoft.CodeAnalysis.CSharp ≥ 4.12 (partial-property
symbol APIs). Consumer: C# 13+ and .NET 8+ (`Ascii`, `CollectionBuilder`,
`MemoryMarshal.GetArrayDataReference`, `RuntimeHelpers.CreateSpan`).

## Unverified API assumptions (check first when compiling)

- `IPropertySymbol.IsPartialDefinition` / `PartialImplementationPart` (Roslyn 4.12).
- `static partial` properties and expression-bodied `get =>` in the implementing part.
- Target-typed `new(i)` as collection-expression elements of `Planet[]`.

## Natural next steps

`ISpanParsable<T>` / `ISpanFormattable`, a JSON converter, `IEnumerable<T>` on
`Set` (boxing) or `ToArray()`, configurable set type name, `[Flags]`-style
persistence for k > 1, and an analyzer for name clashes with generated members
(`Count`, `Values`, `Set`, `Tag`, …). Tests: a `CSharpGeneratorDriver` snapshot
test plus a property test that `PerfectHashBuilder.Hash` equals the runtime hash.
