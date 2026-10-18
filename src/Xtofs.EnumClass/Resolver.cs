using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Xtofs.EnumClass.Generator
{
    /// <summary>
    /// Flattens [Includes&lt;T&gt;]: a type's values are its own values (ordinals 0..k-1)
    /// followed by each included type's values, block by block in attribute order.
    /// An included type's values are themselves flattened, so includes nest.
    /// </summary>
    internal static class Resolver
    {
        private readonly record struct Flat(string[]? Names, string? Error);

        public static ImmutableArray<EmitModel> ResolveAll(ImmutableArray<TypeModel> all, CancellationToken ct)
        {
            var byName = new Dictionary<string, TypeModel>(StringComparer.Ordinal);
            foreach (var m in all)
                if (!byName.ContainsKey(m.FullName)) byName.Add(m.FullName, m);

            var memo = new Dictionary<string, Flat>(StringComparer.Ordinal);
            var result = ImmutableArray.CreateBuilder<EmitModel>(all.Length);
            foreach (var m in all)
            {
                ct.ThrowIfCancellationRequested();
                result.Add(Resolve(m, byName, memo));
            }
            return result.MoveToImmutable();
        }

        private static EmitModel Resolve(TypeModel m, Dictionary<string, TypeModel> byName, Dictionary<string, Flat> memo)
        {
            var diags = new List<DiagnosticInfo>(m.Diagnostics);
            EmitModel Result(bool canEmit, List<string>? names = null, List<ResolvedInclude>? includes = null,
                             bool fold = false, int[]? g = null, int[]? slots = null) =>
                new(m, EquatableArray.From(names ?? new()), EquatableArray.From(includes ?? new()),
                    fold, new EquatableArray<int>(g ?? Array.Empty<int>()), new EquatableArray<int>(slots ?? Array.Empty<int>()),
                    EquatableArray.From(diags), canEmit);

            if (!m.CanEmit) return Result(false);

            bool ok = true;
            var names = m.Members.Select(x => x.Name).ToList();
            var origin = names.Select(_ => "own value").ToList();
            var includes = new List<ResolvedInclude>();
            var stack = new HashSet<string>(StringComparer.Ordinal) { m.FullName };

            foreach (var inc in m.Includes)
            {
                if (includes.Any(r => r.FullName == inc.FullName))
                {
                    diags.Add(Diag(Diagnostics.InvalidInclude, inc.Location, inc.SimpleName, "it is included more than once"));
                    ok = false;
                    continue;
                }
                if (includes.Any(r => r.SimpleName == inc.SimpleName))
                {
                    diags.Add(Diag(Diagnostics.InvalidInclude, inc.Location, inc.SimpleName,
                        $"another included type is also named '{inc.SimpleName}' (Is{inc.SimpleName}/TryGet{inc.SimpleName} would clash)"));
                    ok = false;
                    continue;
                }

                var flat = FlattenInclude(inc, byName, memo, stack);
                if (flat.Names is null)
                {
                    diags.Add(Diag(Diagnostics.InvalidInclude, inc.Location, inc.SimpleName, flat.Error!));
                    ok = false;
                    continue;
                }

                includes.Add(new ResolvedInclude(inc.FullName, inc.DisplayName, inc.SimpleName, names.Count, flat.Names.Length));
                names.AddRange(flat.Names);
                origin.AddRange(flat.Names.Select(_ => "included from " + inc.SimpleName));
            }
            if (!ok) return Result(false);

            // Value names must be unique across own values and all blocks.
            var firstIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                if (firstIndex.TryGetValue(names[i], out int first))
                {
                    diags.Add(Diag(Diagnostics.DuplicateValueName, m.Location, names[i], m.Name, $"{origin[first]} and {origin[i]}"));
                    ok = false;
                }
                else firstIndex.Add(names[i], i);
            }

            if (names.Count > 65536)
            {
                diags.Add(Diag(Diagnostics.TooManyMembers, m.Location, m.Name, names.Count));
                ok = false;
            }

            // Members generated per include must not collide with own values or user members.
            var existing = new HashSet<string>(m.ExistingMemberNames, StringComparer.Ordinal);
            foreach (var inc in includes)
            {
                foreach (var generated in new[] { "Is" + inc.SimpleName, "TryGet" + inc.SimpleName })
                {
                    if (m.Members.Any(x => x.Name == generated))
                    {
                        diags.Add(Diag(Diagnostics.NameConflict, m.Location, generated, $"the member generated for including '{inc.SimpleName}'"));
                        ok = false;
                    }
                    else if (existing.Contains(generated))
                    {
                        diags.Add(Diag(Diagnostics.NameConflict, m.Location, generated, $"existing member '{generated}' (generated for including '{inc.SimpleName}')"));
                        ok = false;
                    }
                }
            }
            if (!ok) return Result(false);

            // Perfect hash over all value names.
            var keys = names.ToArray();
            bool fold = true;
            int[]? g = null, slots = null;
            if (PerfectHashBuilder.TryBuild(keys, fold: true, out var g1, out var s1))
            {
                g = g1; slots = s1;
            }
            else
            {
                fold = false;
                if (keys.Select(PerfectHashBuilder.FoldAscii).Distinct().Count() != keys.Length)
                    diags.Add(Diag(Diagnostics.CaseCollision, m.Location, m.Name));
                if (PerfectHashBuilder.TryBuild(keys, fold: false, out var g2, out var s2)) { g = g2; slots = s2; }
            }

            return Result(true, names, includes, fold, g, slots);
        }

        private static Flat FlattenInclude(IncludeModel inc, Dictionary<string, TypeModel> byName,
                                           Dictionary<string, Flat> memo, HashSet<string> stack)
        {
            if (!inc.FromSource) return new Flat(inc.Names.ToArray(), null);
            if (stack.Contains(inc.FullName)) return new Flat(null, "the includes form a cycle");
            if (!byName.TryGetValue(inc.FullName, out var target)) return new Flat(null, "no [EnumClass] model was found for it");
            return Flatten(target, byName, memo, stack);
        }

        /// <summary>All values of <paramref name="m"/>, own first, then its included blocks.</summary>
        private static Flat Flatten(TypeModel m, Dictionary<string, TypeModel> byName,
                                    Dictionary<string, Flat> memo, HashSet<string> stack)
        {
            if (memo.TryGetValue(m.FullName, out var cached)) return cached;
            if (!m.CanEmit) return memo[m.FullName] = new Flat(null, "it has errors");

            stack.Add(m.FullName);
            var names = m.Members.Select(x => x.Name).ToList();
            Flat result = default;
            foreach (var inc in m.Includes)
            {
                var flat = FlattenInclude(inc, byName, memo, stack);
                if (flat.Names is null)
                {
                    result = new Flat(null, flat.Error == "the includes form a cycle" ? flat.Error : "it has errors");
                    break;
                }
                names.AddRange(flat.Names);
            }
            stack.Remove(m.FullName);

            if (result.Error is null) result = new Flat(names.ToArray(), null);
            return memo[m.FullName] = result;
        }

        private static DiagnosticInfo Diag(Microsoft.CodeAnalysis.DiagnosticDescriptor d, LocationInfo? loc, params object[] args) =>
            new(d, loc, EquatableArray.From(args.Select(a => Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture) ?? "")));
    }
}
