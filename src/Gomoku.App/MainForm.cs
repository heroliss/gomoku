using Liss.Gomoku.Core;
using Liss.Gomoku.Networking;
using 五子棋;

namespace Liss.Gomoku.App;

internal sealed class MainForm : Form
{
    private GameBoard _game = new();
    private MatchAnalysis _analysis = new();
    private readonly BoardControl _board = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label _turn = Theme.Label("黑方先行", 18, true);
    private readonly Label _position = Theme.Label("自由规则 · 连成五子或以上获胜");
    private readonly Label _footer = Theme.Label("准备就绪");
    private readonly Label _diagnostics = Theme.Label("尚未搜索\n选择引擎，开始一局或请求提示。", 10);
    private readonly Label _engineDescription = Theme.Label("");
    private readonly NetworkLobbyControl _lobby = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _mode = Theme.Combo(), _size = Theme.Combo();
    private readonly EngineSettingsControl _blackSettings = new("黑方"), _whiteSettings = new("白方");
    private readonly SearchStatisticsControl _statistics = new();
    private readonly Button[] _sideTabs = new Button[2];
    private readonly ListBox _history = new();
    private readonly Panel[] _pages = [new(), new(), new(), new()];
    private readonly Button[] _tabs = new Button[4];
    private readonly Button _newGame, _undo, _hint, _pause, _openGame;
    private readonly PhotonLobbyService _network = new();
    private readonly System.Windows.Forms.Timer _networkTimer = new() { Interval = 30 };
    private CancellationTokenSource? _search;
    private int _generation;
    private int? _autoplayStopAt;
    private bool _thinking, _paused, _closing, _online, _reviewing, _updatingHistory, _initializing = true;
    private bool _networkVisited, _networkBoard, _networkTransitionOwnsBoard;
    private readonly bool _showcase;
    private readonly ToolTip _tips = new() { AutoPopDelay = 12000, InitialDelay = 350 };

