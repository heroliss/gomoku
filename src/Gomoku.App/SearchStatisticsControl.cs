using System.Drawing.Drawing2D;
using System.Globalization;
using Liss.Gomoku.Core;

namespace Liss.Gomoku.App;

/// <summary>Wall-clock search totals per side, not CPU utilization or a strength rating.</summary>
internal sealed class SearchStatisticsControl : Control
{
    private SideSearchSummary? _black;
    private SideSearchSummary? _white;
    private readonly ToolTip _tips = new() { InitialDelay = 350, AutoPopDelay = 12_000 };

    internal SearchStatisticsControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        Font = new Font("Microsoft YaHei UI", 8.5f);
        Size = new Size(294, 190);
        MinimumSize = new Size(244, 184);
        Margin = Padding.Empty;
        TabStop = false;
        AccessibleName = "黑白双方引擎思考统计";
        AccessibleRole = AccessibleRole.StaticText;
        _tips.SetToolTip(this, "耗时是墙钟时间，不是 CPU 利用率。仅统计成功用于落子的引擎决策；提示和作废搜索不计入。节点与命中为该方累计，深度为最近一次完整完成的层数。");
        UpdateAccessibleSummary();
    }

    internal void SetSummaries(SideSearchSummary black, SideSearchSummary white)
    {
        ArgumentNullException.ThrowIfNull(black);
        ArgumentNullException.ThrowIfNull(white);
        _black = black; _white = white;
        UpdateAccessibleSummary();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var gap = 8f * scale;
        var half = (ClientSize.Width - gap) / 2f;
        if (half < 1 || Height < 1) return;
        DrawSide(graphics, new RectangleF(0, 0, half, ClientSize.Height - 1), "黑方", _black, Theme.Accent, false, scale);
        DrawSide(graphics, new RectangleF(half + gap, 0, half, ClientSize.Height - 1), "白方", _white, Color.FromArgb(239, 215, 174), true, scale);
    }

    private static void DrawSide(Graphics graphics, RectangleF bounds, string side, SideSearchSummary? summary, Color accent, bool white, float scale)
    {
        using var surface = new SolidBrush(Theme.Field);
        using var border = new Pen(Theme.Line);
        graphics.FillRectangle(surface, bounds);
        graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var inset = 9f * scale;
        var left = bounds.Left + inset;
        var right = bounds.Right - inset;
        var width = Math.Max(1, right - left);
        using var titleFont = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
        using var mainFont = new Font("Microsoft YaHei UI", 13, FontStyle.Bold);
        using var labelFont = new Font("Microsoft YaHei UI", 8.2f);
        using var smallFont = new Font("Microsoft YaHei UI", 7.8f);
        using var text = new SolidBrush(Theme.Text);
        using var muted = new SolidBrush(Theme.Muted);
        using var primary = new SolidBrush(accent);
        using var stone = new SolidBrush(white ? Color.FromArgb(223, 230, 234) : Theme.Background);
        using var textFormat = TextFormat();
        var dot = new RectangleF(left, 10 * scale, 9 * scale, 9 * scale);
        graphics.FillEllipse(stone, dot); graphics.DrawEllipse(border, dot);
        graphics.DrawString(side, titleFont, text, left + 15 * scale, 5 * scale);

        var hasData = summary is { Moves: > 0 };
        string recent = hasData ? Math.Max(0, summary!.LastElapsedMs).ToString("N0", CultureInfo.CurrentCulture) + " ms" : "—";
        graphics.DrawString(recent, mainFont, primary, new RectangleF(left, 28 * scale, width, 27 * scale), textFormat);
        graphics.DrawString("最近思考", smallFont, muted, left, 54 * scale);

        DrawMetric("次数", hasData ? summary!.Moves.ToString("N0", CultureInfo.CurrentCulture) : "0", 76);
        DrawMetric("累计", hasData ? (Math.Max(0, summary!.TotalElapsedMs) / 1000d).ToString("0.00", CultureInfo.CurrentCulture) + " s" : "—", 98);
        DrawMetric("平均", hasData ? Math.Max(0, summary!.AverageElapsedMs).ToString("0.0", CultureInfo.CurrentCulture) + " ms" : "—", 120);
        var lineY = 146 * scale;
        graphics.DrawLine(border, left, lineY, right, lineY);
        string nodes = hasData ? Compact(summary!.Nodes) : "—";
        string hits = hasData ? Compact(summary!.TableHits) : "—";
        graphics.DrawString("累计节点 " + nodes, smallFont, muted, new RectangleF(left, 151 * scale, width, 17 * scale), textFormat);
        graphics.DrawString("命中 " + hits + "  /  深度 " + (hasData ? summary!.LastDepth.ToString(CultureInfo.CurrentCulture) : "—"), smallFont, muted,
            new RectangleF(left, 169 * scale, width, 17 * scale), textFormat);

        void DrawMetric(string caption, string value, float y)
        {
            var row = new RectangleF(left, y * scale, width, 19 * scale);
            graphics.DrawString(caption, labelFont, muted, row, textFormat);
            using var alignRight = TextFormat(); alignRight.Alignment = StringAlignment.Far;
            graphics.DrawString(value, labelFont, text, row, alignRight);
        }
    }

    private static StringFormat TextFormat() => new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap,
        Trimming = StringTrimming.EllipsisCharacter,
        LineAlignment = StringAlignment.Near
    };

    private static string Compact(long number)
    {
        number = Math.Max(0, number);
        return number switch
        {
            >= 1_000_000_000 => (number / 1_000_000_000d).ToString("0.0", CultureInfo.CurrentCulture) + "B",
            >= 1_000_000 => (number / 1_000_000d).ToString("0.0", CultureInfo.CurrentCulture) + "M",
            >= 10_000 => (number / 1000d).ToString("0.0", CultureInfo.CurrentCulture) + "k",
            _ => number.ToString("N0", CultureInfo.CurrentCulture)
        };
    }

    private void UpdateAccessibleSummary()
    {
        static string Describe(string side, SideSearchSummary? value) => value is not { Moves: > 0 }
            ? side + "尚无已落子的引擎决策。"
            : $"{side}，{value.Moves} 次，最近 {value.LastElapsedMs} 毫秒，累计 {value.TotalElapsedMs / 1000d:0.00} 秒，平均 {value.AverageElapsedMs:0.0} 毫秒，累计节点 {value.Nodes}，累计命中 {value.TableHits}，最近完整深度 {value.LastDepth}。";
        AccessibleDescription = Describe("黑方", _black) + Describe("白方", _white);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}
