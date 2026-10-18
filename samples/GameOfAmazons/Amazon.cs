namespace GameOfAmazons;

using Xtofs.EnumClass;

public enum Player { White, Black }

/// <summary>The two kinds of amazon (queen-moving piece), one per player.</summary>
[EnumClass]
public readonly partial struct Amazon
{
    public partial Player Owner { get; }

    private static Table Members => new()
    {
        WhiteAmazon = new(Player.White),
        BlackAmazon = new(Player.Black),
    };
}