    internal MainForm(bool showcase, string[]? startupArgs = null)
    {
        _showcase = showcase;
        Text = "Liss Gomoku · 五子棋 v3.2";
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
        _openGame = Theme.Button("打开", (_, _) => OpenGame());
        BuildLayout();
        BindNetwork();
        _board.PlayRequested += PlayHuman;
        _networkTimer.Tick += (_, _) => { _network.Service(); UpdateInteraction(); };
        _networkTimer.Start();
        _mode.SelectedIndexChanged += (_, _) => { if (!_initializing) { CancelSearch(); _paused = true; _reviewing = false; RefreshGame("模式已更改，点击继续或落子"); } };
        _blackSettings.ProfileChanged += ProfileChanged;
        _whiteSettings.ProfileChanged += ProfileChanged;
        _history.SelectedIndexChanged += (_, _) => { if (!_updatingHistory && _history.SelectedIndex >= 0) ReviewTo(_history.SelectedIndex + 1); };
        if (!showcase)
        {
            var preferences = EnginePreferencesStore.Load(PreferencesPath);
            _blackSettings.Profile = preferences.Black;
            _whiteSettings.Profile = preferences.White;
        }
        if (showcase) LoadShowcase(); else RestoreGame();
        ApplyStartupOptions(startupArgs ?? []);
        _initializing = false;
        RefreshGame(showcase ? "真实中盘局面 · 可自由继续落子与试用引擎" : "准备就绪 · 点击棋盘落子");
        Shown += async (_, _) => { if (_pages[3].Visible) EnterLobbyOnce(); if ((startupArgs ?? []).Contains("--hint")) await Think(true); else ScheduleComputer(); };
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
        header.Controls.Add(Theme.Label("LISS  /  五子棋与搜索实验  /  v3.2", 9), 0, 1);
        var save = Theme.Button("保存", (_, _) => SaveGame());
        var actions = Theme.Buttons(_newGame, _undo, _hint, _pause, _openGame, save);
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
        var nav = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(14, 0, 10, 4), Margin = Padding.Empty };
        for (int i = 0; i < _tabs.Length; i++)
        {
            int index = i; nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            _tabs[i] = Theme.Button(new[] { "对局", "引擎", "统计", "联机" }[i], (_, _) => SelectPage(index));
            _tabs[i].Dock = DockStyle.Fill; nav.Controls.Add(_tabs[i], i, 0);
        }
        side.Controls.Add(nav, 0, 1);
        var pageHost = new Panel { Dock = DockStyle.Fill };
        foreach (var page in _pages) { page.Dock = DockStyle.Fill; page.AutoScroll = true; page.BackColor = Theme.Panel; pageHost.Controls.Add(page); }
        side.Controls.Add(pageHost, 0, 2);
        body.Controls.Add(side, 1, 0); root.Controls.Add(body, 0, 1);
        _footer.Font = new Font("Microsoft YaHei UI", 9);
        root.Controls.Add(_footer, 0, 2);
        BuildGamePage(); BuildEnginePage(); BuildStatisticsPage(); BuildNetworkPage(); SelectPage(0);
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
        var sides = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        for (int i = 0; i < 2; i++)
        {
            int index = i; sides.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _sideTabs[i] = Theme.Button(i == 0 ? "黑方设置" : "白方设置", (_, _) => SelectEngineSide(index));
            _sideTabs[i].Dock = DockStyle.Fill; sides.Controls.Add(_sideTabs[i], i, 0);
        }
        Theme.AddRow(stack, sides, 42);
        var editor = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        foreach (var settings in new[] { _blackSettings, _whiteSettings }) { settings.Dock = DockStyle.Fill; editor.Controls.Add(settings); }
        Theme.AddRow(stack, editor, 266); SelectEngineSide(0);
        Theme.AddRow(stack, Theme.Label("双方独立配置", 10, true), 34);
        Theme.AddRow(stack, Theme.Label("引擎、深度、每步预算与评估函数分别保存。\n思考中修改参数，将在下一次搜索生效。\n人机对局的提示使用当前行棋方设置。", 9), 76);
        Theme.AddRow(stack, Theme.Label("后台线程计算，界面可继续响应。\n单次搜索为单线程；可随时暂停或取消。\n实测耗时、节点与缓存命中见「统计」。", 8.5f), 72);
    }

    private void SelectEngineSide(int index)
    {
        _blackSettings.Visible = index == 0; _whiteSettings.Visible = index == 1;
        for (int i = 0; i < _sideTabs.Length; i++)
        {
            _sideTabs[i].BackColor = i == index ? Theme.Accent : Theme.Panel;
            _sideTabs[i].ForeColor = i == index ? Theme.Background : Theme.Text;
            _sideTabs[i].FlatAppearance.MouseOverBackColor = i == index ? Color.FromArgb(150, 235, 219) : Color.FromArgb(36, 53, 72);
            _sideTabs[i].FlatAppearance.MouseDownBackColor = _sideTabs[i].FlatAppearance.MouseOverBackColor;
        }
    }

    private void BuildStatisticsPage()
    {
        var stack = Theme.Stack(); _pages[2].Controls.Add(stack);
        Theme.AddRow(stack, Theme.Label("AI 思考统计", 12, true), 34);
        Theme.AddRow(stack, _engineDescription, 52);
        _statistics.Dock = DockStyle.Fill; Theme.AddRow(stack, _statistics, 196);
        Theme.AddRow(stack, Theme.Label("仅计本局当前棋谱中已落子的 AI 决策。\n提示、取消、手动与联机落子不计入。\n回看随手数更新；新局或读档后重新统计。", 8.5f), 70);
        Theme.AddRow(stack, Theme.Label("最近一次搜索", 10, true), 30);
        _diagnostics.ForeColor = Theme.Accent; _diagnostics.Font = new Font("Microsoft YaHei UI", 10);
        Theme.AddRow(stack, _diagnostics, 100);
    }

    private void BuildNetworkPage()
    {
        _pages[3].Controls.Add(_lobby);
        _lobby.ConnectRequested += (_, _) => NetworkAction(() => _network.Connect(_lobby.Nickname));
        _lobby.DisconnectRequested += (_, _) => NetworkAction(_network.Disconnect);
        _lobby.CancelRequested += (_, _) => NetworkAction(_network.CancelPendingOperation);
        _lobby.CreateRequested += (_, _) => NetworkAction(() => _network.ConnectAndCreateRoom(_lobby.Nickname, _lobby.NewRoomName, _size.SelectedIndex == 1 ? 19 : 15));
        _lobby.JoinRequested += (_, _) => JoinSelectedRoom();
        _lobby.LeaveRequested += (_, _) => LeaveRoom();
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
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == index;
            _tabs[i].BackColor = i == index ? Theme.Accent : Theme.Panel;
            _tabs[i].ForeColor = i == index ? Theme.Background : Theme.Text;
            _tabs[i].FlatAppearance.MouseOverBackColor = i == index ? Color.FromArgb(150, 235, 219) : Color.FromArgb(36, 53, 72);
            _tabs[i].FlatAppearance.MouseDownBackColor = _tabs[i].FlatAppearance.MouseOverBackColor;
        }
        if (index == 3 && !_initializing) EnterLobbyOnce();
    }

    private void EnterLobbyOnce()
    {
        if (_networkVisited || _closing) return;
        _networkVisited = true;
        if (_network.Snapshot.CanConnect) NetworkAction(() => _network.Connect(_lobby.Nickname));
    }

    private bool NetworkOwnsBoard => _network.Snapshot.PendingOperation != LobbyPendingOperation.None
        || _network.Snapshot.Phase is LobbyPhase.CreatingRoom or LobbyPhase.JoiningRoom or LobbyPhase.RoomWaiting
            or LobbyPhase.Match or LobbyPhase.LeavingRoom
        || _network.Snapshot.Phase == LobbyPhase.Disconnecting && _networkTransitionOwnsBoard;
    private void ShowEngineDescription()
    {
        static string Describe(string side, SearchProfile profile) =>
            $"{side} · 深度上限 {profile.Depth} 层 · 预算 {profile.TimeBudgetMs:N0} ms";
        _engineDescription.Text = Describe("黑", _blackSettings.Profile) + "\n" + Describe("白", _whiteSettings.Profile);
    }

    private static string PreferencesPath => Path.Combine(SessionFiles.DataDirectory, "engine-preferences.json");
    private void ProfileChanged(object? sender, EventArgs e) { ShowEngineDescription(); PersistPreferences(); }
    private void PersistPreferences()
    {
        if (_showcase || _initializing) return;
        try { EnginePreferencesStore.Save(PreferencesPath, new(_blackSettings.Profile, _whiteSettings.Profile)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { SessionFiles.Log(e); _footer.Text = "引擎设置保存失败，当前设置仍可使用"; }
    }

    private bool HumanTurn => _online ? _network.IsLocalBlack == _game.BlackToMove : _mode.SelectedIndex switch
    { 0 => _game.BlackToMove, 1 => !_game.BlackToMove, 2 => true, _ => false };

    private void PlayHuman(int x, int y)
    {
        if (_closing || _thinking || _game.IsFinished || !HumanTurn || NetworkOwnsBoard && !_online || (_online && _network.HasPendingRequest)) return;
        if (!_game.TryPlay(x, y)) return;
        // Replaying a former AI coordinate by hand is a new human decision, not a Redo.
        _analysis.DiscardFrom(_game.Cursor);
        _reviewing = false; _paused = false; _board.HintMove = null;
        if (_online && !_network.SendMove(x, y, _game.Cursor))
        {
            _game.Undo(); LeaveRoom(); RefreshGame("落子未能发送，已回退并离开房间；当前棋谱可保存"); return;
        }
        RefreshGame(); Persist(); ScheduleComputer();
    }
    private void ScheduleComputer()
    {
        if (!_closing && IsHandleCreated && !_paused && !_reviewing && !NetworkOwnsBoard && !_online && !_thinking && !_game.IsFinished && !HumanTurn)
            BeginInvoke(async () => await Think(false));
    }
    private async Task Think(bool hintOnly)
    {
        if (_closing || NetworkOwnsBoard || _online || _thinking || _game.IsFinished || (!hintOnly && (_paused || _reviewing || HumanTurn))) return;
        CancelSearch();
        int generation = _generation; int cursor = _game.Cursor;
        var snapshot = _game.Clone();
        bool black = _game.BlackToMove;
        var options = (black ? _blackSettings.Profile : _whiteSettings.Profile).ToOptions();
        var engine = EngineCatalog.All.First(e => e.Id == options.EngineId);
        var cts = new CancellationTokenSource(); _search = cts; _thinking = true;
        RefreshGame((hintOnly ? "正在计算提示 · " : "正在思考 · ") + engine.Name);
        try
        {
            var result = await Task.Run(() => EngineCatalog.Search(snapshot, options, cts.Token));
            if (_closing || generation != _generation || cts.IsCancellationRequested || _game.Cursor != cursor) return;
            _diagnostics.Text = $"{(black ? "黑方" : "白方")}{(hintOnly ? "提示" : "落子")} · {engine.Name}\n深度 {result.Depth}    /    {result.ElapsedMs:N0} ms\n节点 {result.Nodes:N0}    /    命中 {result.TableHits:N0}\n{result.Detail}";
            _thinking = false;
            if (result.Move is { } move && (uint)move.X < _game.Size && (uint)move.Y < _game.Size && _game.Cells[move.X, move.Y] == Stone.空)
            {
                if (hintOnly) { _board.HintMove = move; _paused = true; RefreshGame($"建议落点 {(char)('A' + move.X)}{move.Y + 1} · {engine.Name}"); }
                else if (_game.TryPlay(move.X, move.Y))
                {
                    _analysis.Record(_game, result, options.EngineId);
                    _board.HintMove = null;
                    if (_autoplayStopAt is int stopAt && _game.Cursor >= stopAt) _paused = true;
                    RefreshGame(); Persist();
                }
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
        if (_online || NetworkOwnsBoard) return;
        if (_thinking || !_paused) { CancelSearch(); _paused = true; RefreshGame("已暂停 · 可以回看棋谱或调整引擎"); }
        else { _paused = false; _reviewing = false; RefreshGame(); ScheduleComputer(); }
    }
    private void NewGame()
    {
        if (_online) { NetworkAction(_network.RequestRestart); return; }
        if (NetworkOwnsBoard) { RefreshGame("请先返回大厅，或取消当前联机操作，再开始本地对局"); return; }
        CancelSearch(); _game = new GameBoard(_size.SelectedIndex == 1 ? 19 : 15); _analysis = new();
        _autoplayStopAt = null; _networkBoard = false;
        _board.Board = _game; _paused = false; _reviewing = false;
        _diagnostics.Text = "尚未搜索\n选择引擎，开始一局或请求提示。";
        RefreshGame(); Persist(); ScheduleComputer();
    }
    private void UndoMove()
    {
        if (_online) { if (_game.Cursor > 0) NetworkAction(_network.RequestUndo); return; }
        if (NetworkOwnsBoard) { RefreshGame(_network.Snapshot.Message); return; }
        CancelSearch();
        int count = _mode.SelectedIndex is 0 or 1 && HumanTurn && _game.Cursor >= 2 ? 2 : 1;
        _game.Undo(count); _paused = true; _reviewing = false; _board.HintMove = null;
        RefreshGame("已悔棋 · 可落子或点击继续"); Persist();
    }
    private void ReviewTo(int cursor)
    {
        if (_online || NetworkOwnsBoard) { RefreshGame("联机房间内暂不回看，保存棋谱后可离线查看"); return; }
        CancelSearch(); cursor = Math.Clamp(cursor, 0, _game.Moves.Count);
        if (cursor < _game.Cursor) _game.Undo(_game.Cursor - cursor);
        else while (_game.Cursor < cursor && _game.Redo()) { }
        _paused = true; _reviewing = _game.Cursor < _game.Moves.Count; _board.HintMove = null;
        RefreshGame(_reviewing ? "棋谱回看 · 落子将建立新主线" : "已回到最新局面 · 点击继续或落子");
    }

    private void RefreshGame(string? message = null)
    {
        _analysis.Reconcile(_game);
        _statistics.SetSummaries(_analysis.GetSummary(_game, true), _analysis.GetSummary(_game, false));
        _board.Board = _game;
        _turn.Text = _game.IsDraw ? "和棋" : _game.Winner != Stone.空 ? (_game.Winner == Stone.黑子 ? "黑方获胜" : "白方获胜") : (_game.BlackToMove ? "黑方行棋" : "白方行棋");
        _turn.ForeColor = _game.IsFinished ? Theme.Accent : Theme.Text;
        _position.Text = $"{_game.Size} × {_game.Size}    ·    第 {_game.Cursor + (_game.IsFinished ? 0 : 1)} 手" + (_online ? "    ·    联机" : _reviewing ? "    ·    回看" : "    ·    自由规则");
        if (NetworkOwnsBoard && !_online)
        {
            _turn.ForeColor = Theme.Text;
            _turn.Text = _network.Snapshot.Phase switch
            {
                LobbyPhase.RoomWaiting => "等待对手",
                LobbyPhase.LeavingRoom => "正在返回大厅",
                LobbyPhase.Disconnecting => "正在断开连接",
                _ => "正在准备联机"
            };
            _position.Text = _network.Snapshot.Phase == LobbyPhase.RoomWaiting ? "联机房间 · 1 / 2 人" : "当前棋谱保留 · 暂不落子";
        }
        _updatingHistory = true; _history.BeginUpdate(); _history.Items.Clear();
        for (int i = 0; i < _game.Moves.Count; i++)
        {
            var m = _game.Moves[i]; _history.Items.Add($"{i + 1,3}    {(i % 2 == 0 ? "● 黑" : "○ 白")}    {(char)('A' + m.X)}{m.Y + 1}" + (i >= _game.Cursor ? "  · 待回放" : ""));
        }
        _history.SelectedIndex = _game.Cursor - 1; _history.EndUpdate(); _updatingHistory = false;
        if (message is not null) _footer.Text = message;
        else _footer.Text = NetworkOwnsBoard && !_online ? _network.Snapshot.Message : _online ? $"房间 {_network.RoomName} · 你执{(_network.IsLocalBlack ? "黑" : "白")} · 对手 {_network.OpponentName}" : _game.IsFinished ? "本局结束 · 可回看棋谱、保存或开始新局" : _paused ? "已暂停 · 可调整参数，点击继续" : "方向键与 Enter 也可落子 · Ctrl+Z 悔棋 · Ctrl+S 保存";
        ShowEngineDescription(); UpdateInteraction();
    }
    private void UpdateInteraction()
    {
        bool pending = _online && _network.HasPendingRequest;
        bool localAllowed = !NetworkOwnsBoard && !_online;
        bool input = !_thinking && !_game.IsFinished && HumanTurn && !pending && (localAllowed || _online);
        if (_board.InputEnabled != input) { _board.InputEnabled = input; _board.Invalidate(); }
        _newGame.Enabled = !pending && (localAllowed || _online); _undo.Enabled = _game.Cursor > 0 && !pending && (localAllowed || _online);
        _newGame.Text = _online ? "请求重开" : "新对局"; _undo.Text = _online ? "请求悔棋" : "悔棋";
        _hint.Enabled = localAllowed && !_thinking && !_game.IsFinished;
        _pause.Enabled = localAllowed; _pause.Text = _paused ? "继续" : _thinking ? "停止" : "暂停";
        _mode.Enabled = localAllowed; _size.Enabled = localAllowed; _openGame.Enabled = localAllowed;
        _board.InteractionNotice = !NetworkOwnsBoard || _online ? null : _network.Snapshot.Phase switch
        {
            LobbyPhase.RoomWaiting => "等待对手加入\n当前棋谱保留，双方就绪后开始新对局",
            LobbyPhase.LeavingRoom => "正在返回大厅\n当前棋谱保留，稍后可继续本地对局",
            LobbyPhase.Disconnecting => "正在断开连接\n当前棋谱保留，请稍候",
            _ => "正在准备联机\n当前棋谱保留，可在联机页取消"
        };
        _lobby.SetState(_network.Snapshot, _network.Snapshot.Phase == LobbyPhase.RoomWaiting ? _network.IsHost : _network.IsLocalBlack,
            _network.OpponentName, pending, _game.IsFinished);
    }
    private void OpenGame()
    {
        if (NetworkOwnsBoard) { RefreshGame("请先返回大厅，或取消当前联机操作，再打开棋谱"); return; }
        using var dialog = new OpenFileDialog { Title = "打开五子棋棋谱", Filter = "五子棋棋谱 (*.json;*.sgf)|*.json;*.sgf", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (NetworkOwnsBoard) { RefreshGame("联机状态已变化，保留当前对局；返回大厅后再打开棋谱"); return; }
        try { var saved = SessionFiles.Load(dialog.FileName); CancelSearch(); _game = saved.Board; _analysis = new(); _networkBoard = false; _mode.SelectedIndex = saved.Mode; _paused = true; _reviewing = _game.Cursor < _game.Moves.Count; _diagnostics.Text = "尚未搜索\n读档后从新的 AI 决策开始统计。"; RefreshGame("棋谱已打开 · 点击继续或自由回看"); Persist(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) { MessageBox.Show(this, e.Message, "无法打开棋谱", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private void SaveGame()
    {
        // File dialogs keep pumping network events. Export the position selected when saving began.
        var snapshot = _game.Clone(); int mode = _online ? 2 : _mode.SelectedIndex;
        using var dialog = new SaveFileDialog { Title = "保存棋谱", Filter = "完整棋谱 (*.json)|*.json|SGF 主线棋谱 (*.sgf)|*.sgf", FileName = "gomoku-" + DateTime.Now.ToString("yyyyMMdd-HHmm"), AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { if (Path.GetExtension(dialog.FileName).Equals(".sgf", StringComparison.OrdinalIgnoreCase)) SessionFiles.SaveSgf(dialog.FileName, snapshot); else SessionFiles.Save(dialog.FileName, snapshot, mode); RefreshGame("已保存打开保存窗口时的棋谱快照"); }
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
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) { SessionFiles.Log(e); }
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
        foreach (var (side, settings) in new[] { ("black", _blackSettings), ("white", _whiteSettings) })
        {
            var profile = settings.Profile;
            string? id = Value($"--{side}-engine") ?? Value("--engine");
            string? evaluation = Value($"--{side}-evaluation") ?? Value("--evaluation");
            profile = profile with
            {
                EngineId = EngineCatalog.All.Any(e => e.Id == id) ? id! : profile.EngineId,
                Depth = int.TryParse(Value($"--{side}-depth") ?? Value("--depth"), out int depth) ? Math.Clamp(depth, 1, 8) : profile.Depth,
                TimeBudgetMs = int.TryParse(Value($"--{side}-budget") ?? Value("--budget"), out int budget) ? Math.Clamp(budget, 250, 10000) : profile.TimeBudgetMs,
                EvaluationId = evaluation is "patterns" or "quintuple" ? evaluation : profile.EvaluationId
            };
            settings.Profile = profile;
        }
        if (Value("--size")?.Split('x') is [string w, string h] && int.TryParse(w, out int width) && int.TryParse(h, out int height)) ClientSize = new Size(Math.Clamp(width, 1060, 2400), Math.Clamp(height, 790, 1600));
        if (_showcase && args.Contains("--empty-showcase")) _game.Reset();
        if (args.Contains("--autoplay")) { _mode.SelectedIndex = 3; _paused = false; }
        if (int.TryParse(Value("--autoplay-plies"), out int plies) && plies > 0) _autoplayStopAt = (int)Math.Min(_game.Size * _game.Size, (long)_game.Cursor + plies);
        SelectPage(Value("--page") switch { "engines" => 1, "stats" => 2, "online" => 3, _ => 0 });
    }

    private void BindNetwork()
    {
        _network.StateChanged += state =>
        {
            if (_closing) return;
            if (state.PendingOperation != LobbyPendingOperation.None || state.Phase is LobbyPhase.CreatingRoom
                or LobbyPhase.JoiningRoom or LobbyPhase.RoomWaiting or LobbyPhase.Match or LobbyPhase.LeavingRoom)
                _networkTransitionOwnsBoard = true;
            else if (state.Phase is LobbyPhase.Disconnected or LobbyPhase.Error or LobbyPhase.Lobby)
                _networkTransitionOwnsBoard = false;
            if (NetworkOwnsBoard) { CancelSearch(); _paused = true; _board.HintMove = null; }
            if (state.Phase != LobbyPhase.Match && _online) RetainOnlineGameLocally();
            RefreshGame(state.Message);
        };
        _network.RoomsChanged += rooms => _lobby.SetRooms(rooms);
        _network.MatchStarted += match => { CancelSearch(); _online = true; _networkBoard = true; _paused = true; _reviewing = false; _game = new GameBoard(match.BoardSize); _analysis = new(); _diagnostics.Text = "联机对局\n手动落子不计入 AI 思考统计。"; RefreshGame(); };
        _network.MoveReceived += move =>
        {
            if (!_online || _game.BlackToMove == _network.IsLocalBlack || move.Ply != _game.Cursor + 1 || !_game.TryPlay(move.X, move.Y))
            { LeaveRoom(); RefreshGame("收到不一致的棋局，正在返回大厅；当前棋谱保留"); return; }
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
            if (accepted) { if (kind == "restart") { _game.Reset(); _analysis = new(); } else _game.Undo(2); _board.HintMove = null; RefreshGame(); Persist(); }
            else RefreshGame("请求未获同意或已超时，棋局保持原样");
        };
        _network.OpponentLeft += () =>
        {
            RetainOnlineGameLocally();
            RefreshGame(_network.Snapshot.Phase == LobbyPhase.RoomWaiting
                ? "对手已离开 · 等待下一位对手；也可返回大厅，保留棋谱继续本地对局"
                : _network.Snapshot.Message);
        };
    }
    private void RetainOnlineGameLocally()
    {
        bool wasNetworkGame = _networkBoard || _online;
        _online = false; _networkBoard = false; _paused = true; CancelSearch();
        if (wasNetworkGame) { _mode.SelectedIndex = 2; Persist(); }
    }
    private void NetworkAction(Action action)
    {
        if (_closing) return;
        try { action(); RefreshGame(_network.Snapshot.Message); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { _lobby.ShowValidationError(e.Message); RefreshGame(e.Message); }
    }
    private void JoinSelectedRoom()
    {
        if (_lobby.SelectedRoom is not RoomListing room || room.Players >= room.Capacity) { RefreshGame("请选择一个有空位的房间"); return; }
        NetworkAction(() => _network.ConnectAndJoinRoom(_lobby.Nickname, room.Name));
    }
    private void LeaveRoom()
    { RetainOnlineGameLocally(); NetworkAction(_network.LeaveRoom); }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.N: if (_newGame.Enabled) NewGame(); return true;
            case Keys.Control | Keys.Z: if (_undo.Enabled) UndoMove(); return true;
            case Keys.Control | Keys.S: SaveGame(); return true;
            case Keys.Control | Keys.O: OpenGame(); return true;
            case Keys.Escape:
                if (_network.Snapshot.CanCancel && (_pages[3].Visible || NetworkOwnsBoard)) { NetworkAction(_network.CancelPendingOperation); return true; }
                if (_online || NetworkOwnsBoard) { RefreshGame("联机对局不会暂停；请在联机页返回大厅或等待当前请求完成"); return true; }
                CancelSearch(); _paused = true; RefreshGame("本地对局已暂停"); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true; CancelSearch(); Persist(); PersistPreferences(); _networkTimer.Stop(); _networkTimer.Dispose(); _network.Dispose(); _tips.Dispose();
        base.OnFormClosing(e);
    }
}
