using Liss.Gomoku.Core;
namespace 五子棋;

/// <summary>Retains the v2.3 single-layer scoring algorithm, separately selectable from tree search.</summary>
sealed class QuintupleEngine : SearchEngine
{
    public override string name => "五元组单层引擎（V3 修正）";
    protected override StonePosition getResultFromEngine()
    {
        int size = board.GetLength(0);
        long[,] scores = new long[size, size];
        int[] weights = nowBlack ? [7, 35, 800, 7500, 800000, 0] : [7, 15, 400, 2500, 100000, 0];
        foreach (var (dx, dy) in BoardRules.Directions)
            for (int x = 0; x < size; x++) for (int y = 0; y < size; y++)
            {
                CheckBudget();
                if ((uint)(x + 4 * dx) >= size || (uint)(y + 4 * dy) >= size) continue;
                int black = 0, white = 0;
                for (int n = 0; n < 5; n++) { Stone s = board[x + dx * n, y + dy * n]; if (s == Stone.黑子) black++; else if (s == Stone.白子) white++; }
                if (black > 0 && white > 0) continue;
                int value = weights[black + white];
                for (int n = 0; n < 5; n++) if (board[x + dx * n, y + dy * n] == Stone.空) scores[x + dx * n, y + dy * n] += value;
            }
        var best = new StonePosition();
        long max = long.MinValue;
        for (int x = 0; x < size; x++) for (int y = 0; y < size; y++)
            if (board[x, y] == Stone.空 && scores[x, y] > max) { max = scores[x, y]; best = new StonePosition((byte)x, (byte)y); }
        return best;
    }
}
