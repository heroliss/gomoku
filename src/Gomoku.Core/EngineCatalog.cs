using 五子棋;

namespace Liss.Gomoku.Core;

public sealed record SearchOptions(string EngineId, int Depth = 4, int TimeBudgetMs = 1500, string EvaluationId = "patterns");
public sealed record SearchResult(Move? Move, long Nodes, int Depth, int TableHits, int ElapsedMs, string Detail);
public sealed record EngineDescriptor(string Id, string Name, string Description);

public static class EngineCatalog
{
    public static IReadOnlyList<EngineDescriptor> All { get; } = Array.AsReadOnly(new[]
    {
        new EngineDescriptor("threat-search", "V3 · 威胁搜索", "战术优先、迭代加深、置换表与限时搜索。"),
        new EngineDescriptor("legacy-negamax", "经典 · 负极大值", "保留原版 NegaMax 的全分支极大值搜索。"),
        new EngineDescriptor("legacy-alpha-beta", "经典 · Alpha–Beta", "保留原版负极大值结构与窗口剪枝。"),
        new EngineDescriptor("legacy-fail-soft", "经典 · Fail-soft", "保留原版 Fail-soft Alpha–Beta。"),
        new EngineDescriptor("legacy-aspiration", "经典 · 渴望搜索", "先浅搜估值，再进行窄窗口与失败重搜。"),
        new EngineDescriptor("legacy-history", "经典 · 历史启发", "Alpha–Beta 与历史得分排序。"),
        new EngineDescriptor("legacy-table", "经典 · 置换表", "Alpha–Beta 与已修复的 Zobrist 置换表。"),
        new EngineDescriptor("legacy-pvs", "经典 · PVS", "主变例全窗口，其余走法空窗探测与重搜。"),
        new EngineDescriptor("legacy-pvs-history", "经典 · PVS + 历史启发", "保留原版 NegaScout_HH 搜索分支。"),
        new EngineDescriptor("legacy-pvs-table", "经典 · PVS + HH + TT", "保留原版 NegaScout_HH_TT，修复表写入与哈希。"),
        new EngineDescriptor("legacy-quintuple", "经典 · 五元组单层", "枚举五元组，为可落点累加攻防分数。")
    });

    public static SearchResult Search(GameBoard snapshot, SearchOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        return SearchImplementation.Run(snapshot, options, cancellationToken);
    }
}

