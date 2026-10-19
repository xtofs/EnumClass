namespace Chess;

internal class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("\n## EnumClass member");
        var piece = Piece.WhiteKing;
        Occupancy content = piece;
        Console.WriteLine($"{piece} ({piece.Color}), ordinal in Piece = {piece.Ordinal}, ordinal in Occupancy = {content.Ordinal}");

        Console.WriteLine("\n## EnumClass sets");
        Piece.Set set = [Piece.BlackQueen, Piece.WhiteQueen];
        Console.WriteLine($"Set: {set}");
        Console.WriteLine($"Contains BlackQueen: {set.Contains(Piece.BlackQueen)}");

        Console.WriteLine("\n## array of EnumClass");
        // Supersets via [Includes<T>]
        var board = new Occupancy[64];                  // all Empty: Empty has ordinal 0
        board[4] = Piece.WhiteKing;                     // implicit widening
        if (board[4].TryGetPiece(out piece))            // narrowing
        {
            Console.WriteLine($"e1: is {piece} ({piece.Color})");
        }
        else
        {
            Console.WriteLine("e1: is empty");
        }
        Console.WriteLine($"a1 is empty: {board[0] == Occupancy.Empty}");


        Console.WriteLine("\n## Set widening");
        Piece.Set important = [Piece.WhiteKing, Piece.WhiteQueen];
        Occupancy.Set occupied = important; // set widening (is implemented as shift)

        Console.WriteLine($"pieces {important} -> occupied {occupied}");

        Console.WriteLine("\n## Set narrowing");
        var stuff = occupied | Occupancy.Empty;
        Console.WriteLine($"occupied set {stuff} -> pieces set {stuff.ToPieceSet()}");
    }
}
