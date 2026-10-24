using Liss.Gomoku.Core;
namespace 五子棋;

/// <summary>Original legal-move interface, now bounded to the active two-cell frontier.</summary>
sealed class MoveGenerator(int size) : IMoveGenerator
{
    public string name => "邻域走法生成器（V3 安全优化）";
    public StoneMove[] allLegalMove(Stone[,] board)
    {
        var moves = new List<StoneMove>();
        bool hasStone = false;
        for (int x = 0; x < size; x++) for (int y = 0; y < size; y++)
        {
            if (board[x, y] != Stone.空) { hasStone = true; continue; }
            if (SearchHelpers.HasNeighbor(board, x, y, 2)) moves.Add(new StoneMove(new StonePosition((byte)x, (byte)y), 0));
        }
        if (!hasStone) return [new StoneMove(new StonePosition((byte)(size / 2), (byte)(size / 2)), 0)];
        return moves.OrderBy(m => Math.Abs(m.Pos.X - size / 2) + Math.Abs(m.Pos.Y - size / 2)).ToArray();
    }
}
