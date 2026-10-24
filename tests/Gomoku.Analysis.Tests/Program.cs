using System.Text.Json;
using Liss.Gomoku.Core;

int passed = 0;
void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); passed++; }
void Invalid(Action action, string message)
{
    try { action(); throw new InvalidOperationException("Accepted invalid input: " + message); }
    catch (ArgumentException) { passed++; }
}
SearchResult Result(Move move, int elapsed = 100, long nodes = 10, int hits = 1, int depth = 2) => new(move, nodes, depth, hits, elapsed, "fixture");
void Apply(GameBoard board, MatchAnalysis analysis, Move move, int elapsed = 100, long nodes = 10, int hits = 1, int depth = 2, string engine = "threat-search")
{
    Check(board.TryPlay(move.X, move.Y), "fixture move is legal");
    analysis.Record(board, Result(move, elapsed, nodes, hits, depth), engine);
}

var defaultProfile = new SearchProfile();
defaultProfile.Validate();
Check(defaultProfile.ToOptions() == new SearchOptions("threat-search", 4, 1500, "patterns"), "default profile options");
var blackProfile = new SearchProfile("legacy-negamax", 1, 250, "quintuple");
var whiteProfile = new SearchProfile("legacy-pvs-table", 8, 10000, "patterns");
Check(blackProfile.ToOptions() == new SearchOptions("legacy-negamax", 1, 250, "quintuple"), "black profile exact settings");
Check(whiteProfile.ToOptions() == new SearchOptions("legacy-pvs-table", 8, 10000, "patterns"), "white profile exact settings");
var editedBlack = blackProfile with { Depth = 7, EvaluationId = "patterns" };
Check(whiteProfile.Depth == 8 && whiteProfile.TimeBudgetMs == 10000 && blackProfile.Depth == 1 && editedBlack.Depth == 7, "side profiles do not share mutable state");
foreach (EngineDescriptor engine in EngineCatalog.All)
    foreach (string evaluation in new[] { "patterns", "quintuple" })
    {
        new SearchProfile(engine.Id, 1, 250, evaluation).Validate();
        new SearchProfile(engine.Id, 8, 10000, evaluation).Validate();
        passed += 2;
    }
foreach (SearchProfile bad in new[]
{
    new SearchProfile(""), new SearchProfile("unknown"), new SearchProfile(null!),
    new SearchProfile(EvaluationId: ""), new SearchProfile(EvaluationId: "PATTERNS"), new SearchProfile(EvaluationId: null!),
    new SearchProfile(Depth: 0), new SearchProfile(Depth: 9), new SearchProfile(Depth: int.MinValue), new SearchProfile(Depth: int.MaxValue),
    new SearchProfile(TimeBudgetMs: 249), new SearchProfile(TimeBudgetMs: 10001), new SearchProfile(TimeBudgetMs: int.MinValue), new SearchProfile(TimeBudgetMs: int.MaxValue)
})
{
    Invalid(bad.Validate, "profile validation");
    Invalid(() => bad.ToOptions(), "conversion never silently clamps a profile");
}

