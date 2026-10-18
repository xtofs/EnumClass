

namespace Sample;

using System.Numerics;

using EnumClass;

[EnumClass]
public readonly partial struct Planet
{
    // Associated data: instance partial properties, bound positionally by [Member(...)].
    /// <summary>Mass in kg.</summary>
    public partial double Mass { get; }
    /// <summary>Equatorial radius in m.</summary>
    public partial double Radius { get; }
    public partial bool HasRings { get; }

    // Members: ordinal = declaration order.
    [Member(3.303e+23, 2.4397e6, false)] public static partial Planet Mercury { get; }
    [Member(4.869e+24, 6.0518e6, false)] public static partial Planet Venus { get; }
    [Member(5.976e+24, 6.37814e6, false)] public static partial Planet Earth { get; }
    [Member(6.421e+23, 3.3972e6, false)] public static partial Planet Mars { get; }
    [Member(1.9e+27, 7.1492e7, true)] public static partial Planet Jupiter { get; }
    [Member(5.688e+26, 6.0268e7, true)] public static partial Planet Saturn { get; }
    [Member(8.686e+25, 2.5559e7, true)] public static partial Planet Uranus { get; }
    [Member(1.024e+26, 2.4746e7, true)] public static partial Planet Neptune { get; }


    /// <summary>Newtonian gravitational constant  in m³/(kg·s²).</summary>
    const double G = 6.67300e-11; // gravitational constant

    // Hand-written members can build on the generated data.
    public double SurfaceGravity => G * Mass / (Radius * Radius);
}
