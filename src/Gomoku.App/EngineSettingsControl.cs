using System.ComponentModel;
using Liss.Gomoku.Core;

namespace Liss.Gomoku.App;

/// <summary>Compact settings for one side; the host can put the two instances in separate tabs.</summary>
internal sealed class EngineSettingsControl : UserControl
{
    private readonly ComboBox _engine = Theme.Combo();
    private readonly ComboBox _evaluation = Theme.Combo();
    private readonly NumericUpDown _depth = Theme.Number(1, 8, 4);
    private readonly NumericUpDown _budget = Theme.Number(250, 10_000, 1500, 250);
    private readonly Label _description = Theme.Label("", 8.5f);
    private readonly ToolTip _tips = new() { InitialDelay = 350, AutoPopDelay = 12_000 };
    private bool _updating;

    internal EngineSettingsControl(string side)
    {
        side = string.IsNullOrWhiteSpace(side) ? "本方" : side.Trim();
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = new Font("Microsoft YaHei UI", 9.5f);
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        Size = new Size(294, 244);
        MinimumSize = new Size(244, 232);
        Margin = Padding.Empty;
        Padding = new Padding(10, 2, 10, 4);
        TabStop = false;
        AccessibleName = side + "搜索设置";

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Panel
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 23, 34, 23, 34, 23, 34 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _engine.DisplayMember = nameof(EngineDescriptor.Name);
        _engine.ValueMember = nameof(EngineDescriptor.Id);
        foreach (var engine in EngineCatalog.All) _engine.Items.Add(engine);
        _evaluation.Items.AddRange(["棋型评估", "五元组评估"]);
        _engine.TabIndex = 0; _depth.TabIndex = 1; _budget.TabIndex = 2; _evaluation.TabIndex = 3;
        _engine.AccessibleName = side + "搜索引擎";
        _depth.AccessibleName = side + "最大搜索深度";
        _budget.AccessibleName = side + "每步时间预算，毫秒";
        _evaluation.AccessibleName = side + "评估函数";
        _depth.ThousandsSeparator = false;
        _budget.ThousandsSeparator = true;

        layout.Controls.Add(Theme.Label(side + "搜索引擎"), 0, 0);
        layout.Controls.Add(_engine, 0, 1);
        var parameterLabels = TwoColumns();
        parameterLabels.Controls.Add(Theme.Label("最大深度"), 0, 0);
        parameterLabels.Controls.Add(Theme.Label("预算 / ms"), 1, 0);
        layout.Controls.Add(parameterLabels, 0, 2);
        var parameters = TwoColumns();
        _depth.Margin = new Padding(0, 4, 7, 4);
        _budget.Margin = new Padding(7, 4, 0, 4);
        parameters.Controls.Add(_depth, 0, 0);
        parameters.Controls.Add(_budget, 1, 0);
        layout.Controls.Add(parameters, 0, 3);
        layout.Controls.Add(Theme.Label("评估函数"), 0, 4);
        layout.Controls.Add(_evaluation, 0, 5);
        _description.TextAlign = ContentAlignment.TopLeft;
        _description.Padding = new Padding(0, 5, 0, 0);
        _description.Margin = Padding.Empty;
        _description.UseMnemonic = false;
        layout.Controls.Add(_description, 0, 6);
        Controls.Add(layout);

        _tips.SetToolTip(_engine, "只修改" + side + "的搜索方法；不会同步另一方的设置。");
        _tips.SetToolTip(_depth, "搜索深度上限。实际完整完成的层数见思考统计；限时可能提前停止。");
        _tips.SetToolTip(_budget, "该方每步的墙钟时间预算，范围 250–10,000 ms；取消检查和准备工作可能带来少量额外耗时。");
        _tips.SetToolTip(_evaluation, "选择局面评估方式。单层五元组自带计分，不使用此项。");

        Profile = new SearchProfile();
        _engine.SelectedIndexChanged += OnProfileChanged;
        _evaluation.SelectedIndexChanged += OnProfileChanged;
        _depth.ValueChanged += OnProfileChanged;
        _budget.ValueChanged += OnProfileChanged;
    }

    /// <summary>Programmatic assignment is quiet; ProfileChanged is raised only by user field changes.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal SearchProfile Profile
    {
        get => new((_engine.SelectedItem as EngineDescriptor)?.Id ?? "threat-search", (int)_depth.Value,
            (int)_budget.Value, _evaluation.SelectedIndex == 1 ? "quintuple" : "patterns");
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            value.Validate();
            _updating = true;
            try
            {
                _engine.SelectedIndex = EngineCatalog.All.ToList().FindIndex(engine => engine.Id == value.EngineId);
                _depth.Value = value.Depth;
                _budget.Value = value.TimeBudgetMs;
                _evaluation.SelectedIndex = value.EvaluationId == "quintuple" ? 1 : 0;
                UpdateEngineDescription();
            }
            finally { _updating = false; }
        }
    }

    internal event EventHandler? ProfileChanged;

    private static TableLayoutPanel TwoColumns()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Panel
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return panel;
    }

    private void OnProfileChanged(object? sender, EventArgs e)
    {
        if (_updating) return;
        UpdateEngineDescription();
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateEngineDescription()
    {
        var engine = _engine.SelectedItem as EngineDescriptor;
        var isSingleLayer = engine?.Id == "legacy-quintuple";
        // Keep both values intact so switching back restores the user's preferences.
        _depth.Enabled = !isSingleLayer;
        _evaluation.Enabled = !isSingleLayer;
        _description.Text = engine is null ? "选择一种搜索引擎。" : engine.Description +
            (isSingleLayer ? "\n单层算法不使用深度及评估选项。" : "");
        _tips.SetToolTip(_description, _description.Text);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}
