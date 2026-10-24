using System.Collections;
using ExitGames.Client.Photon;
using ExitGames.Client.Photon.LoadBalancing;

namespace Liss.Gomoku.Networking;

public sealed record RoomListing(string Name, int Players, int Capacity);
public sealed record MatchInfo(bool LocalBlack, int BoardSize, string Opponent);
public sealed record NetworkMove(int X, int Y, int Ply);

/// <summary>
/// Photon Asia lobby and two-player matches. Call every 20–50 ms from the same UI
/// thread on which the service was created. All public events run on that thread.
/// V3 has a separate AppVersion and is deliberately isolated from the legacy game.
/// </summary>
public sealed class PhotonLobbyService : IDisposable
{
    // A public client application identifier, not a dashboard/API secret.
    public const string DefaultAppId = "db5cbc62-3c9e-4a33-8a95-bf2c57ff82e7";
    public const string Protocol = "liss-gomoku-v3";
    private const byte StartEvent = 50, MoveEvent = 51, RequestEvent = 52, ReplyEvent = 53, HelloEvent = 54, CommitEvent = 55, AbortEvent = 56;
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly LoadBalancingClient _client;
    private readonly string _appId;
    private bool _disposed, _matchStarting, _matchActive, _localBlack, _leaving, _roomValidated, _servicing, _lobbyRecoverySent;
    private string _session = "";
    private string _opponentName = "";
    private int _helloSentTo;
    private int _boardSize = 15;
    private readonly MatchRequestProtocol _requests;
    private readonly LobbyWorkflow _lobby;
    private string _lastStatus = "尚未连接";

    public PhotonLobbyService(string? appId = null)
    {
        _appId = appId ?? DefaultAppId;
        if (!Guid.TryParse(_appId, out _)) throw new ArgumentException("Photon AppId 格式无效。", nameof(appId));
        _client = new LoadBalancingClient(ConnectionProtocol.Udp)
        {
            AppId = _appId,
            AppVersion = Protocol,
            AutoJoinLobby = true
        };
        _client.OnEventAction += OnEvent;
        _client.OnOpResponseAction += OnOperation;
        _client.OnStateChangeAction += OnState;
        _client.loadBalancingPeer.DisconnectTimeout = 10000;
        _requests = new MatchRequestProtocol((message, all) => Send(message.Type switch
        {
            RequestWireKind.Offer => RequestEvent, RequestWireKind.Reply => ReplyEvent,
            RequestWireKind.Commit => CommitEvent, _ => AbortEvent
        }, message.Type == RequestWireKind.Reply
            ? new object[] { message.Session, message.Kind, message.Id, message.Accept }
            : new object[] { message.Session, message.Kind, message.Id }, all));
        _requests.Status += SetStatus;
        _requests.Offered += kind => Publish(RequestReceived, kind);
        _requests.Resolved += (kind, accepted) => { _session = _requests.Session; Publish(RequestResolved, kind, accepted); };
        _requests.Failed += EndFailedMatch;
        _lobby = new LobbyWorkflow(ConnectTransport, SendRoomOperation, LeaveTransport, DisconnectTransport);
        _lobby.Changed += snapshot =>
        {
            bool messageChanged = _lastStatus != snapshot.Message;
            _lastStatus = snapshot.Message;
            PublishSnapshot(snapshot);
            if (messageChanged && Snapshot == snapshot) Publish(StatusChanged, snapshot.Message);
        };
    }

    public bool IsConnected => !_disposed && _client.IsConnectedAndReady;
    public bool InRoom => !_disposed && !_leaving && _client.State == ClientState.Joined && _client.CurrentRoom is not null;
    public bool IsHost => InRoom && _client.LocalPlayer.IsMasterClient;
    public bool IsLocalBlack => _localBlack;
    public bool HasPendingRequest => _requests.IsPending;
    public string? PendingIncomingRequestId => _requests.IncomingId;
    public LobbySnapshot Snapshot => _lobby.Snapshot;
    public string Status => Snapshot.Message;
    public string RoomName => InRoom ? _client.CurrentRoom.Name : "";
    public string LocalName => _client.NickName ?? "";
    public string OpponentName => _opponentName.Length > 0 ? _opponentName : "等待对手";
    public event Action<string>? StatusChanged;
    public event Action<LobbySnapshot>? StateChanged;
    public event Action<IReadOnlyList<RoomListing>>? RoomsChanged;
    public event Action<MatchInfo>? MatchStarted;
    public event Action<NetworkMove>? MoveReceived;
    public event Action<string>? RequestReceived;
    public event Action<string, bool>? RequestResolved;
    public event Action? OpponentLeft;

