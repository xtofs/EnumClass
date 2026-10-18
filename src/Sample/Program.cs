using Sample;

Planet.Set inner = [Planet.Mercury, Planet.Venus, Planet.Earth, Planet.Mars]; // collection expression
Planet.Set ringed = Planet.Jupiter | Planet.Saturn | Planet.Uranus | Planet.Neptune;

Console.WriteLine(~ringed == inner);                         // True
Console.WriteLine(inner.Contains(Planet.Earth));             // True
Console.WriteLine((inner | Planet.Saturn).Count);            // 5
Console.WriteLine(inner);                                    // [Mercury, Venus, Earth, Mars]

foreach (var p in Planet.Values)
{
    Console.WriteLine($"{p.Name,-8} g = {p.SurfaceGravity:F2} m/s², year = {p.OrbitalPeriod.TotalDays:F0} d");
}

if (Planet.TryParse("saturn", ignoreCase: true, out var s))
    Console.WriteLine($"{s} has rings: {s.HasRings}");

// Constant-pattern switch via the mirrored Tag enum
static string Describe(Planet p) => p.ToTag() switch
{
    Planet.Tag.Earth => "home",
    Planet.Tag.Mars => "next",
    _ => "elsewhere",
};
Console.WriteLine(Describe(Planet.Mars));
