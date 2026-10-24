namespace Liss.Gomoku.Networking;

internal sealed record LobbyRoomIntent(LobbyPendingOperation Operation, string Nickname, string RoomName, int BoardSize);

/// <summary>
/// Owns the single pending lobby action. Transport callbacks only report state;
/// room operations are dispatched after the SDK pump has finished its callbacks.
/// </summary>
internal sealed class LobbyWorkflow(
    Func<string, bool> connect,
    Func<LobbyRoomIntent, bool> matchmake,
    Func<bool> leave,
    Action disconnect,
    Func<long>? clock = null)
{
    private const long OperationTimeoutMs = 30_000;
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private LobbyRoomIntent? _intent;
    private bool _intentSent, _disconnectRequested;
    private LobbyPhase _phase = LobbyPhase.Disconnected;
    private string _message = "尚未连接", _roomName = "";
    private string? _returnFailure;
    private long? _deadline;
    private LobbyPhase _disconnectDestination;
    private string _disconnectMessage = "";

    internal LobbySnapshot Snapshot { get; private set; } = new(
        LobbyPhase.Disconnected, "尚未连接", "", "", LobbyPendingOperation.None, true, false, true, false);
    internal event Action<LobbySnapshot>? Changed;
    internal bool IsDisconnecting => _disconnectRequested;
    internal bool NeedsLobbyRecovery => _returnFailure is not null && !_disconnectRequested;
    internal bool CanAcceptRoom => !_disconnectRequested && _intentSent && _intent is not null
        && _phase is LobbyPhase.CreatingRoom or LobbyPhase.JoiningRoom;

    internal void Connect(string nickname)
    {
        if (_phase == LobbyPhase.Connecting) return;
        if (_phase is LobbyPhase.Lobby or LobbyPhase.RoomWaiting or LobbyPhase.Match) return;
        if (!Snapshot.CanConnect) { SetMessage("当前操作尚未完成，请先等待或取消"); return; }
        BeginConnect(nickname);
    }

    internal void StartRoom(LobbyRoomIntent intent)
    {
        if (_intent is not null)
        {
            if (_intent == intent) return;
            SetMessage("已有房间操作正在进行；请先取消，再选择其他房间");
            return;
        }
        if (!Snapshot.CanCreateOrJoin)
        {
            SetMessage(_phase is LobbyPhase.RoomWaiting or LobbyPhase.Match
                ? "请先离开当前房间，再创建或加入其他房间"
                : "当前操作尚未完成，请稍后重试");
            return;
        }
        _intent = intent;
        _intentSent = false;
        if (_phase is LobbyPhase.Disconnected or LobbyPhase.Error) BeginConnect(intent.Nickname);
        else if (_phase == LobbyPhase.Connecting) SetMessage(ConnectingMessage());
        else BeginMatchmaking();
    }

    private void BeginConnect(string nickname)
    {
        _returnFailure = null;
        _roomName = "";
        _disconnectRequested = false;
        SetPhase(LobbyPhase.Connecting, ConnectingMessage(), timed: true);
        if (_disconnectRequested || _phase != LobbyPhase.Connecting) return;
        try
        {
            if (!connect(nickname)) FailAndDisconnect("无法开始连接，请检查网络后重试");
        }
        catch (Exception e) when (IsRecoverable(e)) { FailAndDisconnect("无法开始连接，请检查网络后重试"); }
    }

    private string ConnectingMessage() => _intent is null ? "正在连接 Photon · Asia 大厅"
        : $"正在连接大厅 · 连通后自动{(_intent.Operation == LobbyPendingOperation.CreateRoom ? "创建" : "加入")}「{_intent.RoomName}」";

    private void BeginMatchmaking()
    {
        if (_intent is null) return;
        SetPhase(_intent.Operation == LobbyPendingOperation.CreateRoom ? LobbyPhase.CreatingRoom : LobbyPhase.JoiningRoom,
            $"正在{(_intent.Operation == LobbyPendingOperation.CreateRoom ? "创建" : "加入")}房间「{_intent.RoomName}」", timed: true);
    }

    internal void LobbyReady()
    {
        if (_disconnectRequested || _phase is LobbyPhase.Disconnected or LobbyPhase.Error) return;
        _roomName = "";
        if (_returnFailure is not null)
        {
            string failure = _returnFailure;
            ClearIntent();
            _returnFailure = null;
            SetPhase(LobbyPhase.Lobby, failure + " · 已返回大厅，可重试");
        }
        else if (_intent is not null)
        {
            if (!_intentSent) BeginMatchmaking();
        }
        else SetPhase(LobbyPhase.Lobby, "已连接 · Photon Asia 大厅");
    }

    internal void RoomJoined(string roomName)
    {
        if (!CanAcceptRoom) return;
        _roomName = roomName;
        ClearIntent();
        _returnFailure = null;
        SetPhase(LobbyPhase.RoomWaiting, $"已加入「{roomName}」 · 等待对手");
    }

    internal void MatchStarted(bool localBlack)
    {
        if (_disconnectRequested || _phase != LobbyPhase.RoomWaiting) return;
        SetPhase(LobbyPhase.Match, "对局已开始 · " + (localBlack ? "你执黑先行" : "你执白后行"));
    }

    internal void OpponentLeft()
    {
        if (_disconnectRequested || _phase is not (LobbyPhase.RoomWaiting or LobbyPhase.Match)) return;
        SetPhase(LobbyPhase.RoomWaiting, "对手已离开 · 等待下一位对手，或离开房间");
    }

    internal void LeaveRoom()
    {
        if (!Snapshot.CanLeaveRoom) return;
        ClearIntent();
        SetPhase(LobbyPhase.LeavingRoom, "正在退出房间 · 返回大厅", timed: true);
        if (_disconnectRequested || _phase != LobbyPhase.LeavingRoom) return;
        try
        {
            if (!leave()) FailAndDisconnect("退房请求未发出，已断开连接；当前棋谱保留");
        }
        catch (Exception e) when (IsRecoverable(e)) { FailAndDisconnect("退房失败，已断开连接；当前棋谱保留"); }
    }

    internal void CancelPendingOperation()
    {
        if (!Snapshot.CanCancel) return;
        BeginDisconnect(LobbyPhase.Disconnected, "操作已取消 · 可重新连接或直接创建/加入房间", "正在取消操作 · 断开连接");
    }

    internal void Disconnect()
    {
        if (_disconnectRequested) return;
        if (_phase is LobbyPhase.Disconnected or LobbyPhase.Error)
        {
            ClearIntent();
            _roomName = "";
            SetPhase(LobbyPhase.Disconnected, "已断开连接 · 可直接创建或加入房间");
            return;
        }
        BeginDisconnect(LobbyPhase.Disconnected, "已断开连接 · 可直接创建或加入房间", "正在断开连接");
    }

    internal void FailAndDisconnect(string message) => BeginDisconnect(LobbyPhase.Error, message, message + " · 正在断开连接");

    private void BeginDisconnect(LobbyPhase destination, string finalMessage, string progressMessage)
    {
        if (_disconnectRequested) return;
        _disconnectRequested = true;
        _disconnectDestination = destination;
        _disconnectMessage = finalMessage;
        ClearIntent();
        _returnFailure = null;
        SetPhase(LobbyPhase.Disconnecting, progressMessage);
        TryDisconnect();
    }

    private void TryDisconnect()
    {
        try { disconnect(); }
        catch (Exception e) when (IsRecoverable(e)) { SetMessage("正在等待连接释放，请稍候"); }
    }

    internal void Disconnected(string cause)
    {
        if (!_disconnectRequested && (_phase is LobbyPhase.Disconnected or LobbyPhase.Error)) return;
        ClearIntent();
        _returnFailure = null;
        _roomName = "";
        if (_disconnectRequested)
        {
            _disconnectRequested = false;
            SetPhase(_disconnectDestination, _disconnectMessage);
        }
        else SetPhase(LobbyPhase.Error, "连接已断开 · " + cause + " · 可重新连接");
    }

    internal void MatchmakingFailed(string message)
    {
        if (_disconnectRequested || _intent is null) return;
        _returnFailure = message;
        // The SDK may still be returning from its game server. Do not enable a
        // second operation until JoinedLobby is really reached after dispatch.
        SetMessage(message + " · 正在返回大厅");
    }

    internal void AfterService(bool inLobby, bool inRoom, bool transportStopped)
    {
        if (_disconnectRequested)
        {
            if (transportStopped) Disconnected("");
            else if (inLobby || inRoom) TryDisconnect(); // Late success after Cancel must never win.
            return;
        }
        if ((_phase is LobbyPhase.Disconnected or LobbyPhase.Error) && (inLobby || inRoom))
        {
            BeginDisconnect(_phase, _message, "正在释放已取消的旧连接");
            return;
        }
        if (_deadline is long deadline && _clock() >= deadline)
        {
            FailAndDisconnect(_phase switch
            {
                LobbyPhase.Connecting => "连接大厅超时（30 秒），请检查网络后重试",
                LobbyPhase.LeavingRoom => "退房超时（30 秒），已结束当前连接；棋谱保留",
                _ => "房间操作超时（30 秒），请重新连接后重试"
            });
            return;
        }
        if (!inLobby) return;
        if (_returnFailure is not null) { LobbyReady(); return; }
        if (_intent is null || _intentSent) return;
        if (_phase == LobbyPhase.Connecting) BeginMatchmaking();
        if (_phase is not (LobbyPhase.CreatingRoom or LobbyPhase.JoiningRoom)) return;
        _intentSent = true;
        try
        {
            if (matchmake(_intent)) return;
        }
        catch (Exception e) when (IsRecoverable(e)) { }
        if (_disconnectRequested || _phase is not (LobbyPhase.CreatingRoom or LobbyPhase.JoiningRoom)) return;
        string operation = _intent?.Operation == LobbyPendingOperation.CreateRoom ? "创建" : "加入";
        ClearIntent();
        SetPhase(LobbyPhase.Lobby, operation + "房间请求未发出 · 请重试");
    }

    internal void SetMessage(string message)
    {
        _message = message;
        Publish();
    }

    private void ClearIntent() { _intent = null; _intentSent = false; }

    private void SetPhase(LobbyPhase phase, string message, bool timed = false)
    {
        _phase = phase;
        _message = message;
        _deadline = timed ? _clock() + OperationTimeoutMs : null;
        Publish();
    }

    private void Publish()
    {
        var next = new LobbySnapshot(_phase, _message, _roomName, _intent?.RoomName ?? "", _intent?.Operation ?? LobbyPendingOperation.None,
            _phase is LobbyPhase.Disconnected or LobbyPhase.Error,
            _phase is LobbyPhase.Connecting or LobbyPhase.CreatingRoom or LobbyPhase.JoiningRoom,
            _intent is null && (_phase is LobbyPhase.Disconnected or LobbyPhase.Error or LobbyPhase.Lobby or LobbyPhase.Connecting),
            _phase is LobbyPhase.RoomWaiting or LobbyPhase.Match);
        if (next == Snapshot) return;
        Snapshot = next;
        if (Changed is null) return;
        foreach (Action<LobbySnapshot> handler in Changed.GetInvocationList())
        {
            if (Snapshot != next) break; // A subscriber may synchronously cancel.
            try { handler(next); } catch (Exception e) when (IsRecoverable(e)) { }
        }
    }

    private static bool IsRecoverable(Exception e) => e is not OutOfMemoryException and not StackOverflowException;
}
