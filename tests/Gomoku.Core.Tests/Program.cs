using System.Diagnostics;
using System.Text.Json;
using Liss.Gomoku.Core;
using 五子棋;

int passed = 0;
var baselines = new List<object>();
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); passed++; }
GameBoard From(params (int X, int Y)[] moves)
{
    var b = new GameBoard();
    foreach (var (x, y) in moves) Check(b.TryPlay(x, y), $"fixture move {x},{y}");
    return b;
}
void ExpectThrow<T>(Action action) where T : Exception
{
    try { action(); throw new InvalidOperationException($"Expected {typeof(T).Name}"); }
    catch (T) { passed++; }
}

ExpectThrow<ArgumentOutOfRangeException>(() => new GameBoard(4));
ExpectThrow<ArgumentOutOfRangeException>(() => new GameBoard(26));
var board = new GameBoard();
Check(!board.TryPlay(-1, 0) && !board.TryPlay(15, 15), "bounds rejected");
Check(!board.Undo() && !board.Redo(), "empty history safe");
Check(board.TryPlay(7, 7) && !board.TryPlay(7, 7), "repeat rejected");
Check(!board.Undo(0) && !board.Undo(-1), "invalid undo safe");
Check(board.TryPlay(8, 7) && board.TryPlay(9, 7), "alternating turns");
Check(board.Undo(2) && board.Cursor == 1 && !board.BlackToMove, "undo cursor and side");
Check(board.Redo() && board.Cursor == 2 && board.BlackToMove, "redo cursor and side");
Check(board.Undo() && board.TryPlay(1, 1) && board.Moves.Count == 2 && !board.Redo(), "branch truncates future");
GameBoard clone = board.Clone();
Check(clone.TryPlay(2, 1) && board.Cells[2, 1] == Stone.空 && board.Cursor == 2, "clone isolated");
board.Reset();
Check(board.Cursor == 0 && board.Moves.Count == 0 && board.BlackToMove && board.Cells.Cast<Stone>().All(s => s == Stone.空), "reset complete");

// Every direction and near every edge: the legacy board scan missed portions of these lines.
foreach (var (dx, dy, x, y) in new[] { (1, 0, 10, 14), (0, 1, 14, 10), (1, 1, 10, 10), (1, -1, 10, 4), (1, 0, 0, 0), (0, 1, 0, 0), (1, 1, 0, 0), (1, -1, 0, 14) })
{
    var b = new GameBoard();
    for (int n = 0; n < 5; n++)
    {
        Check(b.TryPlay(x + n * dx, y + n * dy), "winning line fixture");
        if (n < 4) Check(b.TryPlay(7, n), "filler");
    }
    Check(b.Winner == Stone.黑子 && b.WinningLine.Count == 5 && b.IsFinished, "edge/direction win");
    Check(!b.TryPlay(6, 6), "terminal rejects move");
    Check(b.Undo() && b.Winner == Stone.空 && !b.IsFinished && b.WinningLine.Count == 0, "undo clears terminal");
    Check(b.Redo() && b.Winner == Stone.黑子, "redo restores terminal");
}
var overline = From((2, 5), (0, 0), (3, 5), (2, 0), (4, 5), (4, 0), (6, 5), (6, 0), (7, 5), (8, 0), (5, 5));
Check(overline.Winner == Stone.黑子 && overline.WinningLine.Count == 6, "freestyle overline wins");
var draw = new GameBoard(5);
var blackCells = new Queue<Move>();
var whiteCells = new Queue<Move>();
for (int x = 0; x < 5; x++) for (int y = 0; y < 5; y++)
    if ((x + 2 * y) % 4 < 2) blackCells.Enqueue(new(x, y)); else whiteCells.Enqueue(new(x, y));
while (draw.Cursor < 25)
{
    Move m = draw.BlackToMove ? blackCells.Dequeue() : whiteCells.Dequeue();
    Check(draw.TryPlay(m.X, m.Y), "draw fixture");
}
Check(draw.IsDraw && draw.IsFinished && draw.Winner == Stone.空, "full board draw");

// A unique immediate win for black, and a unique forced block against white.
var winning = From((4, 7), (3, 7), (5, 7), (1, 1), (6, 7), (3, 1), (7, 7), (5, 1));
var blocking = From((3, 7), (4, 7), (1, 1), (5, 7), (3, 1), (6, 7), (5, 1), (7, 7));
var jumping = From((4, 7), (2, 2), (5, 7), (4, 2), (7, 7), (6, 2), (8, 7), (8, 2));
var position = From((7, 7), (8, 7), (7, 8), (8, 8), (6, 7), (9, 7), (6, 6), (9, 8));
foreach (EngineDescriptor descriptor in EngineCatalog.All)
{
    SearchResult empty = EngineCatalog.Search(new(), new(descriptor.Id, 4, 200));
    Check(empty.Move == new Move(7, 7), $"{descriptor.Id} center opening");
    SearchResult win = EngineCatalog.Search(winning, new(descriptor.Id, 3, 500));
    Check(win.Move == new Move(8, 7), $"{descriptor.Id} takes immediate win");
    SearchResult block = EngineCatalog.Search(blocking, new(descriptor.Id, 3, 500));
    Check(block.Move == new Move(8, 7), $"{descriptor.Id} blocks forced win");
    SearchResult jump = EngineCatalog.Search(jumping, new(descriptor.Id, 3, 500));
    Check(jump.Move == new Move(6, 7), $"{descriptor.Id} jump four win");
    Check(EngineCatalog.Search(draw, new(descriptor.Id)).Move is null, $"{descriptor.Id} no full-board move");
    Check(EngineCatalog.Search(overline, new(descriptor.Id)).Move is null, $"{descriptor.Id} no post-win move");
    foreach (string evaluation in new[] { "patterns", "quintuple" })
    {
        Stone[,] before = (Stone[,])position.Cells.Clone();
        var clock = Stopwatch.StartNew();
        SearchResult result = EngineCatalog.Search(position, new(descriptor.Id, 3, 200, evaluation));
        Check(result.Move is { } m && position.Cells[m.X, m.Y] == Stone.空, $"{descriptor.Id}/{evaluation} legal search result");
        Check(position.Cells.Cast<Stone>().SequenceEqual(before.Cast<Stone>()), $"{descriptor.Id}/{evaluation} caller untouched");
        Check(clock.ElapsedMilliseconds < 1200, $"{descriptor.Id}/{evaluation} bounded wall time");
        baselines.Add(new { engine = descriptor.Id, evaluation, nodes = result.Nodes, depth = result.Depth, tableHits = result.TableHits, elapsedMs = result.ElapsedMs, move = result.Move });
    }
}