    private Player? Opponent => InRoom ? _client.CurrentRoom.Players.Values.FirstOrDefault(p => !p.IsLocal && !p.IsInactive) : null;

    public void Service()
    {
        CheckThread();
        if (_disposed || _servicing) return;
        _servicing = true;
        try
        {
            try { _client.Service(); }
            catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
            {
                // A malformed legacy SDK callback must not take down the application's UI loop.
                RecoverServiceFailure();
            }
            RecoverLobbyAfterRejection();
            // In this legacy SDK, Joined is raised before the join response has
            // finished populating room properties. Validate only after dispatch.
            if (InRoom && !_roomValidated && !_lobby.CanAcceptRoom)
                _lobby.FailAndDisconnect("未请求的房间连接已关闭；请重新创建或加入房间");
            if (InRoom && !_roomValidated && ValidateRoom())
            {
                _roomValidated = true;
                _lobby.RoomJoined(_client.CurrentRoom.Name);
            }
            if (InRoom && _roomValidated) { SendHello(); MaybeStartMatch(); }
            _lobby.AfterService(_client.State == ClientState.JoinedLobby, _client.State == ClientState.Joined,
                _client.State is ClientState.Uninitialized or ClientState.Disconnected);
            _requests.Tick();
        }
        finally { _servicing = false; }
    }

    public void Connect(string nickname)
    {
        CheckUsable();
        var name = CleanName(nickname, 32, "昵称");
        _lobby.Connect(name);
    }

    public void CreateRoom(string roomName, int boardSize = 15)
    {
        CheckUsable();
        var name = CleanName(roomName, 48, "房间名");
        if (boardSize is not (15 or 19)) throw new ArgumentOutOfRangeException(nameof(boardSize), "在线棋盘支持 15 路或 19 路。");
        if (LocalName.Length == 0) { SetStatus("请先填写昵称并连接，或直接使用自动创建房间"); return; }
        _lobby.StartRoom(new(LobbyPendingOperation.CreateRoom, LocalName, name, boardSize));
    }

    public void ConnectAndCreateRoom(string nickname, string roomName, int boardSize = 15)
    {
        CheckUsable();
        var player = CleanName(nickname, 32, "昵称");
        var room = CleanName(roomName, 48, "房间名");
        if (boardSize is not (15 or 19)) throw new ArgumentOutOfRangeException(nameof(boardSize), "在线棋盘支持 15 路或 19 路。");
        _lobby.StartRoom(new(LobbyPendingOperation.CreateRoom, player, room, boardSize));
    }

    public void ConnectAndJoinRoom(string nickname, string roomName)
    {
        CheckUsable();
        var player = CleanName(nickname, 32, "昵称");
        var room = CleanName(roomName, 48, "房间名");
        _lobby.StartRoom(new(LobbyPendingOperation.JoinRoom, player, room, 15));
    }

    private bool ConnectTransport(string nickname)
    {
        _lobbyRecoverySent = false;
        _client.NickName = nickname;
        _leaving = false;
        ClearMatch();
        return _client.ConnectToRegionMaster("asia");
    }

    private bool SendRoomOperation(LobbyRoomIntent intent)
    {
        _lobbyRecoverySent = false;
        // Nickname may have been edited while connecting or browsing the lobby.
        // Set it only immediately before matching so the new room sees it.
        _client.NickName = intent.Nickname;
        ClearMatch();
        if (intent.Operation == LobbyPendingOperation.JoinRoom) return _client.OpJoinRoom(intent.RoomName);
        _boardSize = intent.BoardSize;
        var options = new RoomOptions
        {
            MaxPlayers = 2,
            IsOpen = true,
            IsVisible = true,
            PlayerTtl = 0,
            EmptyRoomTtl = 0,
            CleanupCacheOnLeave = true,
            CustomRoomProperties = new Hashtable { ["protocol"] = Protocol, ["board"] = intent.BoardSize },
            CustomRoomPropertiesForLobby = new[] { "protocol", "board" }
        };
        return _client.OpCreateRoom(intent.RoomName, options, null);
    }

