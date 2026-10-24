using Liss.Gomoku.Core;
namespace 五子棋;

/// <summary>Original five-cell window evaluator; corrected empty-window bias, bounds and overflow.</summary>
sealed class EvaluationQuintuple : IEvaluation
{
    private static readonly int[] BlackScores = [0, 35, 800, 7500, 80000, 1_000_000];
    private static readonly int[] WhiteScores = [0, 15, 400, 2500, 60000, 1_000_000];
    public string name => "五元组估值（V3 修正）";
    public int evaluate(Stone[,] board, bool black)
    {
        int size = board.GetLength(0);
        long sum = 0;
        int[] values = black ? BlackScores : WhiteScores;
        Stone own = black ? Stone.黑子 : Stone.白子;
        foreach (var (dx, dy) in BoardRules.Directions)
            for (int x = 0; x < size; x++) for (int y = 0; y < size; y++)
            {
                if ((uint)(x + 4 * dx) >= size || (uint)(y + 4 * dy) >= size) continue;
                int a = 0, b = 0;
                for (int n = 0; n < 5; n++) { Stone s = board[x + dx * n, y + dy * n]; if (s == own) a++; else if (s != Stone.空) b++; }
                if (a == 0 || b == 0) sum += a > 0 ? values[a] : -values[b];
            }
        return (int)Math.Clamp(sum, -900_000L, 900_000L);
    }
}
