using System.Globalization;
using System.Text;
using System.Text.Json;
using Liss.Gomoku.Core;

namespace Liss.Gomoku.App;

internal sealed record SavedGame(int Version, int BoardSize, Move[] Moves, int Cursor, int Mode = 0);

internal static class SessionFiles
{
    internal const int MaximumFileBytes = 1_048_576;
    internal static string DataDirectory => Environment.GetEnvironmentVariable("LISS_GOMOKU_DATA_DIR") is { Length: > 0 } path
        ? Path.GetFullPath(path) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Liss", "Gomoku3");
    internal static string AutoSavePath => Path.Combine(DataDirectory, "last-game.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 16 };
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Save(string path, GameBoard board, int mode)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (mode is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(mode));
        AtomicSave(path, stream => JsonSerializer.Serialize(stream, new SavedGame(1, board.Size, board.Moves.ToArray(), board.Cursor, mode), JsonOptions));
    }

    private static void AtomicSave(string path, Action<FileStream> write)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        // Same-directory rename keeps a previous valid save intact if writing or flushing fails.
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(true);
            }
            File.Move(temporary, full, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static (GameBoard Board, int Mode) Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("棋谱超过 1 MiB 限制。");
        // Enforce the limit while reading too, rather than relying only on a racy FileInfo check.
        byte[] buffer = new byte[MaximumFileBytes + 1];
        int length = 0;
        while (length < buffer.Length)
        {
            int count = stream.Read(buffer, length, buffer.Length - length);
            if (count == 0) break;
            length += count;
        }
        if (length > MaximumFileBytes) throw new InvalidDataException("棋谱超过 1 MiB 限制。");
        var data = new ReadOnlyMemory<byte>(buffer, 0, length);
        if (data.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) data = data[3..];
        if (Path.GetExtension(path).Equals(".sgf", StringComparison.OrdinalIgnoreCase))
        {
            try { return (new SgfReader(StrictUtf8.GetString(data.Span)).Read(), 2); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("SGF 需要 UTF-8 或 ASCII 编码。", error); }
        }
        try
        {
            using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 16 });
            var fields = Fields(document.RootElement, ["Version", "BoardSize", "Moves", "Cursor", "Mode"]);
            int version = Integer(fields, "Version"), size = Integer(fields, "BoardSize"), cursor = Integer(fields, "Cursor");
            int mode = fields.ContainsKey("Mode") ? Integer(fields, "Mode") : 0;
            if (version != 1 || size is < 5 or > 25 || mode is < 0 or > 3)
                throw new InvalidDataException("棋谱仅支持版本 1、5–25 路棋盘和有效的对局模式。");
            if (!fields.TryGetValue("Moves", out JsonElement moves) || moves.ValueKind != JsonValueKind.Array || moves.GetArrayLength() > size * size || cursor < 0 || cursor > moves.GetArrayLength())
                throw new InvalidDataException("棋谱的走法列表或游标无效。");
            var board = new GameBoard(size);
            foreach (JsonElement value in moves.EnumerateArray())
            {
                var move = Fields(value, ["X", "Y"]);
                if (!board.TryPlay(Integer(move, "X"), Integer(move, "Y")))
                    throw new InvalidDataException("棋谱含有越界、重复落点或终局后的走法。");
            }
            int undo = board.Cursor - cursor;
            if (undo > 0) board.Undo(undo); // Undo(0) is deliberately a no-op, including at a completed game.
            return (board, mode);
        }
        catch (JsonException error) { throw new InvalidDataException("JSON 棋谱格式损坏或层级过深。", error); }
    }

    private static Dictionary<string, JsonElement> Fields(JsonElement element, string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("棋谱字段需要对象格式。");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !fields.TryAdd(property.Name, property.Value))
                throw new InvalidDataException("棋谱含有未知或重复字段。");
        return fields;
    }
    private static int Integer(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
            throw new InvalidDataException($"棋谱字段 {name} 需要完整的整数值。");
        return number;
    }

    internal static void SaveSgf(string path, GameBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var text = new StringBuilder($"(;FF[4]GM[4]CA[UTF-8]AP[Liss Gomoku:3.0]SZ[{board.Size}]");
        // SGF stores the complete main line; JSON additionally remembers the review cursor and mode.
        for (int i = 0; i < board.Moves.Count; i++)
        {
            var move = board.Moves[i];
            text.Append(';').Append(i % 2 == 0 ? 'B' : 'W').Append('[').Append((char)('a' + move.X)).Append((char)('a' + move.Y)).Append(']');
        }
        text.Append(')');
        byte[] bytes = StrictUtf8.GetBytes(text.ToString());
        AtomicSave(path, stream => stream.Write(bytes));
    }

    // A deliberately small SGF main-line parser. Brackets and escapes are parsed before tree syntax,
    // so comments can contain parentheses without masquerading as variations. Unsupported game-state
    // properties are rejected explicitly; malformed moves are never silently skipped by a regex.
    private sealed class SgfReader(string text)
    {
        private int index;
        internal GameBoard Read()
        {
            Skip(); Require('('); Skip();
            GameBoard? board = null;
            int nodes = 0;
            while (Peek() == ';')
            {
                index++; Skip();
                if (++nodes > 4096) throw Error("SGF 节点数量过多。");
                var properties = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                while (Peek() is >= 'A' and <= 'Z')
                {
                    int start = index;
                    while (Peek() is >= 'A' and <= 'Z') index++;
                    if (index - start > 64) throw Error("SGF 属性名称过长。");
                    string name = text[start..index]; Skip();
                    var values = new List<string>();
                    if (Peek() != '[') throw Error("SGF 属性缺少方括号值。");
                    while (Peek() == '[') { values.Add(Value()); Skip(); }
                    if (!properties.TryAdd(name, values)) throw Error("SGF 节点中出现重复属性。");
                }
                if (properties.ContainsKey("AB") || properties.ContainsKey("AW") || properties.ContainsKey("AE"))
                    throw Error("暂不支持 SGF 预设/移除棋子，请导出从空盘开始的主线。");
                if (nodes > 1 && (properties.ContainsKey("SZ") || properties.ContainsKey("GM") || properties.ContainsKey("PL") || properties.ContainsKey("HA")))
                    throw Error("SGF 棋盘和规则属性只能出现在根节点。");
                if (nodes == 1)
                {
                    int size = 15;
                    if (properties.ContainsKey("SZ") && (!int.TryParse(One(properties, "SZ"), NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 5 or > 25))
                        throw Error("SGF 棋盘大小无效。");
                    if (properties.ContainsKey("GM") && One(properties, "GM") != "4") throw Error("这不是五子棋 SGF（GM[4]）。");
                    if (properties.ContainsKey("PL") && One(properties, "PL") != "B") throw Error("暂不支持白棋先行的 SGF。");
                    if (properties.ContainsKey("HA") && One(properties, "HA") != "0") throw Error("暂不支持让子 SGF。");
                    board = new GameBoard(size);
                }
                bool hasBlack = properties.ContainsKey("B"), hasWhite = properties.ContainsKey("W");
                if (hasBlack && hasWhite) throw Error("一个 SGF 节点不能同时包含黑白落子。");
                if (hasBlack || hasWhite)
                {
                    string coordinate = One(properties, hasBlack ? "B" : "W");
                    if (coordinate.Length != 2 || coordinate.Any(c => c is < 'a' or > 'y'))
                        throw Error("SGF 含有无效坐标或跳过回合；五子棋不能停着。");
                    if (hasBlack != board!.BlackToMove || !board.TryPlay(coordinate[0] - 'a', coordinate[1] - 'a'))
                        throw Error("SGF 的颜色顺序、落点或终局后的走法不合法。");
                }
                Skip();
            }
            if (nodes == 0) throw Error("SGF 缺少根节点。");
            if (Peek() == '(') throw Error("暂不支持含分支的 SGF，请导出单一主线。");
            Require(')'); Skip();
            if (index != text.Length) throw Error("SGF 根节点后仍有内容或多个棋谱。");
            return board!;
        }
        private string One(Dictionary<string, List<string>> properties, string name)
        {
            if (properties[name].Count != 1) throw Error($"SGF 属性 {name} 只能有一个值。");
            return properties[name][0];
        }
        private string Value()
        {
            Require('[');
            var value = new StringBuilder();
            while (index < text.Length)
            {
                char c = text[index++];
                if (c == ']') return value.ToString();
                if (c == '\\')
                {
                    if (index == text.Length) throw Error("SGF 转义不完整。");
                    c = text[index++];
                    if (c == '\r') { if (Peek() == '\n') index++; continue; }
                    if (c == '\n') continue;
                }
                value.Append(c);
            }
            throw Error("SGF 属性值没有结束方括号。");
        }
        private char Peek() => index < text.Length ? text[index] : '\0';
        private void Skip() { while (index < text.Length && char.IsWhiteSpace(text[index])) index++; }
        private void Require(char expected) { if (Peek() != expected) throw Error($"SGF 缺少 {expected} 或格式损坏。"); index++; }
        private InvalidDataException Error(string message) => new($"{message}（位置 {index}）");
    }

    internal static void Log(Exception error)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            string file = Path.Combine(DataDirectory, "errors.log");
            if (File.Exists(file) && new FileInfo(file).Length > 500_000) File.Move(file, file + ".previous", true);
            File.AppendAllText(file, $"{DateTimeOffset.Now:O} {error.GetType().Name}: {error.Message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
