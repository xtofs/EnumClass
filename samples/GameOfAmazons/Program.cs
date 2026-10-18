using GameOfAmazons;

// 10×10 board, all Empty because Empty has ordinal 0.
var board = new Square[100];
board[3] = board[6] = Amazon.WhiteAmazon;              // implicit widening Amazon → Square
board[93] = board[96] = Amazon.BlackAmazon;
board[44] = Square.Fire;                               // an arrow burnt e5

// Narrowing and the per-include helpers.
foreach (int i in new[] { 3, 44, 50, 93 })
{
    var square = board[i];
    string what = square.TryGetAmazon(out var amazon)
        ? $"{amazon} owned by {amazon.Owner}"
        : square.ToTag() switch
        {
            Square.Tag.Empty => "empty",
            Square.Tag.Fire => "fire",
            _ => throw new InvalidOperationException(),
        };
    Console.WriteLine($"{(char)('a' + i % 10)}{i / 10 + 1}: {what} (ordinal {square.Ordinal})");
}

// A square is blocked if it holds fire or any amazon: one set test.
Square.Set blocked = [Square.Fire, Amazon.WhiteAmazon, Amazon.BlackAmazon];
Console.WriteLine($"blocked = {blocked}; e5 blocked: {blocked.Contains(board[44])}, a6 blocked: {blocked.Contains(board[50])}");

// Set widening and narrowing between Amazon.Set and Square.Set.
Amazon.Set amazons = Amazon.Set.All;
Square.Set occupied = amazons;
Console.WriteLine($"{occupied | Square.Fire} -> amazons {(occupied | Square.Fire).ToAmazonSet()}");
