using Liss.Gomoku.Core;
using Liss.Gomoku.Networking;
using 五子棋;

namespace Liss.Gomoku.App;

internal sealed class MainForm : Form
{
    private GameBoard _game = new();
    private readonly BoardControl _board = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label _turn = Theme.Label("黑方先行", 18, true);
    private readonly Label _position = Theme.Label("自由规则 · 连成五子或以上获胜");
    private readonly Label _footer = Theme.Label("准备就绪");
    private readonly Label _diagnostics = Theme.Label("尚未搜索\n选择引擎，开始一局或请求提示。", 10);
    private readonly Label _engineDescription = Theme.Label("");
    private readonly Label _connection = Theme.Label("尚未连接");
    private readonly ComboBox _mode = Theme.Combo(), _size = Theme.Combo();
    private readonly ComboBox _blackEngine = Theme.Combo(), _whiteEngine = Theme.Combo(), _evaluation = Theme.Combo();
    private readonly NumericUpDown _depth = Theme.Number(1, 8, 4), _budget = Theme.Number(250, 10000, 1500, 250);
    private readonly ListBox _history = new(), _rooms = new();
    private readonly TextBox _nickname = Theme.Input("Liss"), _roomName = Theme.Input("Liss 的棋室");
    private readonly Panel[] _pages = [new(), new(), new()];
    private readonly Button[] _tabs = new Button[3];
    private readonly Button _newGame, _undo, _hint, _pause, _leave;
    private readonly PhotonLobbyService _network = new();
    private readonly System.Windows.Forms.Timer _networkTimer = new() { Interval = 30 };
    private CancellationTokenSource? _search;
    private int _generation;
    private bool _thinking, _paused, _closing, _online, _reviewing, _updatingHistory, _initializing = true;
    private readonly bool _showcase;
    private readonly ToolTip _tips = new() { AutoPopDelay = 12000, InitialDelay = 350 };

