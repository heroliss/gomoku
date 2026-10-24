using System.Text;
using System.Text.Json;
using Liss.Gomoku.App;
using Liss.Gomoku.Core;

string temporary = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "liss-gomoku-preferences-tests-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(temporary);
var checks = new List<object>();
void Check(bool condition, string name)
{
    checks.Add(new { name, passed = condition });
    Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
    if (!condition) throw new InvalidOperationException(name);
}
string Fixture(string text, bool bom = false)
{
    string path = Path.Combine(temporary, Guid.NewGuid().ToString("N") + ".json");
    File.WriteAllText(path, text, new UTF8Encoding(bom)); return path;
}
void Defaults(string text, string name) => Check(EnginePreferencesStore.Load(Fixture(text)) == EnginePreferences.CreateDefault(), name);
void RejectedSave(EnginePreferences preferences, string path, string name)
{
    byte[] before = File.ReadAllBytes(path);
    try { EnginePreferencesStore.Save(path, preferences); throw new InvalidOperationException("Invalid preferences saved."); }
    catch (ArgumentException) { }
    Check(File.ReadAllBytes(path).SequenceEqual(before), name);
}
try
{
    var defaults = EnginePreferences.CreateDefault();
    Check(defaults.Black == new SearchProfile() && defaults.White == new SearchProfile() && !ReferenceEquals(defaults.Black, defaults.White), "fresh independent default profiles");
    Check(EnginePreferencesStore.Load(Path.Combine(temporary, "missing.json")) == defaults, "missing file returns defaults");
    Check(EnginePreferencesStore.Load("") == defaults && EnginePreferencesStore.Load(null!) == defaults, "invalid path safely returns defaults");
    Check(EnginePreferencesStore.Load(temporary) == defaults, "directory path safely returns defaults");
    string target = Path.Combine(temporary, "settings", "engine-preferences.json");
    var independent = new EnginePreferences(new SearchProfile("legacy-alpha-beta", 1, 250, "quintuple"), new SearchProfile("legacy-pvs-table", 8, 10000, "patterns"));
    EnginePreferencesStore.Save(target, independent);
    Check(EnginePreferencesStore.Load(target) == independent, "black and white independent lower/upper boundaries round trip");
    string valid = File.ReadAllText(target);
    using (var doc = JsonDocument.Parse(valid))
    {
        var root = doc.RootElement;
        Check(root.GetProperty("Version").GetInt32() == 1 && root.EnumerateObject().Count() == 3, "separate schema 1 contains only Version/Black/White");
        Check(root.GetProperty("Black").EnumerateObject().Count() == 4 && root.GetProperty("White").EnumerateObject().Count() == 4
            && !valid.Contains("username", StringComparison.OrdinalIgnoreCase) && !valid.Contains("credential", StringComparison.OrdinalIgnoreCase), "explicit profile fields exclude identity and match statistics");
    }
    var changedBlack = independent with { Black = independent.Black with { Depth = 3, TimeBudgetMs = 750 } };
    EnginePreferencesStore.Save(target, changedBlack);
    var reloaded = EnginePreferencesStore.Load(target);
    Check(reloaded.Black == changedBlack.Black && reloaded.White == independent.White, "changing black never overwrites white settings");
    var changedWhite = reloaded with { White = reloaded.White with { EngineId = "legacy-quintuple", EvaluationId = "quintuple" } };
    EnginePreferencesStore.Save(target, changedWhite);
    Check(EnginePreferencesStore.Load(target) == changedWhite, "changing white never overwrites black settings");
    Check(EnginePreferencesStore.Load(Fixture(valid, true)) == independent, "UTF-8 BOM is accepted");

    foreach (string malformed in new[] { "", "null", "[]", "{}", "{", valid + valid,
        valid.Replace("\"Version\": 1", "\"Version\": 2"),
        valid.Replace("\"Version\": 1", "\"Version\": \"1\""),
        valid.Replace("\"Version\": 1", "\"Version\": 1.5"),
        valid.Replace("\"Version\": 1", "\"Version\": 1, \"Version\": 1"),
        valid.Replace("\"Version\": 1", "\"version\": 1"),
        valid.Replace("\"Version\": 1", "\"Version\": 1, \"Extra\": true"),
        valid.Replace("\"Depth\": 1", "\"Depth\": 1, \"Extra\": true"),
        valid.Replace("\"Depth\": 1", "\"Depth\": 1, \"Depth\": 2"),
        valid.Replace("\"Depth\": 1", "\"Depth\": null"),
        valid.Replace("\"Depth\": 1", "\"Depth\": 2147483648"),
        valid.Replace("\"Depth\": 1", "\"Depth\": 1.1"),
        valid.Replace("\"Depth\": 1", "\"Depth\": \"1\""),
        valid.Replace("\"legacy-alpha-beta\"", "null"),
        valid.Replace("\"legacy-alpha-beta\"", "\"unknown-engine\""),
        valid.Replace("\"quintuple\"", "\"unknown-evaluation\""),
        valid.Replace("\"Depth\": 1", "\"Depth\": 0"),
        valid.Replace("\"Depth\": 1", "\"Depth\": 9"),
        valid.Replace("\"TimeBudgetMs\": 250", "\"TimeBudgetMs\": 249"),
        valid.Replace("\"TimeBudgetMs\": 10000", "\"TimeBudgetMs\": 10001"),
        valid.Replace("\"Depth\": 1,", ""),
        "{\"Version\":1,\"Black\":null,\"White\":null}",
        "{\"Version\":1,\"Black\":[],\"White\":[]}",
        "{\"x\":" + new string('[', 9) + "0" + new string(']', 9) + "}"
    }) Defaults(malformed, "invalid schema/JSON/range safely falls back");

    string boundary = Fixture(valid + new string(' ', EnginePreferencesStore.MaximumFileBytes - Encoding.UTF8.GetByteCount(valid)));
    Check(new FileInfo(boundary).Length == 65536 && EnginePreferencesStore.Load(boundary) == independent, "exact 64 KiB valid file accepted");
    File.AppendAllText(boundary, " ");
    Check(EnginePreferencesStore.Load(boundary) == defaults, "64 KiB plus one byte safely falls back");
    string invalidUtf8 = Path.Combine(temporary, "invalid-utf8.json");
    File.WriteAllBytes(invalidUtf8, [0xff, 0xfe, 0xff]);
    Check(EnginePreferencesStore.Load(invalidUtf8) == defaults, "invalid UTF-8 safely falls back");
    string corrupt = Fixture("{broken"); byte[] corruptBefore = File.ReadAllBytes(corrupt);
    EnginePreferencesStore.Load(corrupt);
    Check(File.ReadAllBytes(corrupt).SequenceEqual(corruptBefore), "load never rewrites a damaged file");

    EnginePreferencesStore.Save(target, independent);
    RejectedSave(independent with { Black = independent.Black with { Depth = 0 } }, target, "invalid black save preserves previous bytes");
    RejectedSave(independent with { White = independent.White with { TimeBudgetMs = 10001 } }, target, "invalid white save preserves previous bytes");
    RejectedSave(independent with { Black = null! }, target, "null black save rejected before writing");
    RejectedSave(independent with { White = null! }, target, "null white save rejected before writing");
    byte[] oldBytes = File.ReadAllBytes(target);
    using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        bool failed = false;
        try { EnginePreferencesStore.Save(target, changedWhite); }
        catch (IOException) { failed = true; }
        catch (UnauthorizedAccessException) { failed = true; }
        Check(failed, "locked destination causes save failure");
    }
    Check(File.ReadAllBytes(target).SequenceEqual(oldBytes), "failed atomic replacement preserves original bytes");
    Check(!Directory.EnumerateFiles(Path.GetDirectoryName(target)!, "*.tmp").Any(), "failed replacement removes unique temporary file");
    using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
        Check(EnginePreferencesStore.Load(target) == defaults, "unreadable locked file safely falls back");
    Check(File.ReadAllBytes(target).SequenceEqual(oldBytes), "unreadable load preserves stored bytes");
    EnginePreferencesStore.Save(target, changedWhite);
    Check(EnginePreferencesStore.Load(target) == changedWhite, "save succeeds after lock released");
    if (args.FirstOrDefault(a => a.StartsWith("--report=")) is { } report)
    {
        string path = Path.GetFullPath(report[9..]); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { atUtc = DateTimeOffset.UtcNow, passed = checks.Count, failed = 0, checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
    Console.WriteLine($"ALL CHECKS PASSED: {checks.Count}");
}
finally
{
    string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
    if (Path.GetDirectoryName(temporary) != tempParent || !Path.GetFileName(temporary).StartsWith("liss-gomoku-preferences-tests-", StringComparison.Ordinal))
        throw new InvalidOperationException("Refusing cleanup outside this test's generated temporary directory.");
    Directory.Delete(temporary, recursive: true);
}