var board = new GameBoard();
var analysis = new MatchAnalysis();
var empty = new SideSearchSummary(0, 0, 0, 0, 0, 0, 0, 0, null);
Check(analysis.GetSummary(board, true) == empty && analysis.GetSummary(board, false) == empty, "empty side summaries have defined zero/null values");
Apply(board, analysis, new(7, 7), 100, 10, 1, 2);
Apply(board, analysis, new(8, 7), 250, 30, 4, 3, "legacy-alpha-beta");
Check(board.TryPlay(7, 8), "a human move has no search record");
Apply(board, analysis, new(8, 8), 400, 50, 5, 4, "legacy-pvs");
Apply(board, analysis, new(6, 7), 300, 20, 2, 5, "legacy-fail-soft");
var black = new SideSearchSummary(2, 400, 200, 300, 300, 30, 3, 5, "legacy-fail-soft");
var white = new SideSearchSummary(2, 650, 325, 400, 400, 80, 9, 4, "legacy-pvs");
Check(analysis.GetSummary(board, true) == black, "black decisions aggregate exactly, excluding the human move");
Check(analysis.GetSummary(board, false) == white, "white decisions stay independent");
Check(board.Undo(2), "undo two plies");
analysis.Reconcile(board);
Check(analysis.GetSummary(board, true) == new SideSearchSummary(1, 100, 100, 100, 100, 10, 1, 2, "threat-search"), "undo excludes future black decision");
Check(analysis.GetSummary(board, false) == new SideSearchSummary(1, 250, 250, 250, 250, 30, 4, 3, "legacy-alpha-beta"), "undo excludes future white decision");
Check(board.Redo() && board.Redo(), "redo both moves");
Check(analysis.GetSummary(board, true) == black && analysis.GetSummary(board, false) == white, "redo restores retained measurements without recording again");
Check(board.Undo(100), "review to beginning");
analysis.Reconcile(board);
Check(analysis.GetSummary(board, true) == empty && analysis.GetSummary(board, false) == empty, "review cursor zero has no applied measurements");
while (board.Redo()) { }
Check(analysis.GetSummary(board, true) == black && analysis.GetSummary(board, false) == white, "review keeps the complete redo ledger");

// A cancelled search and a hint may have metrics, but never commit a move/measurement.
var snapshot = board.Clone();
SearchResult hint = EngineCatalog.Search(snapshot, defaultProfile.ToOptions());
Check(hint.Move is not null && snapshot.Cursor == board.Cursor, "real hint search returns a suggestion without committing");
Check(analysis.GetSummary(board, true) == black && analysis.GetSummary(board, false) == white, "hint without Record leaves totals unchanged");
using (var cts = new CancellationTokenSource())
{
    cts.Cancel();
    SearchResult cancelled = EngineCatalog.Search(snapshot, defaultProfile.ToOptions(), cts.Token);
    Check(cancelled.Move is null, "cancel-before-search has no applied decision");
    Invalid(() => analysis.Record(board, cancelled, "threat-search"), "cancelled/null move cannot be attached to an existing last move");
}
Check(analysis.GetSummary(board, true) == black && analysis.GetSummary(board, false) == white, "cancelled request leaves totals unchanged");

// Undo and a fresh AI decision may land on precisely the same coordinate. Replace, never double count.
Check(board.Undo(), "undo last AI move");
analysis.Reconcile(board);
Check(board.TryPlay(6, 7), "fresh AI reselects the previous point");
analysis.Record(board, Result(new(6, 7), 75, 7, 0, 1), "legacy-quintuple");
var latestBlack = new SideSearchSummary(2, 175, 87.5, 75, 100, 17, 1, 1, "legacy-quintuple");
Check(analysis.GetSummary(board, true) == latestBlack, "same-prefix decision replaces old duration/engine/counters exactly once");
analysis.Record(board, Result(new(6, 7), 75, 7, 0, 1), "legacy-quintuple");
Check(analysis.GetSummary(board, true) == latestBlack, "duplicate callback cannot accumulate a second decision");

SideSearchSummary unchanged = analysis.GetSummary(board, true);
foreach (SearchResult bad in new[]
{
    new SearchResult(null, 1, 1, 0, 1, "no move"), Result(new(0, 0)),
    Result(new(6, 7), elapsed: -1), Result(new(6, 7), nodes: -1), Result(new(6, 7), hits: -1),
    Result(new(6, 7), depth: -1), Result(new(6, 7), depth: 13)
})
{
    Invalid(() => analysis.Record(board, bad, "threat-search"), "invalid search measurement/result");
    Check(analysis.GetSummary(board, true) == unchanged, "invalid record does not mutate the ledger");
}
Invalid(() => analysis.Record(board, Result(new(6, 7)), "unknown"), "unknown engine");
Invalid(() => analysis.Record(board, Result(new(6, 7)), null!), "null engine");
Invalid(() => analysis.Record(null!, Result(new(6, 7)), "threat-search"), "null board");
Invalid(() => analysis.Record(board, null!, "threat-search"), "null result");
Invalid(() => analysis.Record(new(), Result(new(6, 7)), "threat-search"), "empty board");
Invalid(() => analysis.Reconcile(null!), "null reconciliation board");
Invalid(() => analysis.GetSummary(null!, true), "null summary board");
Check(board.Undo(), "review before the final move");
Invalid(() => analysis.Record(board, Result(new(8, 8)), "threat-search"), "recording a review position is not a newly applied decision");
Check(board.Redo(), "return to main-line end");
Check(analysis.GetSummary(board, true) == unchanged, "invalid input preserves the original search history");