// New engine must choose either open-four endpoint (a two-ended threat) rather than a quiet move.
var attack = From((6, 7), (4, 4), (7, 7), (6, 4), (8, 7), (8, 4));
SearchResult tactical = EngineCatalog.Search(attack, new("threat-search", 3, 1500));
Check(tactical.Move is Move { Y: 7, X: 5 or 9 }, "new engine makes an open four");
var cancelBefore = new CancellationTokenSource(); cancelBefore.Cancel();
Check(EngineCatalog.Search(position, new("threat-search"), cancelBefore.Token).Move is null, "cancel before search");
foreach (EngineDescriptor descriptor in EngineCatalog.All.Where(e => e.Id != "legacy-quintuple"))
{
    using var cts = new CancellationTokenSource(); cts.CancelAfter(40);
    var clock = Stopwatch.StartNew();
    SearchResult cancelled = EngineCatalog.Search(position, new(descriptor.Id, 12, 30000), cts.Token);
    Check(clock.ElapsedMilliseconds < 1200, $"{descriptor.Id} prompt cancellation");
}
var concurrent = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => EngineCatalog.Search(position, new("threat-search", 2, 300)))));
Check(concurrent.All(result => result.Move == concurrent[0].Move && result.Depth == 2), "concurrent search isolation and determinism");
Check(position.Cursor == 8 && position.Winner == Stone.空, "parallel caller still unchanged");
ExpectThrow<ArgumentException>(() => EngineCatalog.Search(position, new("unknown")));
ExpectThrow<ArgumentException>(() => EngineCatalog.Search(position, new("threat-search", EvaluationId: "unknown")));
Check(EngineCatalog.Search(position, new("threat-search", -100, -100)).Move is not null, "invalid numeric options clamped safely");
var large = new GameBoard(25); large.TryPlay(12, 12);
Check(EngineCatalog.Search(large, new("legacy-alpha-beta", 1, 200)).Move is not null, "25x25 original evaluator has no fixed-line overflow");

var whiteWinning = From((3, 7), (4, 7), (1, 1), (5, 7), (3, 1), (6, 7), (5, 1), (7, 7), (10, 10));
var nearlyFull = draw.Clone(); nearlyFull.Undo();
var review = position.Clone(); review.Undo(3);
foreach (EngineDescriptor descriptor in EngineCatalog.All)
{
    Check(EngineCatalog.Search(whiteWinning, new(descriptor.Id, 3, 100)).Move == new Move(8, 7), $"{descriptor.Id} white immediate win");
    SearchResult endgame = EngineCatalog.Search(nearlyFull, new(descriptor.Id, 5, 100));
    Check(endgame.Move is { } m && nearlyFull.Cells[m.X, m.Y] == Stone.空, $"{descriptor.Id} one empty cell endgame");
    Check(EngineCatalog.Search(review, new(descriptor.Id, 2, 100)).Move is not null && review.Cursor == 5 && review.Moves.Count == 8, $"{descriptor.Id} review snapshot preserves future");
    var clock = Stopwatch.StartNew();
    SearchResult limited = EngineCatalog.Search(position, new(descriptor.Id, 12, 25));
    Check(clock.ElapsedMilliseconds < 1200 && limited.Move is not null, $"{descriptor.Id} hard time-budget fallback");
}

// Reproducible midgames probe array boundaries, table reuse and the original evaluation pattern parser.
var random = new Random(20261024);
for (int fixture = 0; fixture < 6; fixture++)
{
    var b = new GameBoard(fixture % 2 == 0 ? 15 : 9);
    int target = 16 + fixture * 4;
    for (int attempt = 0; b.Cursor < target && attempt < 1000; attempt++)
    {
        int x = random.Next(b.Size), y = random.Next(b.Size);
        var candidate = b.Clone();
        if (candidate.TryPlay(x, y) && !candidate.IsFinished) b = candidate;
    }
    foreach (EngineDescriptor descriptor in EngineCatalog.All)
    {
        SearchResult sample = EngineCatalog.Search(b, new(descriptor.Id, 2, 100));
        Check(sample.Move is { } m && b.Cells[m.X, m.Y] == Stone.空, $"{descriptor.Id} seeded midgame {fixture}");
    }
}

string? output = args.FirstOrDefault();
string report = JsonSerializer.Serialize(new { passed, failed = 0, framework = "net10.0", board = "freestyle 5–25", timestampUtc = DateTimeOffset.UtcNow, baselines }, new JsonSerializerOptions { WriteIndented = true });
if (output is not null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); File.WriteAllText(output, report); }
Console.WriteLine(report);