    public void JoinRoom(string roomName)
    {
        CheckUsable();
        var name = CleanName(roomName, 48, "房间名");
        if (LocalName.Length == 0) { SetStatus("请先填写昵称并连接，或直接使用自动加入房间"); return; }
        _lobby.StartRoom(new(LobbyPendingOperation.JoinRoom, LocalName, name, 15));
    }

    public void LeaveRoom()
    {
        CheckUsable();
        _lobby.LeaveRoom();
    }

    public void CancelPendingOperation() { CheckUsable(); _lobby.CancelPendingOperation(); }

    public void Disconnect() { CheckUsable(); _lobby.Disconnect(); }

    private bool LeaveTransport()
    {
        _leaving = true;
        ClearMatch();
        return _client.OpLeaveRoom();
    }

    private void DisconnectTransport()
    {
        bool hadOpponent = _matchActive;
        _leaving = true;
        ClearMatch();
        Publish(RoomsChanged, Array.Empty<RoomListing>());
        _client.Disconnect();
        if (hadOpponent) Publish(OpponentLeft);
    }

    public bool SendMove(int x, int y, int ply)
    {
        CheckUsable();
        if (!CanPlay()) return false;
        if (x < 0 || y < 0 || x >= _boardSize || y >= _boardSize || ply < 1 || ply > _boardSize * _boardSize)
            throw new ArgumentOutOfRangeException(nameof(x), "落子坐标或手数无效。");
        if (HasPendingRequest) { SetStatus("请先完成当前请求"); return false; }
        if (Send(MoveEvent, new object[] { _session, x, y, ply })) return true;
        SetStatus("落子发送失败，请重新连接");
        return false;
    }

    public void RequestRestart() => BeginRequest("restart");
    public void RequestUndo() => BeginRequest("undo");

    public bool ReplyRequest(bool accept, string? expectedRequestId = null)
    {
        CheckUsable();
        return CanPlay() && _requests.Reply(accept, expectedRequestId);
    }

    private void BeginRequest(string kind)
    {
        CheckUsable();
        if (!CanPlay()) return;
        _requests.Begin(kind);
    }

    private bool CanPlay()
    {
        if (InRoom && _matchActive && Opponent is not null) return true;
        SetStatus("请等待双方加入房间");
        return false;
    }

