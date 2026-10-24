using System.ComponentModel;
using Liss.Gomoku.Networking;

namespace Liss.Gomoku.App;

/// <summary>State-driven lobby sidebar. The host owns networking; all calls/events use the UI thread.</summary>
internal sealed class NetworkLobbyControl : UserControl
{
    private readonly Label _title = Theme.Label("在线双人", 14, true);
    private readonly Label _message = Theme.Label("尚未连接");
    private readonly Label _nextStep = Theme.Label("");
    private readonly Label _pendingIntent = Theme.Label("", 9, true);
    private readonly TextBox _nickname = Theme.Input("Liss");
    private readonly TextBox _newRoomName = Theme.Input("Liss 的棋室");
    private readonly Button _connectionAction = Theme.Button("连接大厅");
    private readonly Button _create = Theme.Button("创建房间", primary: true);
    private readonly Button _join = Theme.Button("加入所选房间");
    private readonly Label _roomCount = Theme.Label("可加入房间 · 0", 9.5f, true);
    private readonly Label _emptyRooms = Theme.Label("暂无可加入的房间，创建后邀请朋友");
    private readonly RoomListBox _rooms = new();
    private readonly Label _roomName = Theme.Label("", 11, true);
    private readonly Label _occupancy = Theme.Label("", 10, true);
    private readonly Label _localName = Theme.Label("");
    private readonly Label _opponent = Theme.Label("");
    private readonly Label _request = Theme.Label("", 9, true);
    private readonly TableLayoutPanel _lobbyPane = Column();
    private readonly TableLayoutPanel _roomPane = Column();
    private readonly TableLayoutPanel _content = Column();
    private readonly Panel _viewport = new() { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
    private readonly Panel _joinBar = new() { Dock = DockStyle.Fill, Height = 44, Margin = Padding.Empty, Padding = new Padding(14, 0, 14, 8) };
    private readonly List<Label> _wrappingLabels = [];
    private readonly Dictionary<Control, bool> _visibility = [];
    private readonly Dictionary<Control, bool> _enabled = [];
    private readonly ToolTip _tips = new() { InitialDelay = 350, AutoPopDelay = 15_000, ShowAlways = true };
    private LobbySnapshot _snapshot = new(LobbyPhase.Disconnected, "尚未连接", "", "", LobbyPendingOperation.None, true, false, true, false);
    private LobbySnapshot? _displayedSnapshot;
    private RoomListing[] _listings = [];
    private string _opponentName = "";
    private string? _validationError;
    private bool _localBlack, _pendingRequest, _gameFinished, _updatingRooms;
    private int _hoverRoom = -1;
    private ConnectionAction _action;
    private enum ConnectionAction { None, Connect, Cancel, Disconnect, Leave }

    internal NetworkLobbyControl()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = new Font("Microsoft YaHei UI", 9.5f);
        Dock = DockStyle.Fill;
        Size = new Size(332, 546);
        MinimumSize = new Size(244, 0);
        BackColor = Theme.Panel; ForeColor = Theme.Text;
        Margin = Padding.Empty; AutoScroll = false; TabStop = false;
        AccessibleName = "在线双人大厅";

        // Only the content scrolls. Joining remains reachable at the bottom even if a long status
        // message or room name makes the content taller than the minimum supported window.
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Panel
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.Controls.Add(_viewport, 0, 0); shell.Controls.Add(_joinBar, 0, 1);
        Controls.Add(shell);
        _content.Padding = new Padding(14, 2, 14, 8);
        _viewport.Controls.Add(_content);
        _joinBar.Controls.Add(_join);

        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, AutoSize = true,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Panel
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _title.AutoSize = true; _title.Dock = DockStyle.Top; _title.MinimumSize = new Size(0, 36);
        _title.Margin = Padding.Empty; _title.Padding = new Padding(0, 4, 0, 2);
        _title.UseMnemonic = false;
        _connectionAction.Dock = DockStyle.Top; _connectionAction.Height = 36;
        _connectionAction.Margin = new Padding(8, 0, 0, 0);
        heading.Controls.Add(_title, 0, 0); heading.Controls.Add(_connectionAction, 1, 0);
        heading.SizeChanged += (_, _) =>
        {
            int width = Math.Max(60, heading.ClientSize.Width - 108);
            if (_title.MaximumSize.Width != width) _title.MaximumSize = new Size(width, 0);
        };
        Add(_content, heading);
        Add(_content, _message, 22, wrap: true);
        Add(_content, _nextStep, 22, wrap: true);
        Add(_content, _pendingIntent, 26, wrap: true);
        Add(_content, Theme.Label("对局昵称"), 22);
        Add(_content, _nickname, 34);
        Add(_content, _lobbyPane);
        Add(_content, _roomPane);

        _nickname.MaxLength = 32; _newRoomName.MaxLength = 48;
        _nickname.TabIndex = 0; _connectionAction.TabIndex = 1;
        _newRoomName.TabIndex = 2; _create.TabIndex = 3; _rooms.TabIndex = 4; _join.TabIndex = 5;
        _nickname.AccessibleName = "对局昵称";
        _newRoomName.AccessibleName = "新房间名称";
        _rooms.AccessibleName = "可加入的房间";
        _rooms.AccessibleDescription = "选择一个有空位的房间，然后按加入按钮或 Enter；也可双击房间。";
        foreach (Button button in new[] { _create, _join })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = Padding.Empty;
        }
        _nextStep.Font = new Font("Microsoft YaHei UI", 8.5f);
        _nextStep.ForeColor = Theme.Muted;
        _message.UseMnemonic = _nextStep.UseMnemonic = _pendingIntent.UseMnemonic = false;
        _roomName.UseMnemonic = _localName.UseMnemonic = _opponent.UseMnemonic = false;

