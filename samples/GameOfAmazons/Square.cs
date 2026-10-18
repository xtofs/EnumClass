namespace GameOfAmazons;

using Xtofs.EnumClass;

/// <summary>What a square holds: nothing, a burnt square, or an <see cref="Amazon"/>.</summary>
[EnumClass]
[Includes<Amazon>]
public readonly partial struct Square
{
    // Own values first (Empty = 0 = default, Fire = 1), then Amazon's 2 values.
    private static Table Members => new()
    {
        /// <summary>Free to move through or burn.</summary>
        Empty = new(),
        /// <summary>Burnt by an arrow; blocked for the rest of the game.</summary>
        Fire = new(),
    };
}