// Same physical board and same endpoint, but a different chronological history, must not inherit entries.
var original = new GameBoard(); var different = new GameBoard(); var histories = new MatchAnalysis();
foreach (Move move in new[] { new Move(7, 7), new(8, 7), new(7, 8), new(8, 8) }) Apply(original, histories, move);
foreach (Move move in new[] { new Move(7, 8), new(8, 7), new(7, 7), new(8, 8) }) Check(different.TryPlay(move.X, move.Y), "alternate chronological fixture");
Check(original.Cells.Cast<五子棋.Stone>().SequenceEqual(different.Cells.Cast<五子棋.Stone>()), "fixtures have the identical final arrangement");
Check(histories.GetSummary(different, true) == empty && histories.GetSummary(different, false) == empty, "full chronological prefix prevents endpoint/position aliasing");
histories.Reconcile(different);
histories.Record(different, Result(new(8, 8), 31), "threat-search");
Check(histories.GetSummary(different, true) == empty && histories.GetSummary(different, false).TotalElapsedMs == 31, "reconciled replacement has only its actual recorded decision");

// Shared earlier decisions survive a branch; later records never reappear at the same coordinate.
var branch = original.Clone(); var branchStats = new MatchAnalysis();
var build = new GameBoard();
foreach (Move move in original.Moves) Apply(build, branchStats, move, 100);
Check(branch.Undo(2) && branch.TryPlay(9, 9), "branch after a shared prefix");
branchStats.Reconcile(branch); // New human move, so no AI Record call.
Check(branchStats.GetSummary(branch, true).Moves == 1 && branchStats.GetSummary(branch, false).Moves == 1, "human branch clears replaced future records but keeps common prefix");
Check(branch.TryPlay(8, 8), "new branch reaches the same old final coordinate");
branchStats.Record(branch, Result(new(8, 8), 19), "legacy-alpha-beta");
Check(branchStats.GetSummary(branch, false).Moves == 2 && branchStats.GetSummary(branch, false).TotalElapsedMs == 119, "new branch decision uses new metrics");
var larger = new GameBoard(19);
foreach (Move move in branch.Moves) Check(larger.TryPlay(move.X, move.Y), "different board-size fixture");
Check(branchStats.GetSummary(larger, true) == empty && branchStats.GetSummary(larger, false) == empty, "board size belongs to the identity too");

