using System.Drawing.Drawing2D;

namespace Liss.Gomoku.App;

internal static class Theme
{
    internal static readonly Color Background = Color.FromArgb(13, 21, 33);
    internal static readonly Color Panel = Color.FromArgb(23, 35, 50);
    internal static readonly Color Field = Color.FromArgb(16, 27, 41);
    internal static readonly Color Text = Color.FromArgb(230, 237, 246);
    internal static readonly Color Muted = Color.FromArgb(148, 167, 188);
    internal static readonly Color Accent = Color.FromArgb(120, 217, 202);
    internal static readonly Color Line = Color.FromArgb(46, 66, 86);

    internal static Label Label(string text, float size = 9.5f, bool strong = false) => new()
    {
        Text = text, AutoSize = false, ForeColor = strong ? Text : Muted,
        Font = new Font("Microsoft YaHei UI", size, strong ? FontStyle.Bold : FontStyle.Regular),
        TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill,
        Margin = new Padding(0, 3, 0, 3)
    };

    internal static Button Button(string text, EventHandler? action = null, bool primary = false)
    {
        var button = new Button
        {
            Text = text, Height = 36, AutoSize = false, FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Panel, ForeColor = primary ? Background : Text,
            Cursor = Cursors.Hand, Margin = new Padding(0, 4, 8, 4), TabStop = true
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Line;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(150, 235, 219) : Color.FromArgb(36, 53, 72);
        if (action is not null) button.Click += action;
        return button;
    }

    internal static ComboBox Combo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill,
        FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Text,
        Margin = new Padding(0, 4, 0, 4), IntegralHeight = false, DropDownHeight = 330
    };

    internal static TextBox Input(string value = "") => new()
    {
        Text = value, BorderStyle = BorderStyle.FixedSingle, BackColor = Field,
        ForeColor = Text, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4), MaxLength = 32
    };

    internal static NumericUpDown Number(int min, int max, int value, int increment = 1) => new()
    {
        Minimum = min, Maximum = max, Value = value, Increment = increment,
        Dock = DockStyle.Fill, BackColor = Field, ForeColor = Text,
        BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 0, 4)
    };

    internal static TableLayoutPanel Stack() => new()
    {
        ColumnCount = 1, RowCount = 0, Dock = DockStyle.Top, AutoSize = true,
        BackColor = Panel, Padding = new Padding(18, 12, 18, 16), Margin = Padding.Empty
    };

    internal static void AddRow(TableLayoutPanel stack, Control control, int height)
    {
        int row = stack.RowCount++;
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        stack.Controls.Add(control, 0, row);
    }

    internal static FlowLayoutPanel Buttons(params Button[] buttons)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        foreach (var button in buttons) panel.Controls.Add(button);
        return panel;
    }
}
