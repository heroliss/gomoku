using System.Text;
using System.Text.Json;
using Liss.Gomoku.App;
using Liss.Gomoku.Core;
using 五子棋;

int passed = 0;
var temporary = Path.Combine(Path.GetTempPath(), "liss-gomoku-storage-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); passed++; }
string Fixture(string content, string extension = ".json")
{
    string file = Path.Combine(temporary, Guid.NewGuid().ToString("N") + extension);
    File.WriteAllText(file, content, new UTF8Encoding(false));
    return file;
}
void Invalid(string text, string name, string extension = ".json")
{
    try { SessionFiles.Load(Fixture(text, extension)); throw new InvalidOperationException($"Accepted invalid fixture: {name}"); }
    catch (InvalidDataException) { passed++; }
}
string Json(object value) => JsonSerializer.Serialize(value);
try
{
    var board = new GameBoard();
    board.TryPlay(7, 7); board.TryPlay(8, 8); board.TryPlay(9, 9); board.Undo(2);
    string save = Path.Combine(temporary, "new", "game.json");
    SessionFiles.Save(save, board, 2);
    var loaded = SessionFiles.Load(save);
    Check(loaded.Mode == 2 && loaded.Board.Cursor == 1 && loaded.Board.Moves.Count == 3 && loaded.Board.Cells[8, 8] == Stone.空, "JSON round trip preserves review cursor");
    Check(loaded.Board.Redo() && loaded.Board.Cells[8, 8] == Stone.白子, "JSON future restored");
    board.Undo(); SessionFiles.Save(save, board, 0);
    var atBeginning = SessionFiles.Load(save).Board;
    Check(atBeginning.Cursor == 0 && atBeginning.Moves.Count == 3 && atBeginning.Cells.Cast<Stone>().All(s => s == Stone.空), "cursor zero survives JSON");
    var future = atBeginning.Clone(); while (future.Redo()) { }
    SessionFiles.Save(save, future, 1);
    var atEnd = SessionFiles.Load(save);
    Check(atEnd.Mode == 1 && atEnd.Board.Cursor == 3 && atEnd.Board.Cells[9, 9] == Stone.黑子, "Undo(0) preserves complete state");
    SessionFiles.Save(save, new GameBoard(5), 3);
    Check(SessionFiles.Load(save).Board.Size == 5 && SessionFiles.Load(save).Board.Cursor == 0, "empty JSON round trip");

    var win = new GameBoard();
    for (int n = 0; n < 5; n++) { win.TryPlay(n, 0); if (n < 4) win.TryPlay(n, 2); }
    SessionFiles.Save(save, win, 0);
    Check(SessionFiles.Load(save).Board.Winner == Stone.黑子, "won game load does not undo zero steps");
    win.Undo(); SessionFiles.Save(save, win, 0);
    var reviewedWin = SessionFiles.Load(save).Board;
    Check(reviewedWin.Winner == Stone.空 && reviewedWin.Redo() && reviewedWin.Winner == Stone.黑子, "future winning move retains terminal semantics");

    string valid = "{\"Version\":1,\"BoardSize\":15,\"Moves\":[],\"Cursor\":0}";
    Check(SessionFiles.Load(Fixture(valid)).Mode == 0, "schema v1 optional Mode compatibility");
    foreach (int mode in new[] { -1, 4, int.MinValue, int.MaxValue }) Invalid(Json(new SavedGame(1, 15, [], 0, mode)), $"mode {mode}");
    foreach (int version in new[] { -1, 0, 2, int.MaxValue }) Invalid(Json(new SavedGame(version, 15, [], 0)), $"version {version}");
    foreach (int size in new[] { 0, 4, 26, int.MaxValue }) Invalid(Json(new SavedGame(1, size, [], 0)), $"size {size}");
    foreach (int cursor in new[] { -1, 1, int.MaxValue }) Invalid(Json(new SavedGame(1, 15, [], cursor)), $"cursor {cursor}");
    Invalid(Json(new SavedGame(1, 15, [new(-1, 0)], 1)), "negative coordinate");
    Invalid(Json(new SavedGame(1, 15, [new(15, 0)], 1)), "outside coordinate");
    Invalid(Json(new SavedGame(1, 15, [new(int.MaxValue, int.MinValue)], 1)), "extreme coordinate");
    Invalid(Json(new SavedGame(1, 15, [new(1, 1), new(1, 1)], 2)), "duplicate move");
    Invalid(Json(new SavedGame(1, 15, win.Moves.Concat([new Move(14, 14)]).ToArray(), 9)), "post-win future move rejected");
    Invalid(Json(new SavedGame(1, 5, Enumerable.Repeat(new Move(0, 0), 26).ToArray(), 0)), "too many moves");
    foreach (string text in new[]
    {
        "", "null", "[]", "{}", "{", valid + valid,
        valid.Replace("\"Version\":1", "\"Version\":1,\"Version\":1"),
        valid.Replace("\"Version\":1", "\"Version\":1.5"),
        valid.Replace("\"Version\":1", "\"Version\":\"1\""),
        valid.Replace("\"Version\":1,", ""),
        valid.Replace("\"Cursor\":0", "\"Cursor\":0,\"Extra\":true"),
        valid.Replace("[]", "null"),
        valid.Replace("[]", "[{}]"),
        valid.Replace("[]", "[{\"X\":0}]"),
        valid.Replace("[]", "[{\"X\":0,\"Y\":0,\"Y\":1}]"),
        valid.Replace("[]", "[{\"X\":0.2,\"Y\":0}]"),
        valid.Replace("[]", "[{\"X\":2147483648,\"Y\":0}]"),
        valid.Replace("[]", "[{\"X\":0,\"Y\":0,\"Z\":0}]"),
        valid.Replace("[]", "[null]"),
        "{\"x\":" + new string('[', 20) + "0" + new string(']', 20) + "}"
    }) Invalid(text, "JSON malformed/schema field fixture");

    string boundary = Fixture(valid + new string(' ', SessionFiles.MaximumFileBytes - Encoding.UTF8.GetByteCount(valid)));
    Check(SessionFiles.Load(boundary).Board.Cursor == 0, "exact 1 MiB boundary accepted");
    File.AppendAllText(boundary, " ");
    try { SessionFiles.Load(boundary); throw new InvalidOperationException("Oversized file accepted"); } catch (InvalidDataException) { passed++; }
    string bom = Path.Combine(temporary, "bom.json"); File.WriteAllText(bom, valid, new UTF8Encoding(true));
    Check(SessionFiles.Load(bom).Board.Cursor == 0, "UTF-8 BOM JSON");

    string sgf = Path.Combine(temporary, "sgf-folder", "game.sgf");
    SessionFiles.SaveSgf(sgf, new GameBoard());
    Check(SessionFiles.Load(sgf).Board.Cursor == 0, "empty SGF round trip and directory creation");
    SessionFiles.SaveSgf(sgf, future);
    Check(SessionFiles.Load(sgf).Board.Moves.SequenceEqual(future.Moves), "SGF full main line round trip");
    SessionFiles.SaveSgf(sgf, board);
    Check(SessionFiles.Load(sgf).Board.Cursor == 3, "SGF intentionally exports complete line, not JSON review cursor");
    foreach (string text in new[] { "(;)", "(;;)", "(;SZ[15])", "(;;B[hh];W[ii];)", " (; \n ; B[hh]\n C[old (v2) comment];W[ii];) " })
        Check(SessionFiles.Load(Fixture(text, ".sgf")).Board is not null, "legacy SGF valid main line");
    var commented = SessionFiles.Load(Fixture("(;SZ[15]C[brackets \\] and (parentheses) and \\\\];B[hh]C[semi; colon];W[ii])", ".sgf")).Board;
    Check(commented.Cursor == 2 && commented.Cells[7, 7] == Stone.黑子, "SGF escaped strings and comment tree symbols");
    Check(SessionFiles.Load(Fixture("(;SZ[25];B[yy];W[aa])", ".sgf")).Board.Size == 25, "25x25 SGF");
    Check(SessionFiles.Load(Fixture("(;B[hh]C[continued\\\r\nline];W[ii])", ".sgf")).Board.Cursor == 2, "SGF escaped newline");
    foreach (string text in new[]
    {
        "", "()", "(B[hh])", "(;",
        "(;B[zz])", "(;B[a])", "(;B[aaa])", "(;B[HH])", "(;B[])", "(;B[ h])", "(;B[ap])", "(;SZ[5];B[ff])",
        "(;B[hh];B[ii])", "(;W[hh])", "(;B[hh]W[ii])", "(;B[hh][ii])", "(;B[hh]B[ii])", "(;B[hh];W[hh])",
        "(;SZ[4])", "(;SZ[26])", "(;SZ[15:15])", "(;SZ[x])", "(;SZ[15][19])", "(;SZ[15]SZ[15])", "(;;SZ[15])",
        "(;GM[1])", "(;PL[W])", "(;HA[1])", "(;AB[hh])", "(;AW[hh])", "(;AE[hh])",
        "(;B[hh](;W[ii]))", "(;B[hh](;W[ii])(;W[jj]))", "(;B[hh])(;W[ii])", "(;B[hh])trailing",
        "(;B[hh]junk)", "(;B[hh];W[zz];B[ii])", "(;B[hh];W[ii])garbage", "(;C[unterminated)", "(;C[trailing\\", "(;B[hh]];W[ii])",
        "(;B[aa];W[ac];B[ba];W[bc];B[ca];W[cc];B[da];W[dc];B[ea];W[ee])",
        "(" + new string(';', 4097) + ")"
    }) Invalid(text, "SGF invalid syntax/state fixture", ".sgf");
    string badUtf8 = Path.Combine(temporary, "invalid-utf8.sgf"); File.WriteAllBytes(badUtf8, [0x28, 0x3b, 0xff, 0x29]);
    try { SessionFiles.Load(badUtf8); throw new InvalidOperationException("Invalid UTF-8 accepted"); } catch (InvalidDataException) { passed++; }

    // A failed replacement must leave the previous valid bytes intact, without temporary files.
    SessionFiles.Save(save, future, 1);
    byte[] old = File.ReadAllBytes(save);
    using (var locked = new FileStream(save, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        try { SessionFiles.Save(save, board, 0); throw new InvalidOperationException("Locked save unexpectedly replaced"); }
        catch (IOException) { passed++; }
        catch (UnauthorizedAccessException) { passed++; }
    }
    Check(File.ReadAllBytes(save).SequenceEqual(old), "failed atomic replacement preserves previous JSON");
    byte[] oldSgf = File.ReadAllBytes(sgf);
    using (var locked = new FileStream(sgf, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        try { SessionFiles.SaveSgf(sgf, new GameBoard()); throw new InvalidOperationException("Locked SGF unexpectedly replaced"); }
        catch (IOException) { passed++; }
        catch (UnauthorizedAccessException) { passed++; }
    }
    Check(File.ReadAllBytes(sgf).SequenceEqual(oldSgf), "failed atomic replacement preserves previous SGF");
    Check(!Directory.EnumerateFiles(temporary, "*.tmp", SearchOption.AllDirectories).Any(), "atomic save temporary files cleaned");
    string result = JsonSerializer.Serialize(new { passed, failed = 0, timestampUtc = DateTimeOffset.UtcNow, maximumFileBytes = SessionFiles.MaximumFileBytes, jsonSchema = 1, sgf = "strict single main line; legacy empty nodes supported" }, new JsonSerializerOptions { WriteIndented = true });
    if (args.FirstOrDefault() is { } output) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); File.WriteAllText(output, result); }
    Console.WriteLine(result);
}
finally
{
    string absolute = Path.GetFullPath(temporary);
    string expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (absolute.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(absolute).StartsWith("liss-gomoku-storage-tests-", StringComparison.Ordinal)) Directory.Delete(absolute, true);
}
