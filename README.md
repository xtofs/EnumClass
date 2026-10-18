# EnumClass — a source generator for data-carrying enums

A `readonly partial struct` marked `[EnumClass]` becomes a closed set of members,
each with typed associated data, plus a strongly typed bit set over those members.

## Authoring surface

```csharp
[EnumClass]
public readonly partial struct Planet
{
    public partial double Mass { get; }               // data properties: their declaration
    public partial TimeSpan OrbitalPeriod { get; }    // order is the Row parameter order

    private static Table Data => new()
    {
        /// <summary>Innermost planet.</summary>       // copied to Planet.Mercury
        Mercury = new(3.303e+23, TimeSpan.FromDays(87.969)),
        Earth   = new(Mass: 5.976e+24, OrbitalPeriod: TimeSpan.FromDays(365.256)),
    };
}
```

- **The initializer is the single source of truth.** Each `Name = new(...)` entry
  declares a member; its position is the member's ordinal.
- **Values are ordinary C# expressions**, type-checked by the compiler against the
  generated `Row` constructor: `Vector2`, `TimeSpan`, other EnumClass members, etc.
- **Doc comments** (`///`) in front of an entry are copied to the generated member
  property. The `DocCommentSuppressor` shipped with the generator suppresses the
  resulting CS1587 only at those positions. The user's `using` directives are copied
  into the generated file, so `cref`s bind as written.
- `Data` is an ordinary (non-partial) static property. `=> new() { ... }`,
  `{ get => ...; }` and `{ get { return new Table { ... }; } }` are accepted.
  It must not be a field: its initializer and the generated `s_table = Data`
  would sit in different partial declarations, whose initializer order is unspecified.

## Supersets: `[Includes<T>]`

A type can contain all values of other EnumClass types, plus its own:

```csharp
[EnumClass]
[Includes<Piece>]
public readonly partial struct Occupancy
{
    private static Table Data => new() { Empty = new() };
}
```

- **Ordinals:** own values first (0..k-1, so `default` is the first own value, here
  `Empty`, and `new Occupancy[64]` is an empty board), then each included type's
  values as one contiguous block, in attribute order. Own ordinals never depend on
  an included type; growing an included type shifts only the blocks after it.
- **Per include `T`:** implicit widening `T → Occupancy` (`ordinal + offset`), an
  explicit narrowing cast (throws `InvalidCastException`), `IsT`, `TryGetT(out T)`.
- **Sets:** implicit widening `T.Set → Occupancy.Set` and `T → Occupancy.Set`, and
  `Occupancy.Set.ToTSet()`; both are one or two constant shifts per 64-bit word.
- **`Tag`, `Name`, `ToString`, `TryParse`, `Values`** cover all values, own and included,
  so one `switch` on `ToTag()` handles every case.
- **Includes nest:** including `Occupancy` brings in its `Empty` and all of `Piece`'s
  values. Widening is one step: `Cell c = (Occupancy)Piece.BlackPawn;`.
- **Where `T` lives:** in the same compilation (resolved from `T`'s own `Data`
  initializer, since its generated members do not exist yet) or in a referenced
  assembly (read from its public `Tag` enum).
- **Not supported (EC015):** data properties on a type with includes, since included
  values cannot supply them. Use `TryGetPiece(out var p) && p.Color == ...`.
- Declare all `[Includes]` on one partial declaration; attribute order across
  declarations is not specified.

## What is generated

All of it lands in `<Namespace>.<Type>.EnumClass.g.cs` as part of the same struct.

| Piece | Shape | Cost |
|---|---|---|
| Representation | single `byte` (≤ 256 members) or `ushort` field | 1–2 bytes, no allocation |
| Members | `public static Planet Earth => new(2);` | constant |
| `Row`, `Table` | private input types of `Data`; `Table` has one `Row` property per entry | — |
| Associated-data property | `get => GetAt(s_Mass)`: array load via `Unsafe.Add`, no bounds check | one load |
| Columns | filled once from `Data` during type initialization | n·k copies, once |
| `Name` / `ToString()` | member identifier (unless you declare `Name` yourself) | one load |
| `Values`, `Count`, `Ordinal`, `FromOrdinal`, `TryFromOrdinal` | | |
| `Tag` | nested plain `enum` mirror for constant-pattern `switch` | |
| `TryParse` / `Parse` | minimal perfect hash → one candidate → one comparison | 2 hash passes + 1 compare |
| `Set` | `k = ⌈n/64⌉` unrolled `ulong` words (`WordCount`, `GetWord`, `FromWords`); `\| & ^ - ~`, `Contains`, `Count` (popcnt), subset/superset/overlap, ref-struct enumerator (tzcnt), collection-expression builder; `Bits`/`FromBits` when n ≤ 64 | branch-free for n ≤ 64 |

