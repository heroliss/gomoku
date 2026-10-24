using System.Diagnostics;
using 五子棋;

namespace Liss.Gomoku.Core;

internal sealed class SearchStoppedException : Exception;

internal sealed class SearchBudget(int milliseconds, CancellationToken token)
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    internal long Nodes { get; private set; }
    internal int ElapsedMs => (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds);
    internal void Check()
    {
        if (token.IsCancellationRequested || clock.ElapsedMilliseconds >= milliseconds) throw new SearchStoppedException();
    }
    internal void Visit() { Nodes++; Check(); }
}

internal static class SearchImplementation
{
    internal static SearchResult Run(GameBoard position, SearchOptions options, CancellationToken token)
    {
        if (!EngineCatalog.All.Any(e => e.Id == options.EngineId)) throw new ArgumentException("Unknown engine ID.", nameof(options));
        if (options.EvaluationId is not ("patterns" or "quintuple")) throw new ArgumentException("Unknown evaluation ID.", nameof(options));
        int depthLimit = Math.Clamp(options.Depth, 1, 12);
        int budgetMs = Math.Clamp(options.TimeBudgetMs, 25, 30_000);
        var budget = new SearchBudget(budgetMs, token);
        if (position.IsFinished) return new(null, 0, 0, 0, budget.ElapsedMs, position.IsDraw ? "和棋 · 棋盘已满" : "对局已结束");
        if (token.IsCancellationRequested) return new(null, 0, 0, 0, budget.ElapsedMs, "已取消");
        Stone[,] cells = (Stone[,])position.Cells.Clone();
        Move? best = SearchHelpers.FirstLegal(cells);
        if (best is null) return new(null, 0, 0, 0, budget.ElapsedMs, "棋盘已满");
        int completed = 0, hits = 0;
        string detail = "深度上限";
        try
        {
            Stone own = position.BlackToMove ? Stone.黑子 : Stone.白子;
            // The same safety layer prevents even the educational algorithms from overlooking a one-move win.
            var wins = SearchHelpers.ImmediateWins(cells, own, budget);
            if (wins.Count > 0) return new(wins[0], budget.Nodes, 1, 0, budget.ElapsedMs, "一步成五 · 必胜落点");
            var blocks = SearchHelpers.ImmediateWins(cells, SearchHelpers.Opponent(own), budget);
            if (blocks.Count > 0) return new(blocks[0], budget.Nodes, 1, 0, budget.ElapsedMs, blocks.Count == 1 ? "防守 · 阻止一步成五" : "防守 · 对手存在多个成五点");
            if (position.Cursor == 0 && !cells.Cast<Stone>().Any(s => s != Stone.空))
                return new(best, budget.Nodes, 0, 0, budget.ElapsedMs, "空盘 · 天元开局");

            if (options.EngineId == "threat-search")
            {
                var engine = new ThreatSearcher(cells, position.BlackToMove, budget, options.EvaluationId);
                for (int depth = 1; depth <= depthLimit; depth++)
                {
                    (Move move, int score) = engine.Search(depth, best);
                    best = move;
                    completed = depth;
                    hits = engine.Hits;
                    if (Math.Abs(score) >= ThreatSearcher.Mate - 50) { detail = "威胁搜索 · 发现成五序列"; break; }
                }
                hits = engine.Hits;
            }
            else
            {
                for (int depth = 1; depth <= depthLimit; depth++)
                {
                    SearchEngine engine = CreateLegacy(options.EngineId);
                    engine.Budget = budget;
                    IEvaluation evaluation = options.EvaluationId == "quintuple" ? new EvaluationQuintuple() : new Evaluation1(position.Size);
                    engine.init(evaluation, new MoveGenerator(position.Size), depth, false);
                    // Original engines mutate their search board; an interrupted pass is intentionally discarded.
                    StonePosition move = engine.search((Stone[,])cells.Clone(), position.BlackToMove);
                    if ((uint)move.X < position.Size && (uint)move.Y < position.Size && cells[move.X, move.Y] == Stone.空)
                        best = new Move(move.X, move.Y);
                    completed = depth;
                    hits += engine.HitTTCount;
                    if (options.EngineId == "legacy-quintuple") break;
                }
            }
        }
        catch (SearchStoppedException) { detail = token.IsCancellationRequested ? "已取消 · 保留已完成结果" : "时间预算 · 保留已完成结果"; }
        return new(best, budget.Nodes, completed, hits, budget.ElapsedMs, detail);
    }

    private static SearchEngine CreateLegacy(string id) => id switch
    {
        "legacy-negamax" => new NegaMax(),
        "legacy-alpha-beta" => new AlphaBeta(),
        "legacy-fail-soft" => new FAlphaBeta(),
        "legacy-aspiration" => new AspirationSearch(),
        "legacy-history" => new FAlphaBeta_HH(),
        "legacy-table" => new FAlphaBeta_TT(),
        "legacy-pvs" => new PVS(),
        "legacy-pvs-history" => new NegaScout_HH(),
        "legacy-pvs-table" => new NegaScout_HH_TT(),
        "legacy-quintuple" => new QuintupleEngine(),
        _ => throw new ArgumentException("Unknown legacy engine.", nameof(id))
    };
}

internal static class SearchHelpers
{
    internal static Stone Opponent(Stone stone) => stone == Stone.黑子 ? Stone.白子 : Stone.黑子;
    internal static bool HasNeighbor(Stone[,] board, int x, int y, int radius)
    {
        int size = board.GetLength(0);
        for (int xx = Math.Max(0, x - radius); xx <= Math.Min(size - 1, x + radius); xx++)
            for (int yy = Math.Max(0, y - radius); yy <= Math.Min(size - 1, y + radius); yy++)
                if (board[xx, yy] != Stone.空) return true;
        return false;
    }
    internal static Move? FirstLegal(Stone[,] board)
    {
        int size = board.GetLength(0), center = size / 2;
        Move? best = null;
        int distance = int.MaxValue;
        for (int x = 0; x < size; x++) for (int y = 0; y < size; y++)
            if (board[x, y] == Stone.空 && Math.Abs(x - center) + Math.Abs(y - center) < distance)
            { distance = Math.Abs(x - center) + Math.Abs(y - center); best = new Move(x, y); }
        return best;
    }
    internal static List<Move> ImmediateWins(Stone[,] board, Stone stone, SearchBudget budget)
    {
        var wins = new List<Move>();
        int size = board.GetLength(0);
        for (int x = 0; x < size; x++) for (int y = 0; y < size; y++)
        {
            budget.Check();
            if (board[x, y] != Stone.空 || !HasNeighbor(board, x, y, 1)) continue;
            board[x, y] = stone;
            try { if (BoardRules.HasFive(board, x, y)) wins.Add(new Move(x, y)); }
            finally { board[x, y] = Stone.空; }
        }
        return wins;
    }
}
