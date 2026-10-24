using System.Text.Json;
using Liss.Gomoku.Core;

namespace Liss.Gomoku.App;

internal sealed record EnginePreferences(SearchProfile Black, SearchProfile White)
{
    internal static EnginePreferences CreateDefault() => new(new SearchProfile(), new SearchProfile());

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Black);
        ArgumentNullException.ThrowIfNull(White);
        Black.Validate(); White.Validate();
    }
}

/// <summary>
/// A small, independent schema containing engine settings only. Loading never
/// rewrites a damaged file; saving validates before touching a previous file.
/// </summary>
internal static class EnginePreferencesStore
{
    internal const int MaximumFileBytes = 64 * 1024;
    internal const int SchemaVersion = 1;
    private const int MaximumDepth = 8;

    internal static EnginePreferences Load(string path)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumFileBytes) return EnginePreferences.CreateDefault();
            // The read is bounded too, even if another process changes file length.
            byte[] bytes = new byte[MaximumFileBytes + 1];
            int length = 0;
            while (length < bytes.Length)
            {
                int read = stream.Read(bytes, length, bytes.Length - length);
                if (read == 0) break;
                length += read;
            }
            if (length > MaximumFileBytes) return EnginePreferences.CreateDefault();
            ReadOnlyMemory<byte> data = new(bytes, 0, length);
            if (data.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) data = data[3..];
            using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = MaximumDepth });
            var fields = Fields(document.RootElement, ["Version", "Black", "White"]);
            if (Integer(fields, "Version") != SchemaVersion) return EnginePreferences.CreateDefault();
            var preferences = new EnginePreferences(Profile(fields["Black"]), Profile(fields["White"]));
            preferences.Validate();
            return preferences;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or JsonException or NotSupportedException or System.Security.SecurityException)
        {
            return EnginePreferences.CreateDefault();
        }
    }

    internal static void Save(string path, EnginePreferences preferences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, MaxDepth = MaximumDepth }))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("Version", SchemaVersion);
                    WriteProfile(writer, "Black", preferences.Black);
                    WriteProfile(writer, "White", preferences.White);
                    writer.WriteEndObject();
                    writer.Flush();
                }
                if (stream.Length > MaximumFileBytes) throw new InvalidDataException("引擎设置超过 64 KiB 限制。");
                stream.Flush(flushToDisk: true);
            }
            // Same-directory replacement is atomic. A denied/locked target stays intact.
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void WriteProfile(Utf8JsonWriter writer, string name, SearchProfile profile)
    {
        writer.WriteStartObject(name);
        // Explicit fields prevent future model properties from leaking into this file.
        writer.WriteString("EngineId", profile.EngineId);
        writer.WriteNumber("Depth", profile.Depth);
        writer.WriteNumber("TimeBudgetMs", profile.TimeBudgetMs);
        writer.WriteString("EvaluationId", profile.EvaluationId);
        writer.WriteEndObject();
    }

    private static SearchProfile Profile(JsonElement value)
    {
        var fields = Fields(value, ["EngineId", "Depth", "TimeBudgetMs", "EvaluationId"]);
        return new SearchProfile(String(fields, "EngineId"), Integer(fields, "Depth"),
            Integer(fields, "TimeBudgetMs"), String(fields, "EvaluationId"));
    }
    private static Dictionary<string, JsonElement> Fields(JsonElement element, string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("引擎设置必须使用对象格式。");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
            if (!required.Contains(property.Name, StringComparer.Ordinal) || !fields.TryAdd(property.Name, property.Value))
                throw new InvalidDataException("引擎设置含有未知或重复字段。");
        if (fields.Count != required.Length) throw new InvalidDataException("引擎设置缺少必要字段。");
        return fields;
    }
    private static int Integer(Dictionary<string, JsonElement> fields, string key)
    {
        if (fields[key].ValueKind != JsonValueKind.Number || !fields[key].TryGetInt32(out int number))
            throw new InvalidDataException("引擎数值设置必须为有效整数。");
        return number;
    }
    private static string String(Dictionary<string, JsonElement> fields, string key)
    {
        if (fields[key].ValueKind != JsonValueKind.String || fields[key].GetString() is not { Length: > 0 } text)
            throw new InvalidDataException("引擎标识必须为非空字符串。");
        return text;
    }
}
