using 五子棋;

namespace Liss.Gomoku.Core;

/// <summary>One side's independent UI search preferences. Construction is cheap; validate before applying or persisting.</summary>
public sealed record SearchProfile(string EngineId = "threat-search", int Depth = 4, int TimeBudgetMs = 1500, string EvaluationId = "patterns")
{
    public void Validate()
    {
        if (!EngineCatalog.All.Any(engine => engine.Id == EngineId)) throw new ArgumentException("Unknown engine ID.", nameof(EngineId));
        if (EvaluationId is not ("patterns" or "quintuple")) throw new ArgumentException("Unknown evaluation ID.", nameof(EvaluationId));
        if (Depth is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(Depth), "Search profile depth must be between 1 and 8.");
        if (TimeBudgetMs is < 250 or > 10000) throw new ArgumentOutOfRangeException(nameof(TimeBudgetMs), "Search profile time budget must be between 250 and 10000 ms.");
    }

    public SearchOptions ToOptions()
    {
        Validate();
        return new SearchOptions(EngineId, Depth, TimeBudgetMs, EvaluationId);
    }
}

/// <param name="Moves">Applied AI decisions for this side; human moves, hints and cancelled requests are excluded by the caller.</param>
/// <param name="Nodes">Total nodes of those decisions; saturates at Int64.MaxValue instead of wrapping.</param>
/// <param name="TableHits">Total table hits of those decisions.</param>
public sealed record SideSearchSummary(int Moves, long TotalElapsedMs, double AverageElapsedMs, int LastElapsedMs,
    int MaxElapsedMs, long Nodes, long TableHits, int LastDepth, string? LastEngineId);

/// <summary>
/// Match-local statistics for AI moves that have already been applied. Each entry owns its full
/// chronological prefix, so the same final coordinate on a different history cannot inherit statistics.
/// Undo/review retains matching redo history; a new branch drops records from the replaced branch.
/// This model does not infer whether a SearchResult was a hint or cancelled: callers must exclude those.
/// </summary>
public sealed class MatchAnalysis
{
    private readonly SortedDictionary<int, Entry> entries = [];
    private sealed record Entry(int BoardSize, Move[] Prefix, SearchResult Result, string EngineId)
    {
        internal int Ply => Prefix.Length;
        internal bool Black => (Ply & 1) == 1;
    }

    /// <summary>
    /// Records the latest applied move at the end of the main line. A new decision at the same
    /// prefix/ply replaces its old measurements, never counting the move twice. The caller must
    /// only call this after its successful TryPlay (not for a hint, cancelled result or plain Redo).
    /// </summary>
    public void Record(GameBoard board, SearchResult result, string engineId)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(result);
        if (!EngineCatalog.All.Any(engine => engine.Id == engineId)) throw new ArgumentException("Unknown engine ID.", nameof(engineId));
        if (board.Cursor <= 0 || board.Cursor != board.Moves.Count) throw new ArgumentException("Record requires an applied move at the current main-line end.", nameof(board));
        Move last = board.Moves[board.Cursor - 1];
        Stone expectedColor = (board.Cursor & 1) == 1 ? Stone.黑子 : Stone.白子;
        if (result.Move is null || result.Move != last || board.Cells[last.X, last.Y] != expectedColor)
            throw new ArgumentException("The result must match the move that was successfully applied.", nameof(result));
        if (result.Nodes < 0 || result.TableHits < 0 || result.ElapsedMs < 0 || result.Depth is < 0 or > 12)
            throw new ArgumentException("Search measurements must be nonnegative, with a valid completed Core depth.", nameof(result));

        // Validate everything before mutating the collection. Undo followed by a fresh AI search
        // may legitimately select the same coordinate; retain its latest decision, once.
        Reconcile(board);
        entries[board.Cursor] = new Entry(board.Size, board.Moves.Take(board.Cursor).ToArray(), result, engineId);
    }

    /// <summary>
    /// Reconciles against the full board timeline, including unapplied redo moves. Thus merely moving
    /// Cursor backwards does not erase measurements that should return when those moves are redone.
    /// </summary>
    public void Reconcile(GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        foreach (int ply in entries.Where(pair => !Matches(pair.Value, board)).Select(pair => pair.Key).ToArray())
            entries.Remove(ply);
    }

    /// <summary>
    /// Removes measurements at and after a one-based ply. Call immediately after a successful human
    /// TryPlay, even if it repeats an old AI coordinate; do not call for ordinary Undo/Redo/review.
    /// A board prefix alone cannot distinguish such a manual replay from the original AI decision.
    /// </summary>
    public void DiscardFrom(int ply)
    {
        if (ply < 1) throw new ArgumentOutOfRangeException(nameof(ply), "A ply must be positive and one-based.");
        foreach (int key in entries.Keys.Where(key => key >= ply).ToArray()) entries.Remove(key);
    }

    /// <summary>Summarises only this side's records in the currently applied, matching history prefix.</summary>
    public SideSearchSummary GetSummary(GameBoard board, bool black)
    {
        ArgumentNullException.ThrowIfNull(board);
        int moves = 0, lastMs = 0, maxMs = 0, lastDepth = 0;
        long elapsed = 0, nodes = 0, hits = 0;
        string? lastEngine = null;
        foreach (Entry entry in entries.Values)
        {
            if (entry.Black != black || entry.Ply > board.Cursor || !Matches(entry, board)) continue;
            moves++;
            elapsed += entry.Result.ElapsedMs;
            nodes = SaturatingAdd(nodes, entry.Result.Nodes);
            hits += entry.Result.TableHits;
            lastMs = entry.Result.ElapsedMs;
            maxMs = Math.Max(maxMs, lastMs);
            lastDepth = entry.Result.Depth;
            lastEngine = entry.EngineId;
        }
        return new SideSearchSummary(moves, elapsed, moves == 0 ? 0 : (double)elapsed / moves,
            lastMs, maxMs, nodes, hits, lastDepth, lastEngine);
    }

    private static bool Matches(Entry entry, GameBoard board)
    {
        if (entry.BoardSize != board.Size) return false;
        IReadOnlyList<Move> history = board.Moves;
        if (entry.Ply > history.Count) return false;
        for (int i = 0; i < entry.Prefix.Length; i++) if (entry.Prefix[i] != history[i]) return false;
        return true;
    }

    private static long SaturatingAdd(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;
}
