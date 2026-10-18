namespace Sample2;

using EnumClass;

/// <summary>What a chess square holds: nothing, or a <see cref="Piece"/>.</summary>
[EnumClass]
[Includes<Piece>]
public readonly partial struct Occupancy
{
    // Own values first (ordinal 0 = default), then Piece's 12 values.
    private static Table Data => new()
    {
        /// <summary>No piece on the square.</summary>
        Empty = new(),
    };
}

public enum AmazonColor { White, Black }

[EnumClass]
public readonly partial struct Amazon
{
    public partial AmazonColor Color { get; }

    private static Table Data => new()
    {
        WhiteAmazon = new(AmazonColor.White),
        BlackAmazon = new(AmazonColor.Black),
    };
}

/// <summary>What a Game of the Amazons square holds: nothing, a burnt square, or an amazon.</summary>
[EnumClass]
[Includes<Amazon>]
public readonly partial struct AmazonsSquare
{
    private static Table Data => new()
    {
        Empty = new(),
        /// <summary>Burnt by an arrow; blocked for the rest of the game.</summary>
        Fire = new(),
    };
}
