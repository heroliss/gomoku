using Liss.Gomoku.Core;

#nullable disable
namespace 五子棋;

// Shared lifecycle for the original v2.3 search algorithms. Each search owns this instance.
abstract class SearchEngine : ISearchEngine
{
    public abstract string name { get; }
    protected int nodeCount, hitTTcount, searchDepth;
    protected IEvaluation iEvaluation;
    protected IMoveGenerator iMoveGenerator;
    protected Stone[,] board;
    protected bool nowBlack, autoFive;
    internal SearchBudget Budget { get; set; }
    public int NodeCount => nodeCount;
    public int HitTTCount => hitTTcount;
    public void init(IEvaluation evaluation, IMoveGenerator generator, int depth, bool autoFive = true)
    { iEvaluation = evaluation; iMoveGenerator = generator; searchDepth = depth; this.autoFive = autoFive; }
    public StonePosition search(Stone[,] position, bool black)
    { nodeCount = hitTTcount = 0; board = position; nowBlack = black; return getResultFromEngine(); }
    protected abstract StonePosition getResultFromEngine();
    protected void CheckBudget() => Budget.Visit();
    protected bool TryTerminal(bool black, out int score)
    {
        bool anyEmpty = false;
        int size = board.GetLength(0);
        for (int x = 0; x < size; x++) for (int y = 0; y < size; y++)
        {
            if (board[x, y] == Stone.空) { anyEmpty = true; continue; }
            if (!BoardRules.HasFive(board, x, y)) continue;
            score = board[x, y] == (black ? Stone.黑子 : Stone.白子) ? 1_000_000 : -1_000_000;
            return true;
        }
        score = 0;
        return !anyEmpty;
    }
    protected void putStone(StoneMove move)
    { board[move.Pos.X, move.Pos.Y] = nowBlack ? Stone.黑子 : Stone.白子; nowBlack = !nowBlack; }
    protected void delStone(StoneMove move)
    { board[move.Pos.X, move.Pos.Y] = Stone.空; nowBlack = !nowBlack; }
}
