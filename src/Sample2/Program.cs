namespace Sample2;

using EnumClass;


internal class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine(Piece.BlackQueen);

        Piece.Set set = [Piece.BlackQueen, Piece.WhiteQueen];
        set.Contains(Piece.BlackQueen);

        var dictionary = new Dictionary<Piece, int>
        {
            [Piece.BlackQueen] = 1,
            [Piece.WhiteRook] = 2
        };

        Console.WriteLine(string.Join(", ", dictionary));

        var x = Piece.BlackQueen.Color;

        // Supersets via [Includes<T>]
        var board = new Occupancy[64];                  // all Empty: Empty has ordinal 0
        board[4] = Piece.WhiteKing;                     // implicit widening
        if (board[4].TryGetPiece(out var piece))        // narrowing
            Console.WriteLine($"e1: {piece} ({piece.Color}), ordinal in Occupancy = {board[4].Ordinal}");
        Console.WriteLine($"a1 is empty: {board[0] == Occupancy.Empty}");

        Piece.Set whites = [Piece.WhiteKing, Piece.WhiteQueen];
        Occupancy.Set occupied = whites;                // set widening is a shift
        Console.WriteLine($"{occupied | Occupancy.Empty} -> pieces {(occupied | Occupancy.Empty).ToPieceSet()}");

        AmazonsSquare sq = Amazon.BlackAmazon;
        Console.WriteLine(sq.ToTag() switch
        {
            AmazonsSquare.Tag.Empty => "empty",
            AmazonsSquare.Tag.Fire => "fire",
            _ => $"amazon {((Amazon)sq).Color}",
        });
    }
}

[EnumClass]
public readonly partial struct Piece
{
    public partial string Name { get; }
    public partial PieceColor Color { get; }
    public partial PieceType Type { get; }
    public partial char AlgebraicNotationSymbol { get; }

    private static Table Data => new()
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

public enum PieceType
{
    King,
    Queen,
    Knight,
    Bishop,
    Rook,
    Pawn,
}