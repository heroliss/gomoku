using Liss.Gomoku.Networking;

internal static class LobbyWorkflowChecks
{
    internal static void Run(Action<string, bool> check)
    {
        static LobbyRoomIntent Create(string room = "own-room", string nickname = "player", int size = 15)
            => new(LobbyPendingOperation.CreateRoom, nickname, room, size);
        static LobbyRoomIntent Join(string room = "own-room", string nickname = "player")
            => new(LobbyPendingOperation.JoinRoom, nickname, room, 15);

        var fresh = new Fixture();
        check("lobby initial state allows direct room action without connection prerequisite",
            fresh.State.Phase == LobbyPhase.Disconnected && fresh.State.CanConnect && fresh.State.CanCreateOrJoin
            && !fresh.State.CanCancel && !fresh.State.CanLeaveRoom && fresh.State.PendingOperation == LobbyPendingOperation.None);
        fresh.Workflow.Connect("old-name");
        fresh.Workflow.Connect("another-name");
        check("lobby repeated connect is idempotent and allows one room intent while connecting",
            fresh.ConnectNames.SequenceEqual(["old-name"]) && fresh.State.Phase == LobbyPhase.Connecting
            && fresh.State.CanCancel && fresh.State.CanCreateOrJoin && !fresh.State.CanConnect);
        fresh.Workflow.StartRoom(Join(nickname: "new-name"));
        fresh.Workflow.StartRoom(Join(nickname: "new-name"));
        check("lobby connecting accepts latest requested nickname without reconnecting",
            fresh.ConnectNames.Count == 1 && fresh.Sent.Count == 0 && fresh.State.PendingRoomName == "own-room"
            && fresh.State.PendingOperation == LobbyPendingOperation.JoinRoom && !fresh.State.CanCreateOrJoin);
        fresh.Workflow.StartRoom(Create("other-room"));
        check("lobby conflicting action cannot replace the pending room or nickname",
            fresh.State.PendingRoomName == "own-room" && fresh.State.PendingOperation == LobbyPendingOperation.JoinRoom);
        fresh.Workflow.LobbyReady();
        check("lobby callback schedules matching without dispatching inside callback",
            fresh.State.Phase == LobbyPhase.JoiningRoom && fresh.Sent.Count == 0 && fresh.State.CanCancel);
        fresh.Pump(inLobby: true);
        fresh.Pump(inLobby: true);
        check("lobby queued match dispatches once after pump with edited nickname",
            fresh.Sent.SequenceEqual([Join(nickname: "new-name")]) && fresh.State.Phase == LobbyPhase.JoiningRoom);
        fresh.Workflow.RoomJoined("own-room");
        check("lobby validated room clears pending action and permits leaving",
            fresh.State.Phase == LobbyPhase.RoomWaiting && fresh.State.RoomName == "own-room"
            && fresh.State.PendingOperation == LobbyPendingOperation.None && fresh.State.PendingRoomName.Length == 0
            && fresh.State.CanLeaveRoom && !fresh.State.CanCreateOrJoin && !fresh.State.CanCancel);
        fresh.Workflow.MatchStarted(false);
        check("lobby starts match as a distinct phase", fresh.State.Phase == LobbyPhase.Match && fresh.State.CanLeaveRoom);
        fresh.Workflow.OpponentLeft();
        fresh.Time += 600_000;
        fresh.Pump(inRoom: true);
        check("lobby waiting room has no connection timeout", fresh.State.Phase == LobbyPhase.RoomWaiting && fresh.Disconnects == 0);
        fresh.Workflow.LeaveRoom();
        fresh.Workflow.LeaveRoom();
        check("lobby leave is one request and keeps actions locked while returning",
            fresh.Leaves == 1 && fresh.State.Phase == LobbyPhase.LeavingRoom && !fresh.State.CanLeaveRoom
            && !fresh.State.CanConnect && !fresh.State.CanCreateOrJoin && !fresh.State.CanCancel);
        fresh.Workflow.LobbyReady();
        check("lobby leave completion restores lobby actions and clears room",
            fresh.State.Phase == LobbyPhase.Lobby && fresh.State.RoomName.Length == 0 && fresh.State.CanCreateOrJoin);
        fresh.Workflow.StartRoom(Create(nickname: "lobby-new-name", size: 19));
        fresh.Pump(inLobby: true);
        check("lobby already-connected action uses newly supplied nickname and board",
            fresh.Sent[^1] == Create(nickname: "lobby-new-name", size: 19) && fresh.ConnectNames.Count == 1);

        var cancelled = new Fixture();
        cancelled.Workflow.StartRoom(Create());
        check("lobby direct create automatically begins connection with visible pending action",
            cancelled.ConnectNames.SequenceEqual(["player"]) && cancelled.State.Phase == LobbyPhase.Connecting
            && cancelled.State.PendingOperation == LobbyPendingOperation.CreateRoom);
        cancelled.Workflow.CancelPendingOperation();
        cancelled.Workflow.CancelPendingOperation();
        cancelled.Workflow.Connect("cannot-reconnect-yet");
        cancelled.Workflow.LobbyReady();
        cancelled.Workflow.RoomJoined("late-room");
        cancelled.Pump(inLobby: true);
        check("lobby cancel drops queue and late lobby/room success cannot defeat cancellation",
            cancelled.State.Phase == LobbyPhase.Disconnecting && cancelled.State.PendingOperation == LobbyPendingOperation.None
            && cancelled.Sent.Count == 0 && cancelled.ConnectNames.Count == 1 && cancelled.Disconnects == 2
            && !cancelled.State.CanConnect && !cancelled.State.CanCreateOrJoin);
        cancelled.Pump(stopped: true);
        var stopped = cancelled.State;
        cancelled.Workflow.Disconnected("duplicate SDK notification");
        cancelled.Pump(stopped: true);
        check("lobby cancellation completes only after real stop and duplicate disconnect is harmless",
            cancelled.State == stopped && stopped.Phase == LobbyPhase.Disconnected && stopped.CanConnect && stopped.CanCreateOrJoin);
        cancelled.Workflow.LobbyReady(); cancelled.Workflow.RoomJoined("very-late-room"); cancelled.Workflow.MatchStarted(true);
        check("lobby callbacks after completed cancellation cannot revive lobby or match", cancelled.State == stopped);
        cancelled.Workflow.StartRoom(Join("fresh-room", "retry-name"));
        cancelled.Workflow.LobbyReady();
        cancelled.Pump(inLobby: true);
        check("lobby explicit fresh retry has no stale cancelled intent",
            cancelled.ConnectNames.SequenceEqual(["player", "retry-name"]) && cancelled.Sent.SequenceEqual([Join("fresh-room", "retry-name")]));

        var sentCancel = new Fixture();
        sentCancel.Workflow.Connect("player"); sentCancel.Workflow.LobbyReady();
        sentCancel.Workflow.StartRoom(Create()); sentCancel.Pump(inLobby: true);
        sentCancel.Workflow.CancelPendingOperation();
        sentCancel.Workflow.RoomJoined("own-room");
        sentCancel.Pump(inRoom: true);
        sentCancel.Pump(stopped: true);
        check("lobby cancelling an already-sent create disconnects instead of admitting late room",
            sentCancel.Sent.Count == 1 && sentCancel.Disconnects == 2 && sentCancel.State.Phase == LobbyPhase.Disconnected
            && sentCancel.State.RoomName.Length == 0);

        var refused = new Fixture { SendResult = false };
        refused.Workflow.StartRoom(Create()); refused.Workflow.LobbyReady(); refused.Pump(inLobby: true);
        check("lobby failed local send returns to usable lobby with failure visible",
            refused.State.Phase == LobbyPhase.Lobby && refused.State.CanCreateOrJoin
            && refused.State.PendingOperation == LobbyPendingOperation.None && refused.State.Message.Contains("未发出"));
        var denied = new Fixture();
        denied.Workflow.StartRoom(Join()); denied.Workflow.LobbyReady(); denied.Pump(inLobby: true);
        denied.Workflow.MatchmakingFailed("加入失败（房间已满）");
        denied.Pump();
        check("lobby remote rejection does not falsely unlock before SDK returns to lobby",
            denied.State.Phase == LobbyPhase.JoiningRoom && !denied.State.CanCreateOrJoin && denied.State.Message.Contains("房间已满"));
        denied.Pump(inLobby: true);
        denied.Pump(inLobby: true);
        check("lobby rejection reason persists after returning and action is not resent",
            denied.State.Phase == LobbyPhase.Lobby && denied.State.CanCreateOrJoin
            && denied.State.Message.Contains("房间已满") && denied.Sent.Count == 1);

        var timeout = new Fixture();
        timeout.Workflow.StartRoom(Create());
        timeout.Time = 29_999; timeout.Pump();
        check("lobby connecting stays pending before deadline", timeout.State.Phase == LobbyPhase.Connecting && timeout.Disconnects == 0);
        timeout.Time = 30_000; timeout.Pump();
        check("lobby connection timeout clears pending and disconnects at thirty seconds",
            timeout.State.Phase == LobbyPhase.Disconnecting && timeout.Disconnects == 1
            && timeout.State.PendingOperation == LobbyPendingOperation.None && !timeout.State.CanConnect);
        timeout.Pump(stopped: true);
        var error = timeout.State;
        timeout.Workflow.Disconnected("late duplicate");
        timeout.Time += 600_000; timeout.Pump(stopped: true);
        check("lobby terminal error remains actionable without automatic retry loop",
            timeout.State == error && error.Phase == LobbyPhase.Error && error.CanConnect && error.CanCreateOrJoin
            && error.Message.Contains("30 秒") && timeout.ConnectNames.Count == 1);
        timeout.Workflow.StartRoom(Join("retry"));
        timeout.Workflow.LobbyReady(); timeout.Pump(inLobby: true);
        check("lobby room operation gets its own deadline after connecting",
            timeout.State.Phase == LobbyPhase.JoiningRoom && timeout.Sent.Count == 1);
        timeout.Time += 30_000; timeout.Pump(); timeout.Pump(stopped: true);
        check("lobby matching timeout ends session and enables explicit retry",
            timeout.State.Phase == LobbyPhase.Error && timeout.State.CanConnect && timeout.State.Message.Contains("房间操作超时"));

        var connectRefused = new Fixture { ConnectResult = false };
        connectRefused.Workflow.Connect("player"); connectRefused.Pump(stopped: true);
        check("lobby refused connect becomes retryable error", connectRefused.State.Phase == LobbyPhase.Error && connectRefused.State.CanConnect);
        var loss = new Fixture();
        loss.Workflow.Connect("player"); loss.Workflow.LobbyReady();
        loss.Workflow.Disconnected("TimeoutDisconnect");
        check("lobby unexpected transport loss clears lobby and exposes retry",
            loss.State.Phase == LobbyPhase.Error && loss.State.RoomName.Length == 0 && loss.State.CanConnect && loss.State.Message.Contains("TimeoutDisconnect"));
        loss.Workflow.Disconnect();
        check("lobby explicit disconnect resets error to intentional disconnected state", loss.State.Phase == LobbyPhase.Disconnected);
        var leaveFailure = new Fixture { LeaveResult = false };
        leaveFailure.EnterRoom(); leaveFailure.Workflow.LeaveRoom(); leaveFailure.Pump(stopped: true);
        check("lobby refused leave falls back to terminal disconnect", leaveFailure.State.Phase == LobbyPhase.Error && leaveFailure.Disconnects == 1);
        var leaveTimeout = new Fixture();
        leaveTimeout.EnterRoom(); leaveTimeout.Workflow.LeaveRoom();
        leaveTimeout.Time = 30_000; leaveTimeout.Pump(); leaveTimeout.Pump(stopped: true);
        check("lobby timed-out leave preserves retry path", leaveTimeout.State.Phase == LobbyPhase.Error && leaveTimeout.State.Message.Contains("退房超时"));

        var eventCancel = new Fixture();
        var observed = new List<LobbyPhase>();
        eventCancel.Workflow.Changed += state => { if (state.Phase == LobbyPhase.Connecting) eventCancel.Workflow.CancelPendingOperation(); };
        eventCancel.Workflow.Changed += state => observed.Add(state.Phase);
        eventCancel.Workflow.StartRoom(Create()); eventCancel.Pump(stopped: true);
        check("lobby cancellation during state callback prevents starting new transport afterwards",
            eventCancel.ConnectNames.Count == 0 && eventCancel.State.Phase == LobbyPhase.Disconnected);
        check("lobby nested cancellation does not deliver stale connecting snapshot to later observers",
            observed.SequenceEqual([LobbyPhase.Disconnecting, LobbyPhase.Disconnected]));
        var observer = new Fixture();
        int delivered = 0;
        observer.Workflow.Changed += _ => throw new InvalidOperationException("intentional observer failure");
        observer.Workflow.Changed += _ => delivered++;
        observer.Workflow.Connect("player");
        check("lobby broken observer cannot suppress other state subscribers or connection", delivered == 1 && observer.ConnectNames.Count == 1);
    }

    private sealed class Fixture
    {
        internal long Time;
        internal bool ConnectResult = true, SendResult = true, LeaveResult = true;
        internal int Disconnects, Leaves;
        internal List<string> ConnectNames { get; } = [];
        internal List<LobbyRoomIntent> Sent { get; } = [];
        internal LobbyWorkflow Workflow { get; }
        internal LobbySnapshot State => Workflow.Snapshot;

        internal Fixture()
        {
            Workflow = new(name => { ConnectNames.Add(name); return ConnectResult; },
                intent => { Sent.Add(intent); return SendResult; },
                () => { Leaves++; return LeaveResult; }, () => Disconnects++, () => Time);
        }

        internal void Pump(bool inLobby = false, bool inRoom = false, bool stopped = false)
            => Workflow.AfterService(inLobby, inRoom, stopped);

        internal void EnterRoom()
        {
            Workflow.StartRoom(new(LobbyPendingOperation.CreateRoom, "player", "own-room", 15));
            Workflow.LobbyReady(); Pump(inLobby: true); Workflow.RoomJoined("own-room");
        }
    }
}
