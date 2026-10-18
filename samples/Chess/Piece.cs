namespace Chess;

using Xtofs.EnumClass;

[EnumClass]
public readonly partial struct Piece
{
    public partial string Name { get; }
    public partial PieceColor Color { get; }
    public partial PieceType Type { get; }
    public partial char AlgebraicNotationSymbol { get; }

    private static Table Members => new()
    {
        WhiteKing = new("White King", PieceColor.White, PieceType.King, 'K'),
        WhiteQueen = new("White Queen", PieceColor.White, PieceType.Queen, 'Q'),
        WhiteRook = new("White Rook", PieceColor.White, PieceType.Rook, 'R'),
        WhiteBishop = new("White Bishop", PieceColor.White, PieceType.Bishop, 'B'),
        WhiteKnight = new("White Knight", PieceColor.White, PieceType.Knight, 'N'),
        WhitePawn = new("White Pawn", PieceColor.White, PieceType.Pawn, 'P'),

        BlackKing = new("Black King", PieceColor.Black, PieceType.King, 'k'),
        BlackQueen = new("Black Queen", PieceColor.Black, PieceType.Queen, 'q'),
        BlackRook = new("Black Rook", PieceColor.Black, PieceType.Rook, 'r'),
        BlackBishop = new("Black Bishop", PieceColor.Black, PieceType.Bishop, 'b'),
        BlackKnight = new("Black Knight", PieceColor.Black, PieceType.Knight, 'n'),
        BlackPawn = new("Black Pawn", PieceColor.Black, PieceType.Pawn, 'p'),
    };

}

public enum PieceColor
{
    White,
    Black
}

/// <summary>
/// Represents the type of a chess piece, including its letter and value.
/// For demonstration purposes, it is modeled as a EnumClass with 
/// associated data instead of a traditional enum.
/// </summary>
[EnumClass]
public readonly partial struct PieceType
{
    public partial char Letter { get; }
    public partial int Value { get; }
    private static Table Members => new()
    {
        King = new('K', 0),
        Queen = new('Q', 9),
        Rook = new('R', 5),
        Bishop = new('B', 3),
        Knight = new('N', 3),
        Pawn = new('P', 1),
    };
}
