using System.Drawing.Drawing2D;
using Liss.Gomoku.Core;
using 五子棋;

namespace Liss.Gomoku.App;

internal sealed class BoardControl : Control
{
    private GameBoard board = new();
    private Move? hover;
    private Move keyboard = new(7, 7);
    private string? interactionNotice;
    private string[] noticeLines = [];
    private readonly Font noticeTitleFont = new("Microsoft YaHei UI", 11, FontStyle.Bold);
    private readonly Font noticeDetailFont = new("Microsoft YaHei UI", 9.5f);
    private readonly StringFormat noticeFormat = new()
    {
        Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
        FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter
    };
    internal event Action<int, int>? PlayRequested;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool ShowNumbers { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool InputEnabled { get; set; } = true;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Move? HintMove { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string? InteractionNotice
    {
        get => interactionNotice;
        set
        {
            if (string.Equals(interactionNotice, value, StringComparison.Ordinal)) return;
            interactionNotice = value;
            noticeLines = string.IsNullOrWhiteSpace(value) ? [] : value.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(2).ToArray();
            Invalidate();
        }
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal GameBoard Board
    {
        get => board;
        set { if (!ReferenceEquals(board, value)) { board = value; keyboard = new(value.Size / 2, value.Size / 2); hover = null; HintMove = null; } Invalidate(); }
    }

    internal BoardControl()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Background; TabStop = true;
        AccessibleName = "五子棋棋盘";
        AccessibleDescription = "方向键选择交点，Enter 或空格落子。";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }

    private (RectangleF Surface, float Left, float Top, float Step) Geometry()
    {
        float side = Math.Max(80, Math.Min(ClientSize.Width - 32, ClientSize.Height - 32));
        var rect = new RectangleF((Width - side) / 2, (Height - side) / 2, side, side);
        float padding = side * .075f;
        return (rect, rect.Left + padding, rect.Top + padding, (side - padding * 2) / (board.Size - 1));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var (surface, left, top, step) = Geometry();
        using var wood = new LinearGradientBrush(surface, Color.FromArgb(226, 204, 162), Color.FromArgb(193, 159, 109), 34f);
        using var border = new Pen(Color.FromArgb(131, 104, 62), 2);
        using var grid = new Pen(Color.FromArgb(126, 100, 62), Math.Max(.7f, step / 40));
        using var grain = new Pen(Color.FromArgb(13, 94, 61, 26), 1);
        g.FillRectangle(wood, surface);
        g.DrawRectangle(border, surface.X, surface.Y, surface.Width, surface.Height);
        for (int n = 1; n < 75; n++)
        {
            float y = surface.Top + n * surface.Height / 75;
            g.DrawBezier(grain, surface.Left, y, surface.Left + surface.Width * .3f, y - 4, surface.Left + surface.Width * .7f, y + 3, surface.Right, y);
        }
        float end = step * (board.Size - 1);
        using var labelBrush = new SolidBrush(Color.FromArgb(98, 75, 43));
        using var labels = new Font("Segoe UI", Math.Clamp(step * .26f, 7, 10), FontStyle.Regular, GraphicsUnit.Pixel);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < board.Size; i++)
        {
            g.DrawLine(grid, left + i * step, top, left + i * step, top + end);
            g.DrawLine(grid, left, top + i * step, left + end, top + i * step);
            g.DrawString(((char)('A' + i)).ToString(), labels, labelBrush, left + i * step, top - step * .62f, centered);
            g.DrawString((i + 1).ToString(), labels, labelBrush, left - step * .65f, top + i * step, centered);
        }
        using var star = new SolidBrush(Color.FromArgb(113, 87, 48));
        int a = board.Size >= 13 ? 3 : 2, b = board.Size - 1 - a, c = board.Size / 2;
        foreach (var point in new[] { new Move(a,a), new Move(b,a), new Move(a,b), new Move(b,b), new Move(c,c) }.Distinct())
            g.FillEllipse(star, left + point.X * step - 3, top + point.Y * step - 3, 6, 6);

        for (int y = 0; y < board.Size; y++)
            for (int x = 0; x < board.Size; x++)
                if (board.Cells[x, y] is var stone && stone != Stone.空)
                    DrawStone(g, left + x * step, top + y * step, step * .86f, stone == Stone.黑子, 255);

        if (ShowNumbers)
        {
            using var numberFont = new Font("Segoe UI", Math.Clamp(step * .28f, 8, 16), FontStyle.Bold, GraphicsUnit.Pixel);
            for (int n = 0; n < board.Cursor; n++)
            {
                var point = board.Moves[n];
                using var ink = new SolidBrush(n % 2 == 0 ? Color.FromArgb(217, 224, 232) : Color.FromArgb(50, 61, 73));
                g.DrawString((n + 1).ToString(), numberFont, ink, left + point.X * step, top + point.Y * step, centered);
            }
        }
        if (board.Cursor > 0)
        {
            var last = board.Moves[board.Cursor - 1];
            using var mark = new Pen(Color.FromArgb(222, 125, 55), Math.Max(1.5f, step / 14));
            float r = ShowNumbers ? step * .39f : step * .13f;
            g.DrawEllipse(mark, left + last.X * step - r, top + last.Y * step - r, r * 2, r * 2);
        }
        if (board.WinningLine.Count >= 5)
        {
            var points = board.WinningLine.Select(p => new PointF(left + p.X * step, top + p.Y * step)).ToArray();
            using var line = new Pen(Color.FromArgb(210, 40, 147, 120), Math.Max(3, step * .09f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(line, points[0], points[^1]);
        }
        if (HintMove is { } hint && board.Cells[hint.X, hint.Y] == Stone.空)
        {
            using var hintPen = new Pen(Color.FromArgb(24, 112, 102), 2) { DashStyle = DashStyle.Dash };
            float radius = step * .35f;
            g.DrawEllipse(hintPen, left + hint.X * step - radius, top + hint.Y * step - radius, radius * 2, radius * 2);
        }
        Move? preview = hover ?? (Focused ? keyboard : null);
        if (InputEnabled && !board.IsFinished && preview is { } move && board.Cells[move.X, move.Y] == Stone.空)
        {
            DrawStone(g, left + move.X * step, top + move.Y * step, step * .82f, board.BlackToMove, 100);
            using var cross = new Pen(Color.FromArgb(190, 52, 108, 98), 1);
            float x = left + move.X * step, y = top + move.Y * step;
            g.DrawLine(cross, x - 5, y, x + 5, y); g.DrawLine(cross, x, y - 5, x, y + 5);
        }
        if (Focused)
        {
            var focus = Rectangle.Round(surface); focus.Inflate(6, 6);
            ControlPaint.DrawFocusRectangle(g, focus, Theme.Muted, Theme.Background);
        }
        if (noticeLines.Length > 0) DrawInteractionNotice(g, surface);
    }

    private void DrawInteractionNotice(Graphics g, RectangleF surface)
    {
        float scale = DeviceDpi / 96f;
        float inset = Math.Min(22 * scale, surface.Width * .05f);
        float width = Math.Min(500 * scale, surface.Width - inset * 2);
        float height = (noticeLines.Length > 1 ? 88 : 60) * scale;
        var panel = new RectangleF(surface.Left + (surface.Width - width) / 2,
            surface.Top + (surface.Height - height) / 2, width, height);
        using var veil = new SolidBrush(Color.FromArgb(48, Theme.Background));
        g.FillRectangle(veil, surface);
        using var shape = NoticeShape(panel, 10 * scale);
        using var background = new SolidBrush(Color.FromArgb(246, Theme.Panel));
        using var outline = new Pen(Theme.Line, scale);
        using var title = new SolidBrush(Theme.Text);
        using var detail = new SolidBrush(Theme.Muted);
        g.FillPath(background, shape);
        g.DrawPath(outline, shape);
        float textInset = 18 * scale;
        var titleBounds = new RectangleF(panel.Left + textInset,
            noticeLines.Length > 1 ? panel.Top + 13 * scale : panel.Top,
            panel.Width - textInset * 2, noticeLines.Length > 1 ? 28 * scale : panel.Height);
        g.DrawString(noticeLines[0], noticeTitleFont, title, titleBounds, noticeFormat);
        if (noticeLines.Length > 1)
            g.DrawString(noticeLines[1], noticeDetailFont, detail,
                new RectangleF(panel.Left + textInset, panel.Top + 43 * scale, panel.Width - textInset * 2, 28 * scale), noticeFormat);
    }

    private static GraphicsPath NoticeShape(RectangleF rectangle, float radius)
    {
        float diameter = radius * 2;
        var shape = new GraphicsPath();
        shape.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        shape.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        shape.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        shape.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        shape.CloseFigure();
        return shape;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            noticeTitleFont.Dispose();
            noticeDetailFont.Dispose();
            noticeFormat.Dispose();
        }
        base.Dispose(disposing);
    }

    private static void DrawStone(Graphics g, float x, float y, float diameter, bool black, int alpha)
    {
        var rect = new RectangleF(x - diameter / 2, y - diameter / 2, diameter, diameter);
        using var shadow = new SolidBrush(Color.FromArgb(alpha / 4, 20, 24, 28));
        g.FillEllipse(shadow, rect.X + 1.5f, rect.Y + 2.5f, diameter, diameter);
        using var path = new GraphicsPath(); path.AddEllipse(rect);
        using var fill = new PathGradientBrush(path)
        {
            CenterPoint = new PointF(x - diameter * .18f, y - diameter * .22f),
            CenterColor = black ? Color.FromArgb(alpha, 65, 75, 87) : Color.FromArgb(alpha, 255, 255, 251),
            SurroundColors = [black ? Color.FromArgb(alpha, 13, 20, 28) : Color.FromArgb(alpha, 195, 197, 186)]
        };
        g.FillEllipse(fill, rect);
        using var edge = new Pen(black ? Color.FromArgb(alpha, 9, 16, 23) : Color.FromArgb(alpha, 135, 139, 129), .8f);
        g.DrawEllipse(edge, rect);
    }

    private Move? Hit(Point point)
    {
        var (_, left, top, step) = Geometry();
        int x = (int)Math.Round((point.X - left) / step), y = (int)Math.Round((point.Y - top) / step);
        if ((uint)x >= board.Size || (uint)y >= board.Size) return null;
        if (Math.Abs(point.X - (left + x * step)) > step * .46 || Math.Abs(point.Y - (top + y * step)) > step * .46) return null;
        return new Move(x, y);
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); var next = Hit(e.Location); if (hover != next) { hover = next; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = null; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Focus();
        if (e.Button == MouseButtons.Left && InputEnabled && Hit(e.Location) is { } move) PlayRequested?.Invoke(move.X, move.Y);
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var current = hover ?? keyboard;
        keyboard = e.KeyCode switch
        {
            Keys.Left => current with { X = Math.Max(0, current.X - 1) },
            Keys.Right => current with { X = Math.Min(board.Size - 1, current.X + 1) },
            Keys.Up => current with { Y = Math.Max(0, current.Y - 1) },
            Keys.Down => current with { Y = Math.Min(board.Size - 1, current.Y + 1) },
            _ => current
        };
        hover = null;
        if (e.KeyCode is Keys.Enter or Keys.Space && InputEnabled) PlayRequested?.Invoke(keyboard.X, keyboard.Y);
        if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Enter or Keys.Space) { e.Handled = true; e.SuppressKeyPress = true; Invalidate(); }
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