// Human replay can be chronologically identical to an earlier AI move. The UI explicitly identifies
// it after successful TryPlay; discarding this ply and its future avoids reusing old AI measurements.
var manual = original.Clone(); var manualStats = new MatchAnalysis();
var measured = new GameBoard();
foreach (Move move in original.Moves) Apply(measured, manualStats, move, 100);
Check(manual.Undo(2), "rewind before human replay");
manualStats.Reconcile(manual);
Check(manual.TryPlay(7, 8), "human replays precisely the old AI coordinate");
manualStats.DiscardFrom(manual.Cursor);
Check(manualStats.GetSummary(manual, true).Moves == 1 && manualStats.GetSummary(manual, true).TotalElapsedMs == 100, "manual replay no longer inherits the replaced AI decision");
Check(manualStats.GetSummary(original, true).Moves == 1 && manualStats.GetSummary(original, false).Moves == 1, "discard clears both colours in the future, preserving the shared earlier prefix");
Check(manual.TryPlay(8, 8), "human replays the next original coordinate");
manualStats.DiscardFrom(manual.Cursor);
Check(manualStats.GetSummary(manual, false).Moves == 1, "a second manual replay cannot resurrect discarded future metrics");
var manualBeforeInvalid = manualStats.GetSummary(manual, true);
Invalid(() => manualStats.DiscardFrom(0), "discard needs positive one-based ply");
Invalid(() => manualStats.DiscardFrom(-1), "negative discard ply");
Invalid(() => manualStats.DiscardFrom(int.MinValue), "overflow-like discard ply");
Check(manualStats.GetSummary(manual, true) == manualBeforeInvalid, "invalid discard leaves prior data intact");
manualStats.DiscardFrom(int.MaxValue);
Check(manualStats.GetSummary(manual, true) == manualBeforeInvalid, "discard after every possible move is a safe no-op");
manualStats.DiscardFrom(1);
Check(manualStats.GetSummary(manual, true) == empty && manualStats.GetSummary(manual, false) == empty, "discard from first ply clears the entire ledger");

// Extreme counter inputs cannot wrap totals negative; elapsed/hits are accumulated as Int64.
var huge = new GameBoard(); var hugeStats = new MatchAnalysis();
Apply(huge, hugeStats, new(7, 7), int.MaxValue, long.MaxValue, int.MaxValue, 0);
Check(huge.TryPlay(8, 8), "unmeasured human reply");
Apply(huge, hugeStats, new(7, 8), int.MaxValue, 100, int.MaxValue, 12);
SideSearchSummary totals = hugeStats.GetSummary(huge, true);
Check(totals.Nodes == long.MaxValue && totals.TotalElapsedMs == 2L * int.MaxValue && totals.TableHits == 2L * int.MaxValue && totals.AverageElapsedMs == int.MaxValue, "counter sums remain bounded and nonnegative");
huge.Reset(); hugeStats.Reconcile(huge);
Check(hugeStats.GetSummary(huge, true) == empty && hugeStats.GetSummary(huge, false) == empty, "reset reconciliation clears all records");
Check(new MatchAnalysis().GetSummary(board, true) == empty, "loading with a new analysis does not invent old metrics");

// Max board size bounds the ledger to 625 plies; a full chronological prefix remains exact.
var full = new GameBoard(25); var fullStats = new MatchAnalysis();
var blacks = new Queue<Move>(); var whites = new Queue<Move>();
for (int x = 0; x < 25; x++) for (int y = 0; y < 25; y++)
    if ((x + 2 * y) % 4 < 2) blacks.Enqueue(new(x, y)); else whites.Enqueue(new(x, y));
while (!full.IsFinished)
{
    Move next = full.BlackToMove ? blacks.Dequeue() : whites.Dequeue();
    Apply(full, fullStats, next, 1, 1, 0, 1);
}
Check(full.IsDraw && full.Cursor == 625 && fullStats.GetSummary(full, true).Moves == 313 && fullStats.GetSummary(full, false).Moves == 312, "maximum board has one bounded measurement per move");
full.Undo(625); fullStats.Reconcile(full);
Check(fullStats.GetSummary(full, true) == empty && fullStats.GetSummary(full, false) == empty, "maximum-board review excludes every future measurement");
while (full.Redo()) { }
Check(fullStats.GetSummary(full, true).Moves + fullStats.GetSummary(full, false).Moves == 625, "maximum-board redo retains exact decisions");

string report = JsonSerializer.Serialize(new { passed, failed = 0, timestampUtc = DateTimeOffset.UtcNow, profileDepth = "1–8", profileTimeBudgetMs = "250–10000", historyIdentity = "board size and complete chronological move prefix", samePrefixDecision = "replace metrics; count once", maximumPlies = 625 }, new JsonSerializerOptions { WriteIndented = true });
if (args.FirstOrDefault() is string output) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); File.WriteAllText(output, report); }
Console.WriteLine(report);