        Add(_lobbyPane, Theme.Label("新房间名称"), 22);
        Add(_lobbyPane, _newRoomName, 34);
        Add(_lobbyPane, _create, 40);
        Add(_lobbyPane, _roomCount, 28);
        _rooms.Dock = DockStyle.Fill;
        Add(_lobbyPane, _rooms, 96);
        Add(_lobbyPane, _emptyRooms, 48, wrap: true);

        Add(_roomPane, Theme.Label("当前房间"), 24);
        Add(_roomPane, _roomName, 36, wrap: true);
        Add(_roomPane, _occupancy, 32);
        Add(_roomPane, _localName, 32, wrap: true);
        Add(_roomPane, _opponent, 32, wrap: true);
        Add(_roomPane, _request, 30, wrap: true);
        Add(_roomPane, Theme.Label("返回大厅会结束当前联机，棋谱会保留。", 8.5f), 46, wrap: true);

        _tips.SetToolTip(_nickname, "尚未提交房间操作时可修改；创建或加入时使用这里的昵称。操作排队后及房间内不能修改。");
        _tips.SetToolTip(_newRoomName, "创建一个双人房间，再邀请朋友从大厅加入。房间名最多 48 个字符。");
        _tips.SetToolTip(_create, "未连接时也可直接创建；程序会先连接，再提交建房操作。");
        _tips.SetToolTip(_join, "仅能加入所选的有空位房间；未连接时会先连接。");
        _nickname.TextChanged += (_, _) => { ClearValidationError(); RefreshRoomIdentity(); };
        _newRoomName.TextChanged += (_, _) => ClearValidationError();
        _connectionAction.Click += (_, _) => RunConnectionAction();
        _create.Click += (_, _) => { if (_snapshot.CanCreateOrJoin) CreateRequested?.Invoke(this, EventArgs.Empty); };
        _join.Click += (_, _) => JoinSelection();
        _rooms.SelectedIndexChanged += (_, _) =>
        {
            if (!_updatingRooms && !_rooms.IsReflowing) ClearValidationError();
            UpdateButtons();
            if (!_updatingRooms) UpdateRoomTooltip(_rooms.SelectedIndex);
        };
        _rooms.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && _rooms.IndexFromPoint(e.Location) is int index && index >= 0 && index == _rooms.SelectedIndex)
                JoinSelection();
        };
        _rooms.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || !_join.Enabled) return;
            e.Handled = true; e.SuppressKeyPress = true; JoinSelection();
        };
        _rooms.MouseMove += (_, e) =>
        {
            int index = _rooms.IndexFromPoint(e.Location);
            if (index == _hoverRoom) return;
            _hoverRoom = index; UpdateRoomTooltip(index);
        };
        _rooms.MouseLeave += (_, _) => { _hoverRoom = -1; UpdateRoomTooltip(_rooms.SelectedIndex); };
        _content.SizeChanged += (_, _) => UpdateWrapping();
        SetState(_snapshot, false, "", false);
        SetRooms([]);
    }

    internal event EventHandler? ConnectRequested;
    internal event EventHandler? CancelRequested;
    internal event EventHandler? DisconnectRequested;
    internal event EventHandler? CreateRequested;
    internal event EventHandler? JoinRequested;
    internal event EventHandler? LeaveRequested;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string Nickname
    {
        get => _nickname.Text;
        set { if (_nickname.Text != (value ?? "")) _nickname.Text = value ?? ""; }
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string NewRoomName
    {
        get => _newRoomName.Text;
        set { if (_newRoomName.Text != (value ?? "")) _newRoomName.Text = value ?? ""; }
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal RoomListing? SelectedRoom => _rooms.SelectedItem is RoomRow row ? row.Room : null;

    internal void SetState(LobbySnapshot state, bool localBlack, string opponentName, bool pendingRequest, bool gameFinished = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (IsDisposed || Disposing) return;
        opponentName ??= "";
        bool snapshotChanged = state != _displayedSnapshot;
        if (!snapshotChanged && _localBlack == localBlack && _opponentName == opponentName && _pendingRequest == pendingRequest && _gameFinished == gameFinished) return;
        if (snapshotChanged) _validationError = null;
        _snapshot = state; _displayedSnapshot = state;
        _localBlack = localBlack; _opponentName = opponentName; _pendingRequest = pendingRequest; _gameFinished = gameFinished;
        SuspendLayout(); _content.SuspendLayout();
        try
        {
            SetText(_title, state.Phase switch
            {
                LobbyPhase.Connecting => "正在连接大厅",
                LobbyPhase.Lobby => "在线大厅",
                LobbyPhase.CreatingRoom => "正在创建房间",
                LobbyPhase.JoiningRoom => "正在加入房间",
                LobbyPhase.RoomWaiting => "等待对手",
                LobbyPhase.Match when gameFinished => "对局已结束",
                LobbyPhase.Match => "联机对局中",
                LobbyPhase.LeavingRoom => "正在返回大厅",
                LobbyPhase.Disconnecting => "正在断开连接",
                LobbyPhase.Error => "连接未完成",
                _ => "在线双人"
            });
            RefreshMessage();
            SetText(_nextStep, NextStep(state, gameFinished));
            bool roomVisible = state.CanLeaveRoom || state.Phase is LobbyPhase.RoomWaiting or LobbyPhase.Match or LobbyPhase.LeavingRoom;
            SetVisible(_roomPane, roomVisible); SetVisible(_lobbyPane, !roomVisible);
            SetVisible(_joinBar, !roomVisible);
            bool showIntent = !roomVisible && state.PendingOperation != LobbyPendingOperation.None;
            SetVisible(_pendingIntent, showIntent);
            if (showIntent)
            {
                string intent = state.PendingOperation == LobbyPendingOperation.CreateRoom ? "待创建房间：" : "待加入房间：";
                SetText(_pendingIntent, intent + state.PendingRoomName);
                _tips.SetToolTip(_pendingIntent, _pendingIntent.Text);
            }
            bool editNickname = state.PendingOperation == LobbyPendingOperation.None &&
                state.Phase is LobbyPhase.Disconnected or LobbyPhase.Connecting or LobbyPhase.Lobby or LobbyPhase.Error;
            SetEnabled(_nickname, editNickname);
            SetEnabled(_newRoomName, state.CanCreateOrJoin);
            SetEnabled(_rooms, state.CanCreateOrJoin);
            RefreshRoomIdentity();
            SetText(_request, pendingRequest ? "对局请求处理中，请稍候…" : "");
            SetVisible(_request, pendingRequest);
            UpdateButtons();
        }
        finally { _content.ResumeLayout(true); ResumeLayout(true); }
    }

    internal void SetRooms(IReadOnlyList<RoomListing> rooms)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        if (IsDisposed || Disposing) return;
        RoomListing[] available = rooms.Where(IsJoinable).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.Ordinal).ToArray();
        if (_listings.SequenceEqual(available)) { RefreshRoomCount(); return; }
        string? selected = SelectedRoom?.Name;
        int previousTop = _rooms.Items.Count > 0 ? _rooms.TopIndex : 0;
        _listings = available; _updatingRooms = true; _hoverRoom = -1;
        _rooms.BeginUpdate();
        try
        {
            _rooms.Items.Clear();
            _rooms.Items.AddRange(available.Select(room => (object)new RoomRow(room)).ToArray());
            _rooms.SelectedIndex = selected is null ? -1 : Array.FindIndex(available, room => room.Name == selected);
            if (_rooms.Items.Count > 0) _rooms.TopIndex = Math.Clamp(previousTop, 0, _rooms.Items.Count - 1);
        }
        finally { _rooms.EndUpdate(); _updatingRooms = false; }
        RefreshRoomCount(); UpdateButtons(); UpdateRoomTooltip(_rooms.SelectedIndex);
    }

    /// <summary>Local input errors remain visible across identical polling snapshots.</summary>
    internal void ShowValidationError(string message)
    {
        if (IsDisposed || Disposing) return;
        _validationError = string.IsNullOrWhiteSpace(message) ? "输入未完成，请检查昵称与房间名称。" : message;
        RefreshMessage();
    }

    private void ClearValidationError()
    {
        if (_validationError is null) return;
        _validationError = null; RefreshMessage();
    }
    private void RefreshMessage()
    {
        string message = _validationError ?? _snapshot.Message ?? "";
        SetText(_message, message); SetVisible(_message, message.Length > 0);
        if (_message.AccessibleName != message) _message.AccessibleName = message;
        _tips.SetToolTip(_message, message);
    }
    private void RefreshRoomIdentity()
    {
        string room = _snapshot.RoomName ?? "";
        SetText(_roomName, room.Length == 0 ? "房间信息确认中" : room);
        if (_roomName.AccessibleName != "当前房间：" + room) _roomName.AccessibleName = "当前房间：" + room;
        _tips.SetToolTip(_roomName, room);
        bool hasOpponent = _snapshot.Phase == LobbyPhase.Match;
        SetText(_occupancy, _snapshot.Phase == LobbyPhase.LeavingRoom ? "正在退出当前房间" : hasOpponent ? _gameFinished ? "2 / 2 · 本局已结束" : "2 / 2 · 对局进行中" : "1 / 2 · 等待另一位玩家");
        SetText(_localName, $"你 · {(_localBlack ? "黑方" : "白方")}\n{Nickname}");
        SetText(_opponent, $"对手 · {(_localBlack ? "白方" : "黑方")}\n{(hasOpponent ? string.IsNullOrWhiteSpace(_opponentName) ? "另一位玩家" : _opponentName : "等待加入")}");
        _tips.SetToolTip(_localName, _localName.Text); _tips.SetToolTip(_opponent, _opponent.Text);
    }
    private void RefreshRoomCount()
    {
        SetText(_roomCount, $"可加入房间 · {_listings.Length}");
        SetVisible(_rooms, _listings.Length > 0); SetVisible(_emptyRooms, _listings.Length == 0);
    }
    private void UpdateButtons()
    {
        if (_snapshot.CanLeaveRoom) _action = ConnectionAction.Leave;
        else if (_snapshot.CanCancel) _action = ConnectionAction.Cancel;
        else if (_snapshot.Phase == LobbyPhase.Lobby) _action = ConnectionAction.Disconnect;
        else if (_snapshot.CanConnect) _action = ConnectionAction.Connect;
        else _action = ConnectionAction.None;
        SetText(_connectionAction, _action switch
        {
            ConnectionAction.Leave => "返回大厅",
            ConnectionAction.Cancel when _snapshot.PendingOperation == LobbyPendingOperation.CreateRoom => "取消创建",
            ConnectionAction.Cancel when _snapshot.PendingOperation == LobbyPendingOperation.JoinRoom => "取消加入",
            ConnectionAction.Cancel => "取消连接",
            ConnectionAction.Disconnect => "断开连接",
            ConnectionAction.Connect when _snapshot.Phase == LobbyPhase.Error => "重新连接",
            ConnectionAction.Connect => "连接大厅",
            _ when _snapshot.Phase == LobbyPhase.LeavingRoom => "返回中…",
            _ when _snapshot.Phase == LobbyPhase.Disconnecting => "断开中…",
            _ => "处理中…"
        });
        SetEnabled(_connectionAction, _action != ConnectionAction.None);
        SetEnabled(_create, _snapshot.CanCreateOrJoin);
        SetEnabled(_join, _snapshot.CanCreateOrJoin && SelectedRoom is { } room && IsJoinable(room));
        _connectionAction.AccessibleName = _connectionAction.Text;
    }
    private void RunConnectionAction()
    {
        switch (_action)
        {
            case ConnectionAction.Connect when _snapshot.CanConnect: ConnectRequested?.Invoke(this, EventArgs.Empty); break;
            case ConnectionAction.Cancel when _snapshot.CanCancel: CancelRequested?.Invoke(this, EventArgs.Empty); break;
            case ConnectionAction.Disconnect when _snapshot.Phase == LobbyPhase.Lobby: DisconnectRequested?.Invoke(this, EventArgs.Empty); break;
            case ConnectionAction.Leave when _snapshot.CanLeaveRoom: LeaveRequested?.Invoke(this, EventArgs.Empty); break;
        }
    }
    private void JoinSelection()
    { if (_snapshot.CanCreateOrJoin && SelectedRoom is { } room && IsJoinable(room)) JoinRequested?.Invoke(this, EventArgs.Empty); }

    private static string NextStep(LobbySnapshot state, bool gameFinished) => state.Phase switch
    {
        LobbyPhase.RoomWaiting => "等待另一位玩家加入，暂时不能落子。",
        LobbyPhase.Match when gameFinished => "可以请求重开，或返回大厅。",
        LobbyPhase.Match => "按棋盘提示轮流落子，可协商悔棋或重开。",
        LobbyPhase.Lobby => "创建房间邀请朋友，或选择房间加入。",
        LobbyPhase.Connecting when state.PendingOperation != LobbyPendingOperation.None => "连接后继续已选操作，也可取消。",
        LobbyPhase.Connecting => "可先填写昵称与房名，或直接创建房间。",
        LobbyPhase.CreatingRoom or LobbyPhase.JoiningRoom => "房间操作进行中，请稍候或取消。",
        LobbyPhase.LeavingRoom => "当前联机会结束，棋谱会保留。",
        LobbyPhase.Disconnecting => "正在断开，请稍候。",
        _ => "填写昵称后连接，也可直接创建房间。"
    };
    private void UpdateRoomTooltip(int index)
    {
        RoomListing? room = index >= 0 && index < _rooms.Items.Count && _rooms.Items[index] is RoomRow row ? row.Room : null;
        string text = room is null ? "选择房间后按 Enter 或双击加入。" : $"{room.Name}\n人数 {room.Players} / {room.Capacity}";
        _tips.SetToolTip(_rooms, text);
        _rooms.AccessibleDescription = SelectedRoom is { } selected ? $"已选择房间 {selected.Name}，人数 {selected.Players}/{selected.Capacity}。按 Enter 加入。" : "选择一个有空位的房间，然后按加入按钮或 Enter。";
    }
    private static bool IsJoinable(RoomListing? room) => room is not null && !string.IsNullOrWhiteSpace(room.Name) && room.Players >= 0 && room.Capacity > 0 && room.Players < room.Capacity;
    private static TableLayoutPanel Column()
    {
        var column = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 0, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Panel
        };
        column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return column;
    }
    private void Add(TableLayoutPanel layout, Control control, int height = 0, bool wrap = false)
    {
        int row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // Theme controls carry their own default margins; use one compact row rhythm here rather
        // than adding those margins on top of every explicitly sized row.
        control.Margin = Padding.Empty;
        if (wrap && control is Label label)
        {
            label.AutoSize = true; label.Dock = DockStyle.Top; label.MinimumSize = new Size(0, height);
            label.TextAlign = ContentAlignment.TopLeft; label.Padding = new Padding(0, 2, 0, 2);
            _wrappingLabels.Add(label);
        }
        else if (height > 0) { control.Height = height; control.Dock = DockStyle.Fill; }
        layout.Controls.Add(control, 0, row);
    }
    private void UpdateWrapping()
    {
        int width = Math.Max(80, _content.ClientSize.Width - _content.Padding.Horizontal);
        foreach (Label label in _wrappingLabels)
            if (label.MaximumSize.Width != width) label.MaximumSize = new Size(width, 0);
    }
    private static void SetText(Control control, string value) { if (control.Text != value) control.Text = value; }
    private void SetVisible(Control control, bool visible)
    {
        // Visible/Enabled getters include parent state. Cache the child's requested state so initial
        // updates made while this tab is hidden still apply, without repeated polling assignments.
        if (_visibility.TryGetValue(control, out bool previous) && previous == visible) return;
        _visibility[control] = visible; control.Visible = visible;
    }
    private void SetEnabled(Control control, bool enabled)
    {
        if (_enabled.TryGetValue(control, out bool previous) && previous == enabled) return;
        _enabled[control] = enabled; control.Enabled = enabled;
    }
    protected override void Dispose(bool disposing) { if (disposing) _tips.Dispose(); base.Dispose(disposing); }

    private sealed record RoomRow(RoomListing Room)
    { public override string ToString() => $"{Room.Name}，{Room.Players}/{Room.Capacity} 人"; }

    /// <summary>Natural wrapping keeps complete room names visible; native list items retain keyboard/a11y semantics.</summary>
    private sealed class RoomListBox : ListBox
    {
        private int _measuredWidth;
        internal bool IsReflowing { get; private set; }
        internal RoomListBox()
        {
            BackColor = Theme.Field; ForeColor = Theme.Text; BorderStyle = BorderStyle.FixedSingle;
            DrawMode = DrawMode.OwnerDrawVariable; IntegralHeight = false; SelectionMode = SelectionMode.One;
        }
        protected override void OnMeasureItem(MeasureItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not RoomRow row) { base.OnMeasureItem(e); return; }
            int width = Math.Max(60, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Scale(20));
            var measured = TextRenderer.MeasureText(e.Graphics, row.Room.Name, Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            e.ItemHeight = Math.Max(Scale(42), measured.Height + Font.Height + Scale(15));
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not RoomRow row) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var surface = new SolidBrush(selected ? Theme.Line : Theme.Field);
            e.Graphics.FillRectangle(surface, e.Bounds);
            int inset = Scale(10), top = Scale(6), footer = Font.Height + Scale(5);
            var name = new Rectangle(e.Bounds.Left + inset, e.Bounds.Top + top, Math.Max(1, e.Bounds.Width - inset * 2), Math.Max(Font.Height, e.Bounds.Height - footer - top));
            TextRenderer.DrawText(e.Graphics, row.Room.Name, Font, name, Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            var count = new Rectangle(e.Bounds.Left + inset, e.Bounds.Bottom - footer, Math.Max(1, e.Bounds.Width - inset * 2), Font.Height);
            TextRenderer.DrawText(e.Graphics, $"{row.Room.Players} / {row.Room.Capacity} 人", Font, count, selected ? Theme.Accent : Theme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            e.DrawFocusRectangle();
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (IsReflowing || _measuredWidth == ClientSize.Width || Items.Count == 0) return;
            _measuredWidth = ClientSize.Width;
            int selected = SelectedIndex, top = TopIndex;
            BeginUpdate(); IsReflowing = true;
            try { RefreshItems(); SelectedIndex = selected; if (Items.Count > 0) TopIndex = Math.Clamp(top, 0, Items.Count - 1); }
            finally { IsReflowing = false; EndUpdate(); }
        }
        private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96d);
    }
}
