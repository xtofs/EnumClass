namespace Planets;

using Xtofs.EnumClass;

[EnumClass]
public readonly partial struct Planet
{
    // Associated data: instance partial properties. Their declaration order is the
    // parameter order of the generated Row constructor used in Members below.
    /// <summary>Mass in kg.</summary>
    public partial double Mass { get; }
    /// <summary>Equatorial radius in m.</summary>
    public partial double Radius { get; }
    public partial bool HasRings { get; }
    /// <summary>Sidereal orbital period (not expressible as an attribute argument).</summary>
    public partial TimeSpan OrbitalPeriod { get; }

    // Members: one entry each; initializer order = ordinal.
    // Doc comments on entries are copied to the generated Planet.Mercury etc.
    private static Table Members => new()
    {
        /// <summary>Innermost planet.</summary>
        Mercury = new(3.303e+23, 2.4397e6, false, TimeSpan.FromDays(87.969)),
        Venus = new(4.869e+24, 6.0518e6, false, TimeSpan.FromDays(224.701)),
        /// <summary>Home.</summary>
        Earth = new(5.976e+24, 6.37814e6, false, TimeSpan.FromDays(365.256)),
        Mars = new(6.421e+23, 3.3972e6, false, TimeSpan.FromDays(686.980)),
        Jupiter = new(1.9e+27, 7.1492e7, true, TimeSpan.FromDays(4332.59)),
        Saturn = new(5.688e+26, 6.0268e7, true, TimeSpan.FromDays(10759.22)),
        Uranus = new(8.686e+25, 2.5559e7, true, TimeSpan.FromDays(30688.5)),
        Neptune = new(Mass: 1.024e+26, Radius: 2.4746e7, HasRings: true, OrbitalPeriod: TimeSpan.FromDays(60182)),
    };

    /// <summary>Newtonian gravitational constant  in m³/(kg·s²).</summary>
    const double G = 6.67300e-11; // gravitational constant

    // Hand-written members can build on the generated data.
    public double SurfaceGravity => G * Mass / (Radius * Radius);
}