    internal MainForm(bool showcase, string[]? startupArgs = null)
    {
        _showcase = showcase;
        Text = "Liss Gomoku · 五子棋 v3";
        ClientSize = new Size(1280, 840);
        MinimumSize = new Size(1060, 790);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background; ForeColor = Theme.Text;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        KeyPreview = true;
        _newGame = Theme.Button("新对局", (_, _) => NewGame(), true);
        _undo = Theme.Button("悔棋", (_, _) => UndoMove());
        _hint = Theme.Button("提示", async (_, _) => await Think(true));
        _pause = Theme.Button("暂停", (_, _) => TogglePause());
        _leave = Theme.Button("离开房间", (_, _) => LeaveRoom());
        BuildLayout();
        BindNetwork();
        _board.PlayRequested += PlayHuman;
        _networkTimer.Tick += (_, _) => { _network.Service(); UpdateInteraction(); };
        _networkTimer.Start();
        _mode.SelectedIndexChanged += (_, _) => { if (!_initializing) { CancelSearch(); _paused = true; _reviewing = false; RefreshGame("模式已更改，点击继续或落子"); } };
        _blackEngine.SelectedIndexChanged += (_, _) => ShowEngineDescription();
        _whiteEngine.SelectedIndexChanged += (_, _) => ShowEngineDescription();
        _history.SelectedIndexChanged += (_, _) => { if (!_updatingHistory && _history.SelectedIndex >= 0) ReviewTo(_history.SelectedIndex + 1); };
        _rooms.DoubleClick += (_, _) => JoinSelectedRoom();
        _initializing = false;
        ShowEngineDescription();
        if (showcase) LoadShowcase(); else RestoreGame();
        ApplyStartupOptions(startupArgs ?? []);
        RefreshGame(showcase ? "真实中盘局面 · 可自由继续落子与试用引擎" : "准备就绪 · 点击棋盘落子");
        Shown += async (_, _) => { if ((startupArgs ?? []).Contains("--hint")) await Think(true); else ScheduleComputer(); };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(22, 12, 22, 8), Margin = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(root);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 570));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); header.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        header.Controls.Add(Theme.Label("GOMOKU", 25, true), 0, 0);
        header.Controls.Add(Theme.Label("LISS  /  五子棋与搜索实验  /  v3.0", 9), 0, 1);
        var open = Theme.Button("打开", (_, _) => OpenGame());
        var save = Theme.Button("保存", (_, _) => SaveGame());
        var actions = Theme.Buttons(_newGame, _undo, _hint, _pause, open, save);
        foreach (Control button in actions.Controls) button.Width = 84;
        header.Controls.Add(actions, 1, 0); header.SetRowSpan(actions, 2);
        actions.Padding = new Padding(0, 20, 0, 0);
        root.Controls.Add(header, 0, 0);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
        body.Controls.Add(_board, 0, 0);
        var side = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Panel, Margin = new Padding(18, 0, 0, 0) };
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 102)); side.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var current = Theme.Stack(); current.Padding = new Padding(18, 10, 18, 6);
        Theme.AddRow(current, _turn, 42); Theme.AddRow(current, _position, 34);
        side.Controls.Add(current, 0, 0);
        var nav = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(14, 0, 10, 4), Margin = Padding.Empty };
        for (int i = 0; i < 3; i++)
        {
            int index = i; nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            _tabs[i] = Theme.Button(new[] { "对局", "引擎", "联机" }[i], (_, _) => SelectPage(index));
            _tabs[i].Dock = DockStyle.Fill; nav.Controls.Add(_tabs[i], i, 0);
        }
        side.Controls.Add(nav, 0, 1);
        var pageHost = new Panel { Dock = DockStyle.Fill };
        foreach (var page in _pages) { page.Dock = DockStyle.Fill; page.AutoScroll = true; page.BackColor = Theme.Panel; pageHost.Controls.Add(page); }
        side.Controls.Add(pageHost, 0, 2);
        body.Controls.Add(side, 1, 0); root.Controls.Add(body, 0, 1);
        _footer.Font = new Font("Microsoft YaHei UI", 9);
        root.Controls.Add(_footer, 0, 2);
        BuildGamePage(); BuildEnginePage(); BuildNetworkPage(); SelectPage(0);
        _tips.SetToolTip(_undo, "Ctrl+Z · 人机对局退回到你的上一回合；联机需对方同意。");
        _tips.SetToolTip(_hint, "由当前行棋方的引擎计算建议，不自动落子。");
    }

    private void BuildGamePage()
    {
        var stack = Theme.Stack(); _pages[0].Controls.Add(stack);
        _mode.Items.AddRange(["人机 · 我执黑", "人机 · 我执白", "本地双人", "引擎自对弈"]); _mode.SelectedIndex = 0;
        _size.Items.AddRange(["15 × 15", "19 × 19"]); _size.SelectedIndex = 0;
        AddField(stack, "对局方式", _mode); AddField(stack, "新局棋盘", _size);
        var numbers = new CheckBox { Text = "显示手数", Dock = DockStyle.Fill, ForeColor = Theme.Text, Checked = false };
        numbers.CheckedChanged += (_, _) => { _board.ShowNumbers = numbers.Checked; _board.Invalidate(); };
        Theme.AddRow(stack, numbers, 34);
        Theme.AddRow(stack, Theme.Label("棋谱回看", 10, true), 32);
        var first = Theme.Button("开局", (_, _) => ReviewTo(0));
        var previous = Theme.Button("上手", (_, _) => ReviewTo(_game.Cursor - 1));
        var next = Theme.Button("下手", (_, _) => ReviewTo(_game.Cursor + 1));
        var latest = Theme.Button("当前", (_, _) => ReviewTo(_game.Moves.Count));
        foreach (var b in new[] { first, previous, next, latest }) b.Width = 62;
        Theme.AddRow(stack, Theme.Buttons(first, previous, next, latest), 46);
        StyleList(_history); Theme.AddRow(stack, _history, 166);
        Theme.AddRow(stack, Theme.Label("回看后落子会产生新主线。\nJSON 保留回看位置，SGF 可交换主线棋谱。", 9), 56);
        Theme.AddRow(stack, Theme.Label("Ctrl+N 新局 · Ctrl+Z 悔棋 · Ctrl+S 保存", 8.5f), 30);
    }

    private void BuildEnginePage()
    {
        var stack = Theme.Stack(); _pages[1].Controls.Add(stack);
        foreach (var combo in new[] { _blackEngine, _whiteEngine })
        {
            combo.DisplayMember = nameof(EngineDescriptor.Name); combo.ValueMember = nameof(EngineDescriptor.Id);
            foreach (var engine in EngineCatalog.All) combo.Items.Add(engine);
            combo.SelectedIndex = 0;
        }
        AddField(stack, "黑方引擎", _blackEngine); AddField(stack, "白方引擎", _whiteEngine);
        _evaluation.Items.AddRange(["棋型评估", "五元组评估"]); _evaluation.SelectedIndex = 0;
        AddField(stack, "旧引擎评估函数", _evaluation);
        var parameters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        parameters.Controls.Add(Theme.Label("最大深度"), 0, 0); parameters.Controls.Add(Theme.Label("时间预算 / ms"), 1, 0);
        parameters.Controls.Add(_depth, 0, 1); parameters.Controls.Add(_budget, 1, 1);
        Theme.AddRow(stack, parameters, 64);
        Theme.AddRow(stack, _engineDescription, 60);
        Theme.AddRow(stack, Theme.Label("最近一次搜索", 10, true), 34);
        _diagnostics.ForeColor = Theme.Accent; _diagnostics.Font = new Font("Microsoft YaHei UI", 10);
        Theme.AddRow(stack, _diagnostics, 90);
        Theme.AddRow(stack, Theme.Label("11 种可选引擎 · 独立限时与取消\n深度为完整完成的层数；单层五元组不使用深度。\n时间预算会受机器、局面和候选点数量影响。", 8.5f), 66);
    }

    private void BuildNetworkPage()
    {
        var stack = Theme.Stack(); _pages[2].Controls.Add(stack);
        AddField(stack, "昵称", _nickname);
        var connect = Theme.Button("连接大厅", (_, _) => NetworkAction(() => _network.Connect(_nickname.Text)), true); connect.Width = 130;
        _leave.Width = 130;
        Theme.AddRow(stack, Theme.Buttons(connect, _leave), 46);
        _connection.ForeColor = Theme.Accent; Theme.AddRow(stack, _connection, 54);
        AddField(stack, "新房间名称", _roomName);
        var create = Theme.Button("创建房间", (_, _) => NetworkAction(() => { CancelSearch(); _paused = true; _network.CreateRoom(_roomName.Text, _size.SelectedIndex == 1 ? 19 : 15); })); create.Width = 130;
        var join = Theme.Button("加入所选", (_, _) => JoinSelectedRoom()); join.Width = 130;
        Theme.AddRow(stack, Theme.Buttons(create, join), 46);
        StyleList(_rooms); _rooms.DisplayMember = nameof(RoomListing.Name); Theme.AddRow(stack, _rooms, 160);
        Theme.AddRow(stack, Theme.Label("Photon · Asia · 双人实时对局\n房主执黑；悔棋退两手，重开与悔棋均需同意。\nv3 使用独立房间协议，请双方使用同一版本。", 8.5f), 76);
    }

    private static void StyleList(ListBox list)
    {
        list.Dock = DockStyle.Fill; list.BackColor = Theme.Field; list.ForeColor = Theme.Text;
        list.BorderStyle = BorderStyle.FixedSingle; list.IntegralHeight = false;
        list.Font = new Font("Microsoft YaHei UI", 9.5f); list.ItemHeight = 24;
    }
    private static void AddField(TableLayoutPanel stack, string title, Control field)
    { Theme.AddRow(stack, Theme.Label(title), 28); Theme.AddRow(stack, field, 37); }
    private void SelectPage(int index)
    {
        for (int i = 0; i < 3; i++) { _pages[i].Visible = i == index; _tabs[i].BackColor = i == index ? Theme.Accent : Theme.Panel; _tabs[i].ForeColor = i == index ? Theme.Background : Theme.Text; }
    }
    private void ShowEngineDescription()
    {
        if (_blackEngine.SelectedItem is EngineDescriptor black && _whiteEngine.SelectedItem is EngineDescriptor white)
            _engineDescription.Text = (_game.BlackToMove ? black : white).Description;
    }

    private bool HumanTurn => _online ? _network.IsLocalBlack == _game.BlackToMove : _mode.SelectedIndex switch
    { 0 => _game.BlackToMove, 1 => !_game.BlackToMove, 2 => true, _ => false };

    private void PlayHuman(int x, int y)
    {
        if (_closing || _thinking || _game.IsFinished || !HumanTurn || (_online && _network.HasPendingRequest)) return;
        if (!_game.TryPlay(x, y)) return;
        _reviewing = false; _paused = false; _board.HintMove = null;
        if (_online && !_network.SendMove(x, y, _game.Cursor))
        {
            _game.Undo(); LeaveRoom(); RefreshGame("落子未能发送，已回退并离开房间；当前棋谱可保存"); return;
        }
        RefreshGame(); Persist(); ScheduleComputer();
    }
    private void ScheduleComputer()
    {
        if (!_closing && IsHandleCreated && !_paused && !_reviewing && !_online && !_thinking && !_game.IsFinished && !HumanTurn)
            BeginInvoke(async () => await Think(false));
    }
    private async Task Think(bool hintOnly)
    {
        if (_closing || _online || _thinking || _game.IsFinished || (!hintOnly && (_paused || _reviewing || HumanTurn))) return;
        CancelSearch();
        int generation = _generation; int cursor = _game.Cursor;
        var snapshot = _game.Clone();
        var engine = (EngineDescriptor)(_game.BlackToMove ? _blackEngine.SelectedItem! : _whiteEngine.SelectedItem!);
        var options = new SearchOptions(engine.Id, (int)_depth.Value, (int)_budget.Value, _evaluation.SelectedIndex == 1 ? "quintuple" : "patterns");
        var cts = new CancellationTokenSource(); _search = cts; _thinking = true;
        RefreshGame((hintOnly ? "正在计算提示 · " : "正在思考 · ") + engine.Name);
        try
        {
            var result = await Task.Run(() => EngineCatalog.Search(snapshot, options, cts.Token));
            if (_closing || generation != _generation || cts.IsCancellationRequested || _game.Cursor != cursor) return;
            _diagnostics.Text = $"{engine.Name}\n深度 {result.Depth}    /    {result.ElapsedMs:N0} ms\n节点 {result.Nodes:N0}    /    命中 {result.TableHits:N0}\n{result.Detail}";
            _thinking = false;
            if (result.Move is { } move && (uint)move.X < _game.Size && (uint)move.Y < _game.Size && _game.Cells[move.X, move.Y] == Stone.空)
            {
                if (hintOnly) { _board.HintMove = move; _paused = true; RefreshGame($"建议落点 {(char)('A' + move.X)}{move.Y + 1} · {engine.Name}"); }
                else if (_game.TryPlay(move.X, move.Y)) { _board.HintMove = null; RefreshGame(); Persist(); }
            }
            else { _paused = true; RefreshGame("引擎未返回有效落点，请切换引擎或继续手动对局"); }
        }
        catch (OperationCanceledException) { if (generation == _generation) RefreshGame("搜索已取消"); }
        catch (Exception error) { SessionFiles.Log(error); if (!_closing && generation == _generation) { _paused = true; RefreshGame("搜索未完成，可切换引擎继续"); } }
        finally
        {
            if (generation == _generation) { _thinking = false; _search = null; UpdateInteraction(); ScheduleComputer(); }
            cts.Dispose();
        }
    }
    private void CancelSearch()
    { _generation++; _search?.Cancel(); _search = null; _thinking = false; }
    private void TogglePause()
    {
        if (_online) return;
        if (_thinking || !_paused) { CancelSearch(); _paused = true; RefreshGame("已暂停 · 可以回看棋谱或调整引擎"); }
        else { _paused = false; _reviewing = false; RefreshGame(); ScheduleComputer(); }
    }
    private void NewGame()
    {
        if (_online) { NetworkAction(_network.RequestRestart); return; }
        if (_network.InRoom) { RefreshGame("请先离开房间再开始本地对局"); return; }
        CancelSearch(); _game = new GameBoard(_size.SelectedIndex == 1 ? 19 : 15);
        _board.Board = _game; _paused = false; _reviewing = false;
        _diagnostics.Text = "尚未搜索\n选择引擎，开始一局或请求提示。";
        RefreshGame(); Persist(); ScheduleComputer();
    }
    private void UndoMove()
    {
        if (_online) { if (_game.Cursor > 0) NetworkAction(_network.RequestUndo); return; }
        CancelSearch();
        int count = _mode.SelectedIndex is 0 or 1 && HumanTurn && _game.Cursor >= 2 ? 2 : 1;
        _game.Undo(count); _paused = true; _reviewing = false; _board.HintMove = null;
        RefreshGame("已悔棋 · 可落子或点击继续"); Persist();
    }
    private void ReviewTo(int cursor)
    {
        if (_online || _network.InRoom) { RefreshGame("联机过程中暂不回看，保存棋谱后可离线查看"); return; }
        CancelSearch(); cursor = Math.Clamp(cursor, 0, _game.Moves.Count);
        if (cursor < _game.Cursor) _game.Undo(_game.Cursor - cursor);
        else while (_game.Cursor < cursor && _game.Redo()) { }
        _paused = true; _reviewing = _game.Cursor < _game.Moves.Count; _board.HintMove = null;
        RefreshGame(_reviewing ? "棋谱回看 · 落子将建立新主线" : "已回到最新局面 · 点击继续或落子");
    }

    private void RefreshGame(string? message = null)
    {
        _board.Board = _game;
        _turn.Text = _game.IsDraw ? "和棋" : _game.Winner != Stone.空 ? (_game.Winner == Stone.黑子 ? "黑方获胜" : "白方获胜") : (_game.BlackToMove ? "黑方行棋" : "白方行棋");
        _turn.ForeColor = _game.IsFinished ? Theme.Accent : Theme.Text;
        _position.Text = $"{_game.Size} × {_game.Size}    ·    第 {_game.Cursor + (_game.IsFinished ? 0 : 1)} 手" + (_online ? "    ·    联机" : _reviewing ? "    ·    回看" : "    ·    自由规则");
        _updatingHistory = true; _history.BeginUpdate(); _history.Items.Clear();
        for (int i = 0; i < _game.Moves.Count; i++)
        {
            var m = _game.Moves[i]; _history.Items.Add($"{i + 1,3}    {(i % 2 == 0 ? "● 黑" : "○ 白")}    {(char)('A' + m.X)}{m.Y + 1}" + (i >= _game.Cursor ? "  · 待回放" : ""));
        }
        _history.SelectedIndex = _game.Cursor - 1; _history.EndUpdate(); _updatingHistory = false;
        if (message is not null) _footer.Text = message;
        else _footer.Text = _game.IsFinished ? "本局结束 · 可回看棋谱、保存或开始新局" : _online ? $"房间 {_network.RoomName} · 你执{(_network.IsLocalBlack ? "黑" : "白")} · 对手 {_network.OpponentName}" : _paused ? "已暂停 · 可调整参数，点击继续" : "方向键与 Enter 也可落子 · Ctrl+Z 悔棋 · Ctrl+S 保存";
        ShowEngineDescription(); UpdateInteraction();
    }
    private void UpdateInteraction()
    {
        bool pending = _online && _network.HasPendingRequest;
        _board.InputEnabled = !_thinking && !_game.IsFinished && HumanTurn && !pending && (!_network.InRoom || _online);
        _newGame.Enabled = !pending; _undo.Enabled = _game.Cursor > 0 && !pending;
        _hint.Enabled = !_online && !_network.InRoom && !_thinking && !_game.IsFinished;
        _pause.Enabled = !_online && !_network.InRoom; _pause.Text = _paused ? "继续" : _thinking ? "停止" : "暂停";
        _mode.Enabled = !_online && !_network.InRoom; _size.Enabled = !_network.InRoom;
        _leave.Enabled = _network.InRoom;
    }
    private void OpenGame()
    {
        if (_network.InRoom) { RefreshGame("请先离开房间再打开棋谱"); return; }
        using var dialog = new OpenFileDialog { Title = "打开五子棋棋谱", Filter = "五子棋棋谱 (*.json;*.sgf)|*.json;*.sgf", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { var saved = SessionFiles.Load(dialog.FileName); CancelSearch(); _game = saved.Board; _mode.SelectedIndex = saved.Mode; _paused = true; _reviewing = _game.Cursor < _game.Moves.Count; RefreshGame("棋谱已打开 · 点击继续或自由回看"); Persist(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) { MessageBox.Show(this, e.Message, "无法打开棋谱", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private void SaveGame()
    {
        using var dialog = new SaveFileDialog { Title = "保存棋谱", Filter = "完整棋谱 (*.json)|*.json|SGF 主线棋谱 (*.sgf)|*.sgf", FileName = "gomoku-" + DateTime.Now.ToString("yyyyMMdd-HHmm"), AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { if (Path.GetExtension(dialog.FileName).Equals(".sgf", StringComparison.OrdinalIgnoreCase)) SessionFiles.SaveSgf(dialog.FileName, _game); else SessionFiles.Save(dialog.FileName, _game, _online ? 2 : _mode.SelectedIndex); RefreshGame("棋谱已保存"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { MessageBox.Show(this, e.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private void Persist()
    {
        if (_showcase) return;
        try { SessionFiles.Save(SessionFiles.AutoSavePath, _game, _online ? 2 : _mode.SelectedIndex); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { SessionFiles.Log(e); _footer.Text = "自动保存失败；可使用保存按钮另存棋谱"; }
    }
    private void RestoreGame()
    {
        try
        {
            if (!File.Exists(SessionFiles.AutoSavePath)) return;
            var saved = SessionFiles.Load(SessionFiles.AutoSavePath); _game = saved.Board; _mode.SelectedIndex = saved.Mode;
            _paused = true; _reviewing = _game.Cursor < _game.Moves.Count;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) { SessionFiles.Log(e); }
    }
    private void LoadShowcase()
    {
        _mode.SelectedIndex = 2;
        foreach (var (x, y) in new[] { (7, 7), (8, 7), (7, 8), (7, 6), (6, 8), (8, 8), (5, 8), (10, 8), (6, 7), (6, 6), (9, 6), (9, 7), (4, 8), (3, 8), (8, 5), (9, 5), (5, 7), (4, 5) }) _game.TryPlay(x, y);
        _paused = true;
    }

    private void ApplyStartupOptions(string[] args)
    {
        string? Value(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        foreach (var (name, combo) in new[] { ("--black-engine", _blackEngine), ("--white-engine", _whiteEngine) })
        {
            string? id = Value(name) ?? Value("--engine");
            for (int i = 0; i < combo.Items.Count; i++) if (combo.Items[i] is EngineDescriptor item && item.Id == id) combo.SelectedIndex = i;
        }
        if (int.TryParse(Value("--depth"), out int depth)) _depth.Value = Math.Clamp(depth, 1, 8);
        if (int.TryParse(Value("--budget"), out int budget)) _budget.Value = Math.Clamp(budget, 250, 10000);
        if (Value("--size")?.Split('x') is [string w, string h] && int.TryParse(w, out int width) && int.TryParse(h, out int height)) ClientSize = new Size(Math.Clamp(width, 1060, 2400), Math.Clamp(height, 790, 1600));
        if (args.Contains("--autoplay")) { _mode.SelectedIndex = 3; _paused = false; }
        SelectPage(Value("--page") switch { "engines" => 1, "online" => 2, _ => 0 });
    }

    private void BindNetwork()
    {
        _network.StatusChanged += status => { _connection.Text = status; _footer.Text = status; };
        _network.RoomsChanged += rooms =>
        {
            string? selected = (_rooms.SelectedItem as RoomListing)?.Name;
            _rooms.BeginUpdate(); _rooms.Items.Clear(); foreach (var room in rooms) _rooms.Items.Add(room);
            for (int i = 0; i < _rooms.Items.Count; i++) if (((RoomListing)_rooms.Items[i]).Name == selected) _rooms.SelectedIndex = i;
            _rooms.EndUpdate();
        };
        _network.MatchStarted += match => { CancelSearch(); _online = true; _paused = true; _reviewing = false; _game = new GameBoard(match.BoardSize); RefreshGame(); };
        _network.MoveReceived += move =>
        {
            if (!_online || _game.BlackToMove == _network.IsLocalBlack || move.Ply != _game.Cursor + 1 || !_game.TryPlay(move.X, move.Y))
            { LeaveRoom(); RefreshGame("收到不一致的棋局，已离开房间；可保存当前棋谱"); return; }
            _board.HintMove = null; RefreshGame(); Persist();
        };
        _network.RequestReceived += kind =>
        {
            string? requestId = _network.PendingIncomingRequestId;
            BeginInvoke(() =>
            {
                if (_closing || !_online || requestId is null || requestId != _network.PendingIncomingRequestId) return;
                bool accept = MessageBox.Show(this, kind == "restart" ? "对手请求重新开始当前对局。是否同意？" : "对手请求退回两手。是否同意？", "联机请求", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                NetworkAction(() => _network.ReplyRequest(accept, requestId));
            });
        };
        _network.RequestResolved += (kind, accepted) =>
        {
            if (!_online) return;
            if (accepted) { if (kind == "restart") _game.Reset(); else _game.Undo(2); _board.HintMove = null; RefreshGame(); Persist(); }
            else RefreshGame("请求未获同意或已超时，棋局保持原样");
        };
        _network.OpponentLeft += () => { _online = false; _paused = true; RefreshGame("对手已离开 · 可保存棋谱；离开房间后可继续本地对局"); };
    }
    private void NetworkAction(Action action)
    {
        try { action(); UpdateInteraction(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { RefreshGame(e.Message); }
    }
    private void JoinSelectedRoom()
    {
        if (_rooms.SelectedItem is not RoomListing room) { RefreshGame("请先在列表中选择房间"); return; }
        CancelSearch(); _paused = true; NetworkAction(() => _network.JoinRoom(room.Name));
    }
    private void LeaveRoom()
    { _online = false; _paused = true; CancelSearch(); NetworkAction(_network.LeaveRoom); RefreshGame("已离开联机对局 · 棋谱保留为本地局面"); }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.N: if (_newGame.Enabled) NewGame(); return true;
            case Keys.Control | Keys.Z: if (_undo.Enabled) UndoMove(); return true;
            case Keys.Control | Keys.S: SaveGame(); return true;
            case Keys.Control | Keys.O: OpenGame(); return true;
            case Keys.Escape: CancelSearch(); _paused = true; RefreshGame("已暂停"); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true; CancelSearch(); Persist(); _networkTimer.Stop(); _networkTimer.Dispose(); _network.Dispose(); _tips.Dispose();
        base.OnFormClosing(e);
    }
}
