namespace Chess;

using Xtofs.EnumClass;

/// <summary>What a chess square holds: nothing, or a <see cref="Piece"/>.</summary>
[EnumClass]
[Includes<Piece>]
public readonly partial struct Occupancy
{
    // Own values first (ordinal 0 = default), then Piece's 12 values.
    private static Table Members => new()
    {
        /// <summary>No piece on the square.</summary>
        Empty = new(),
    };
}