    private bool Send(byte code, object[] content, bool all = false)
    {
        try { return _client.OpRaiseEvent(code, content, true, new RaiseEventOptions { Receivers = all ? ReceiverGroup.All : ReceiverGroup.Others }); }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            SetStatus("网络发送失败，请重新连接");
            return false;
        }
    }

    private void OnState(ClientState state)
    {
        switch (state)
        {
            case ClientState.JoinedLobby:
                if (_lobby.IsDisconnecting) return;
                _leaving = false;
                _lobbyRecoverySent = false;
                ClearMatch();
                _lobby.LobbyReady();
                RefreshRooms();
                break;
            case ClientState.Joined:
                if (_lobby.IsDisconnecting) return;
                _leaving = false;
                _lobbyRecoverySent = false;
                break;
            case ClientState.Disconnected:
                var hadOpponent = _matchActive;
                _leaving = false;
                _lobbyRecoverySent = false;
                ClearMatch();
                _lobby.Disconnected(_client.DisconnectedCause.ToString());
                Publish(RoomsChanged, Array.Empty<RoomListing>());
                if (hadOpponent) Publish(OpponentLeft);
                break;
        }
    }

    private bool ValidateRoom()
    {
        var properties = _client.CurrentRoom.CustomProperties;
        if (properties["protocol"] as string != Protocol || properties["board"] is not int size || size is not (15 or 19)
            || _client.CurrentRoom.MaxPlayers != 2)
        {
            _lobby.FailAndDisconnect("房间协议不兼容，请使用 v3 房间");
            return false;
        }
        _boardSize = size;
        return true;
    }

    private void OnOperation(OperationResponse response)
    {
        if (response.ReturnCode == 0) return;
        // The debug string can contain server details; only expose stable operation/code.
        string message = response.OperationCode switch
        {
            OperationCode.CreateGame => "创建房间失败（名称可能已存在）",
            OperationCode.JoinGame => "加入房间失败（房间不存在、已满或已关闭）",
            _ => "网络操作被拒绝 · " + response.ReturnCode
        };
        if (response.OperationCode is OperationCode.CreateGame or OperationCode.JoinGame) _lobby.MatchmakingFailed(message);
        else SetStatus(message);
    }

    private void RecoverLobbyAfterRejection()
    {
        if (!_lobby.NeedsLobbyRecovery || _lobbyRecoverySent || _client.Server != LoadBalancingClient.ServerConnection.MasterServer
            || _client.State is not (ClientState.Joining or ClientState.ConnectedToMaster or ClientState.Authenticated)) return;
        _lobbyRecoverySent = true;
        try
        {
            // SDK 4.0.5 can remain in Joining after a master-server rejection.
            // Re-enter its lobby once, after response dispatch. The public peer
            // API handles the legacy client's overly strict readiness guard.
            if (_client.OpJoinLobby(null) || _client.loadBalancingPeer.OpJoinLobby()) return;
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { }
        _lobby.FailAndDisconnect("房间操作失败，无法恢复大厅；请重新连接");
    }

    private void RefreshRooms()
    {
        if (_lobby.IsDisconnecting) return;
        var rooms = _client.RoomInfoList.Values
            .Where(r => r.IsVisible && r.IsOpen && r.MaxPlayers == 2 && r.CustomProperties["protocol"] as string == Protocol)
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => new RoomListing(r.Name, r.PlayerCount, r.MaxPlayers)).ToArray();
        Publish(RoomsChanged, rooms);
    }

    private void MaybeStartMatch()
    {
        if (!InRoom || !_roomValidated || !IsHost || _matchStarting || _matchActive || Opponent is null || _opponentName.Length == 0) return;
        _matchStarting = true;
        var session = Guid.NewGuid().ToString("N");
        if (!Send(StartEvent, new object[] { Protocol, session, _boardSize, _client.LocalPlayer.ID, LocalName }, all: true))
        {
            _matchStarting = false;
            SetStatus("开始对局失败，请重新加入房间");
        }
    }

    private void SendHello()
    {
        if (Opponent is not { } opponent || _helloSentTo == opponent.ID) return;
        if (Send(HelloEvent, new object[] { Protocol, LocalName })) _helloSentTo = opponent.ID;
    }

    private void OnEvent(EventData data)
    {
        if (_disposed) return;
        switch (data.Code)
        {
            case EventCode.GameList:
            case EventCode.GameListUpdate:
                RefreshRooms();
                return;
            case EventCode.Join:
                MaybeStartMatch();
                return;
            case EventCode.Leave:
                if (!InRoom) return;
                if (data.Parameters.TryGetValue(ParameterCode.ActorNr, out var left) && left is int actor && actor != _client.LocalPlayer.ID)
                {
                    bool validated = _roomValidated;
                    ClearMatch();
                    _roomValidated = validated; // The same room remains valid when its opponent leaves.
                    _lobby.OpponentLeft();
                    Publish(OpponentLeft);
                }
                return;
        }
        if (!InRoom || !data.Parameters.TryGetValue(ParameterCode.ActorNr, out var senderValue) || senderValue is not int sender
            || !_client.CurrentRoom.Players.ContainsKey(sender)
            || !data.Parameters.TryGetValue(ParameterCode.CustomEventContent, out var payload) || payload is not object[] values) return;

        if (data.Code == HelloEvent)
        {
            if (sender == _client.LocalPlayer.ID || sender != Opponent?.ID || values.Length != 2 || values[0] as string != Protocol
                || values[1] is not string name || name.Length is < 1 or > 32 || name.Any(char.IsControl)) return;
            _opponentName = name;
            SendHello();
            MaybeStartMatch();
            return;
        }

        if (data.Code == StartEvent)
        {
            if (!_roomValidated)
            {
                if (!_lobby.CanAcceptRoom) { _lobby.FailAndDisconnect("未请求的房间连接已关闭；请重新创建或加入房间"); return; }
                if (!ValidateRoom()) return;
                _roomValidated = true;
                _lobby.RoomJoined(_client.CurrentRoom.Name);
            }
            if (sender != _client.CurrentRoom.MasterClientId || values.Length != 5 || values[0] as string != Protocol
                || values[1] is not string session || !Guid.TryParseExact(session, "N", out _)
                || values[2] is not int size || size != _boardSize
                || values[3] is not int black || black != sender || Opponent is null || _matchActive
                || values[4] is not string hostName || hostName.Length is < 1 or > 32 || hostName.Any(char.IsControl)) return;
            if (sender != _client.LocalPlayer.ID) _opponentName = hostName;
            _session = session;
            _localBlack = _client.LocalPlayer.ID == black;
            _matchActive = true;
            _matchStarting = false;
            _requests.Start(_session, _client.LocalPlayer.ID, Opponent.ID);
            _lobby.MatchStarted(_localBlack);
            Publish(MatchStarted, new MatchInfo(_localBlack, _boardSize, OpponentName));
            return;
        }
        if (_matchActive && data.Code is RequestEvent or ReplyEvent or CommitEvent or AbortEvent)
        {
            if (values.Length != (data.Code == ReplyEvent ? 4 : 3) || values[0] is not string requestSession
                || values[1] is not string requestKind || values[2] is not string requestId
                || data.Code == ReplyEvent && values[3] is not bool) return;
            _requests.Receive(new RequestWire(data.Code switch
            {
                RequestEvent => RequestWireKind.Offer, ReplyEvent => RequestWireKind.Reply,
                CommitEvent => RequestWireKind.Commit, _ => RequestWireKind.Abort
            }, requestSession, requestKind, requestId, data.Code == ReplyEvent && (bool)values[3]), sender);
            return;
        }
        if (!_matchActive || sender == _client.LocalPlayer.ID || sender != Opponent?.ID
            || values.Length != 4 && data.Code == MoveEvent
            || values.Length < 1 || values[0] as string != _session) return;

        switch (data.Code)
        {
            case MoveEvent:
                if (values[1] is int x && values[2] is int y && values[3] is int ply
                    && x >= 0 && x < _boardSize && y >= 0 && y < _boardSize && ply > 0 && ply <= _boardSize * _boardSize)
                    Publish(MoveReceived, new NetworkMove(x, y, ply));
                break;
        }
    }

    private void RecoverServiceFailure()
    {
        _lobby.FailAndDisconnect("网络处理失败，已断开；当前棋谱保留");
    }

    private void EndFailedMatch()
    {
        _lobby.FailAndDisconnect("联机请求未能同步，当前对局已结束；棋谱保留");
    }

    private void ClearMatch()
    {
        _matchActive = _matchStarting = false;
        _roomValidated = false;
        _opponentName = "";
        _helloSentTo = 0;
        _session = "";
        _requests.Clear();
    }

    private static string CleanName(string? value, int maximum, string label)
    {
        var name = value?.Trim() ?? "";
        if (name.Length == 0 || name.Length > maximum || name.Any(char.IsControl))
            throw new ArgumentException($"{label}请填写 1–{maximum} 个可显示字符。");
        return name;
    }

    private void SetStatus(string message)
    {
        _lobby.SetMessage(message);
    }

    private void PublishSnapshot(LobbySnapshot snapshot)
    {
        if (StateChanged is null) return;
        foreach (Action<LobbySnapshot> handler in StateChanged.GetInvocationList())
        {
            if (Snapshot != snapshot) break;
            try { handler(snapshot); } catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { }
        }
    }

    // UI subscribers are isolated: one disposed/form callback cannot break polling
    // or prevent another subscriber from receiving an important disconnect event.
    private static void Publish<T>(Action<T>? handlers, T value)
    {
        if (handlers is null) return;
        foreach (Action<T> handler in handlers.GetInvocationList())
            try { handler(value); } catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { }
    }
    private static void Publish<T, U>(Action<T, U>? handlers, T first, U second)
    {
        if (handlers is null) return;
        foreach (Action<T, U> handler in handlers.GetInvocationList())
            try { handler(first, second); } catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { }
    }
    private static void Publish(Action? handlers)
    {
        if (handlers is null) return;
        foreach (Action handler in handlers.GetInvocationList())
            try { handler(); } catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { }
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _threadId)
            throw new InvalidOperationException("PhotonLobbyService 必须在创建它的同一线程上使用。");
    }
    private void CheckUsable()
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        CheckThread();
        if (_disposed) return;
        _disposed = true;
        _client.OnEventAction -= OnEvent;
        _client.OnOpResponseAction -= OnOperation;
        _client.OnStateChangeAction -= OnState;
        _client.Disconnect();
        ClearMatch();
        StatusChanged = null;
        StateChanged = null;
        RoomsChanged = null;
        MatchStarted = null;
        MoveReceived = null;
        RequestReceived = null;
        RequestResolved = null;
        OpponentLeft = null;
    }
}