`Planet | Planet` yields a `Planet.Set`; a `Planet` converts implicitly to a
singleton `Set`, so `set | Planet.Earth` works too.

## Perfect hash

Used **only** for name → ordinal in `TryParse`/`Parse`; the ordinal itself is the
initializer index. Hash-and-displace (CHD family, Hanov's simplification), minimal:
n buckets, n slots.

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

Roslyn already compiles `switch` on strings/spans into length- and character-based
dispatch. A PHF wins mainly for case-insensitive span lookup and larger n;
benchmark against a plain `switch` before relying on it.

## Diagnostics

| ID | Severity | Meaning |
|---|---|---|
| EC001 | Error | Target is not a non-nested, non-generic `readonly partial struct` |
| EC005 | Error | Associated-data property has a setter or is an indexer |
| EC007 | Error | `Data` initializer assigns no members |
| EC008 | Warning | Members differ only by ASCII case |
| EC009 | Error | More than 65 536 members |
| EC010 | Error | No `Data` property |
| EC011 | Error | `Data` is not a static property returning one object creation with `Name = expr` entries (a field or method is rejected too) |
| EC012 | Error | Member name collides with the type, a data property, an existing or a generated member |
| EC013 | Error | A value name occurs twice (own and included, or in two included types) |
| EC014 | Error | Invalid include: not an EnumClass, has errors, included twice, cycle, or two included types with the same simple name |
| EC015 | Error | Data properties on a type with includes |

Duplicate entries are reported by the compiler (CS1912); wrong value types and
arity by ordinary overload resolution on `Row`'s constructor.

## Precise semantics worth knowing

- `default(Planet)` is the first member (like `enum` value 0). To make `default`
  invalid instead, store `ordinal + 1` and add a check in `GetAt`.
- The `Data` getter runs **once**, during type initialization. The type is `beforefieldinit`,
  so the runtime runs it no later than the first static-field access (data
  properties, `Name`, `ToString`, `Values`, `TryParse`), possibly earlier; getting a
  member (`Planet.Earth`) does not itself require it. An exception in `Data`
  surfaces as `TypeInitializationException` and leaves the type unusable.
- Inside `Data`, referring to members (`Next = Planet.Earth`) is fine; reading
  their **data** (`Planet.Earth.Mass`) is not — the columns are not filled yet.
- Static field initializers in *your* part of the struct must not read data
  properties either: the order of initializers across partial declarations is
  unspecified.
- `Data` must be `private`, because `Table` and `Row` are private (anything wider
  is CS0050).
- `ignoreCase` folds **ASCII letters only** — not `StringComparison.OrdinalIgnoreCase`
  (which also equates e.g. U+212A KELVIN SIGN with `k`).
- Members are emitted as plain static properties, so F12 on `Planet.Earth` lands in
  the generated file; hover shows the copied doc comment.
- Generated helpers (`EnumClass.Internal.PerfectHash`, attributes) are `internal`;
  two assemblies with `InternalsVisibleTo` between them will see CS0436 duplicates.
  On Roslyn ≥ 4.14, `AddEmbeddedAttributeDefinition()` + `[Embedded]` fixes that.

## Requirements

Generator: `netstandard2.0`, Microsoft.CodeAnalysis.CSharp ≥ 4.12 (partial-property
symbol APIs). Consumer: C# 13+ and .NET 8+ (`Ascii`, `CollectionBuilder`,
`MemoryMarshal.GetArrayDataReference`).

## Natural next steps

`ISpanParsable<T>` / `ISpanFormattable`, a JSON converter, `IEnumerable<T>` on
`Set` (boxing) or `ToArray()`, optional `Row` parameters with defaults,
`[Flags]`-style persistence for k > 1. Tests: a `CSharpGeneratorDriver` snapshot
test plus a property test that `PerfectHashBuilder.Hash` equals the runtime hash.
