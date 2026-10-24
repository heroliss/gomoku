using System.Diagnostics;
using System.Text.Json;
using Liss.Gomoku.Networking;

var checks = new List<object>();
object? liveDetails = null;
void Check(string name, bool result)
{
    checks.Add(new { name, passed = result });
    Console.WriteLine((result ? "PASS " : "FAIL ") + name);
    if (!result) throw new Exception("Acceptance failed: " + name);
}
void Rejects<T>(string name, Action action) where T : Exception
{
    try { action(); Check(name, false); }
    catch (T) { Check(name, true); }
}

using (var local = new PhotonLobbyService())
{
    Check("SDK constructs on .NET 10", !local.IsConnected && !local.InRoom);
    Rejects<ArgumentException>("empty nickname rejected", () => local.Connect("  "));
    Rejects<ArgumentException>("control nickname rejected", () => local.Connect("a\nb"));
    Rejects<ArgumentException>("room name bounded", () => local.CreateRoom(new string('x', 49)));
    Rejects<ArgumentOutOfRangeException>("invalid board rejected", () => local.CreateRoom("unit", 14));
    Rejects<ArgumentException>("automatic create validates nickname before connecting", () => local.ConnectAndCreateRoom("", "own-room"));
    Rejects<ArgumentException>("automatic join validates room before connecting", () => local.ConnectAndJoinRoom("player", "\n"));
    Rejects<ArgumentOutOfRangeException>("automatic create validates board before connecting", () => local.ConnectAndCreateRoom("player", "own-room", 20));
    Check("invalid automatic actions leave disconnected snapshot untouched", local.Snapshot.Phase == LobbyPhase.Disconnected && local.Snapshot.CanCreateOrJoin);
    Exception? wrongThread = null;
    var thread = new Thread(() => { try { local.Service(); } catch (Exception e) { wrongThread = e; } });
    thread.Start(); thread.Join();
    Check("wrong thread rejected", wrongThread is InvalidOperationException);
    wrongThread = null;
    var actionThread = new Thread(() => { try { local.ConnectAndJoinRoom("player", "own-room"); } catch (Exception e) { wrongThread = e; } });
    actionThread.Start(); actionThread.Join();
    Check("automatic room action enforces owner thread", wrongThread is InvalidOperationException);
    local.LeaveRoom(); Check("offline move reports failure", !local.SendMove(0, 0, 1)); local.RequestUndo(); local.ReplyRequest(true);
    Check("offline operations safe", !local.InRoom);
    int snapshots = 0, ownerThread = Environment.CurrentManagedThreadId;
    local.StateChanged += _ => throw new InvalidOperationException("intentional state subscriber test");
    local.StateChanged += state => { if (state.Phase == LobbyPhase.Disconnected && Environment.CurrentManagedThreadId == ownerThread) snapshots++; };
    local.Disconnect();
    Check("public structured snapshots isolate subscribers on owner thread", snapshots == 1);
    local.Dispose(); local.Dispose(); local.Service();
    Rejects<ObjectDisposedException>("disposed use rejected", () => local.Connect("unit"));
}
Rejects<ArgumentException>("invalid AppId rejected", () => new PhotonLobbyService("not-an-app-id"));
RequestProtocolChecks.Run(Check);
LobbyWorkflowChecks.Run(Check);
using (var failure = new PhotonLobbyService())
{
    int disconnected = 0;
    failure.OpponentLeft += () => disconnected++;
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    typeof(PhotonLobbyService).GetField("_matchActive", flags)!.SetValue(failure, true);
    var recover = typeof(PhotonLobbyService).GetMethod("RecoverServiceFailure", flags)!;
    recover.Invoke(failure, null); failure.Service(); recover.Invoke(failure, null);
    Check("SDK poll failure notifies match end exactly once and unlocks", disconnected == 1 && !failure.InRoom && !failure.HasPendingRequest);
}

if (args.Contains("--live"))
{
    using var black = new PhotonLobbyService();
    using var white = new PhotonLobbyService();
    var blackThread = Environment.CurrentManagedThreadId;
    bool startedA = false, startedB = false, opponentLeft = false, statesOnOwnerThread = true;
    var phasesA = new List<LobbyPhase>();
    var phasesB = new List<LobbyPhase>();
    int sizeA = 0, sizeB = 0, receivedA = 0, receivedB = 0, restartA = 0, restartB = 0, undoA = 0, undoB = 0, declinedA = 0, declinedB = 0;
    bool acceptRequests = true;
    bool automaticallyReply = true;
    string opponentA = "", opponentB = "";
    IReadOnlyList<RoomListing> roomsB = Array.Empty<RoomListing>();
    string TransportState(PhotonLobbyService service)
    {
        var client = typeof(PhotonLobbyService).GetField("_client", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(service)!;
        return client.GetType().GetProperty("State")!.GetValue(client)!.ToString()!;
    }
    black.StatusChanged += status => Console.WriteLine("A [" + TransportState(black) + "] " + status);
    white.StatusChanged += status => Console.WriteLine("B [" + TransportState(white) + "] " + status);
    black.StateChanged += state => { phasesA.Add(state.Phase); statesOnOwnerThread &= Environment.CurrentManagedThreadId == blackThread; };
    white.StateChanged += state => { phasesB.Add(state.Phase); statesOnOwnerThread &= Environment.CurrentManagedThreadId == blackThread; };
    // One broken subscriber must not suppress delivery to subsequent subscribers.
    white.MatchStarted += _ => throw new InvalidOperationException("intentional subscriber test");
    black.MatchStarted += match => { startedA = match.LocalBlack; sizeA = match.BoardSize; opponentA = match.Opponent; };
    white.MatchStarted += match => { startedB = !match.LocalBlack; sizeB = match.BoardSize; opponentB = match.Opponent; };
    black.MoveReceived += move => { if (move == new NetworkMove(8, 8, 2)) receivedA++; Check("A callback UI thread", Environment.CurrentManagedThreadId == blackThread); };
    white.MoveReceived += move => { if (move == new NetworkMove(7, 7, 1)) receivedB++; Check("B callback UI thread", Environment.CurrentManagedThreadId == blackThread); };
    white.RoomsChanged += rooms => roomsB = rooms;
    white.RequestReceived += _ =>
    {
        if (!automaticallyReply) return;
        int applied = restartA + restartB + undoA + undoB;
        Check("reply uses current incoming request identity", white.ReplyRequest(acceptRequests, white.PendingIncomingRequestId));
        if (acceptRequests) Check("accepted live reply waits for All commit", white.HasPendingRequest && restartA + restartB + undoA + undoB == applied);
    };
    black.RequestReceived += _ => black.ReplyRequest(true);
    black.RequestResolved += (kind, accepted) => { if (accepted && kind == "restart") restartA++; if (accepted && kind == "undo") undoA++; if (!accepted) declinedA++; };
    white.RequestResolved += (kind, accepted) => { if (accepted && kind == "restart") restartB++; if (accepted && kind == "undo") undoB++; if (!accepted) declinedB++; };
    black.OpponentLeft += () => opponentLeft = true;
    void PumpUntil(Func<bool> predicate, string label, int seconds = 35)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate() && timer.Elapsed.TotalSeconds < seconds)
        {
            black.Service(); white.Service(); Thread.Sleep(10);
        }
        Check(label, predicate());
    }
    string Session(PhotonLobbyService service) => (string)typeof(PhotonLobbyService).GetField("_session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(service)!;
    // Raw wire fixtures are sent only between our two clients in our own room.
    void Raw(PhotonLobbyService service, byte code, object[] payload)
    {
        var client = typeof(PhotonLobbyService).GetField("_client", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(service)!;
        var accepted = (bool)client.GetType().GetMethod("OpRaiseEvent")!.Invoke(client, new object?[] { code, payload, true, null })!;
        Check("own-room wire fixture queued", accepted);
    }
    void PumpFor(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds) { black.Service(); white.Service(); Thread.Sleep(10); }
    }
    var room = "Liss-v3-check-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..8];
    try
    {
        black.ConnectAndCreateRoom("Liss acceptance A", room + "-cancelled", 15);
        Check("real automatic create exposes connecting intent immediately", black.Snapshot.Phase == LobbyPhase.Connecting
            && black.Snapshot.PendingOperation == LobbyPendingOperation.CreateRoom && black.Snapshot.CanCancel);
        black.CancelPendingOperation();
        PumpUntil(() => black.Snapshot.Phase == LobbyPhase.Disconnected, "real cancelled connect releases transport without entering room");
        Check("real cancelled queue clears intent", !black.InRoom && black.Snapshot.PendingOperation == LobbyPendingOperation.None && black.Snapshot.CanConnect);
        black.ConnectAndCreateRoom("Liss acceptance A", room + "-cancel2", 15);
        white.Connect("Liss before nickname edit");
        PumpUntil(() => black.Snapshot.Phase == LobbyPhase.CreatingRoom, "real second automatic create reaches sent matchmaking request");
        black.CancelPendingOperation();
        PumpUntil(() => black.Snapshot.Phase == LobbyPhase.Disconnected, "real sent matchmaking cancellation releases transport");
        Check("real cancelled matching cannot enter room on a late response", !black.InRoom && black.Snapshot.PendingOperation == LobbyPendingOperation.None);
        black.ConnectAndCreateRoom("Liss acceptance A", room, 15);
        black.ConnectAndCreateRoom("Liss acceptance A", room, 15);
        white.Connect("Liss before nickname edit"); white.Connect("Liss before nickname edit");
        PumpUntil(() => black.Snapshot.Phase == LobbyPhase.RoomWaiting && white.Snapshot.Phase == LobbyPhase.Lobby
            && roomsB.Any(r => r.Name == room && r.Players == 1 && r.Capacity == 2), "automatic own isolated room appears in second client's lobby", 65);
        Check("real waiting and lobby capabilities reflect actual connection", black.Snapshot.CanLeaveRoom && !black.Snapshot.CanCreateOrJoin
            && white.Snapshot.CanCreateOrJoin && !white.Snapshot.CanConnect && phasesA.Contains(LobbyPhase.CreatingRoom));
        Check("real cancelled intents leave no available lobby room", roomsB.All(r => r.Name != room + "-cancelled" && r.Name != room + "-cancel2"));
        white.ConnectAndCreateRoom("Liss acceptance B", room, 15);
        PumpUntil(() => white.Snapshot.Phase == LobbyPhase.Lobby && white.Status.Contains("创建房间失败"), "real duplicate room returns to usable lobby with failure visible");
        Check("real create rejection clears pending intent", white.Snapshot.CanCreateOrJoin && white.Snapshot.PendingOperation == LobbyPendingOperation.None);
        white.ConnectAndJoinRoom("Liss acceptance B", room + "-missing");
        PumpUntil(() => white.Snapshot.Phase == LobbyPhase.Lobby && white.Status.Contains("加入房间失败"), "real missing own room returns to usable lobby with failure visible");
        white.ConnectAndJoinRoom("Liss acceptance B", room);
        PumpUntil(() => startedA && startedB, "both peers start with opposite colors");
        Check("board and nickname agreement", sizeA == 15 && sizeB == 15 && opponentA == "Liss acceptance B" && opponentB == "Liss acceptance A");
        Check("real match snapshots and state events agree on owner thread", black.Snapshot.Phase == LobbyPhase.Match
            && white.Snapshot.Phase == LobbyPhase.Match && phasesB.Contains(LobbyPhase.JoiningRoom) && statesOnOwnerThread);
        var sessionBeforeRestart = Session(black);
        Raw(white, 51, new object[] { sessionBeforeRestart });
        Raw(white, 51, new object[] { sessionBeforeRestart, -1, 0, 1 });
        Raw(white, 51, new object[] { sessionBeforeRestart, 15, 0, 1 });
        Raw(white, 51, new object[] { sessionBeforeRestart, 0, 0, 0 });
        Raw(white, 51, new object[] { sessionBeforeRestart, "0", 0, 1 });
        Raw(white, 52, new object[] { sessionBeforeRestart, "restart" });
        Raw(white, 53, new object[] { sessionBeforeRestart, "restart", "unknown", true });
        Raw(white, 55, new object[] { sessionBeforeRestart, "restart" });
        Raw(white, 56, new object[] { sessionBeforeRestart, "undo", "unknown" });
        PumpFor(1500);
        Check("malformed remote arrays ignored without callback or crash", receivedA == 0 && !black.HasPendingRequest && startedA && startedB);
        Check("black move queued successfully", black.SendMove(7, 7, 1));
        PumpUntil(() => receivedB == 1, "black move received once");
        Check("white move queued successfully", white.SendMove(8, 8, 2));
        PumpUntil(() => receivedA == 1, "white move received once");
        black.RequestUndo();
        PumpUntil(() => undoA == 1 && undoB == 1, "undo request accepted on both clients");
        string sessionAfterUndo = Session(black);
        Check("live undo rotates epoch on both peers", sessionAfterUndo != sessionBeforeRestart && sessionAfterUndo == Session(white));
        acceptRequests = false;
        black.RequestRestart();
        PumpUntil(() => declinedA == 1 && declinedB == 1, "restart rejection resolves on both peers");
        Check("rejected restart preserves network session", Session(black) == sessionAfterUndo && Session(white) == sessionAfterUndo && !black.HasPendingRequest && !white.HasPendingRequest);
        automaticallyReply = false;
        int rejectionsA = declinedA, rejectionsB = declinedB;
        black.RequestRestart();
        PumpUntil(() => white.PendingIncomingRequestId is not null, "real unanswered request arrives at peer");
        string timedOutId = white.PendingIncomingRequestId!;
        Check("pending decisions block moves on both peers", !black.SendMove(0, 0, 3) && !white.SendMove(0, 0, 3));
        PumpUntil(() => !black.HasPendingRequest && !white.HasPendingRequest && declinedA > rejectionsA && declinedB > rejectionsB,
            "real 30-second timeout cancels on both peers", 45);
        Check("expired live dialog cannot accept", !white.ReplyRequest(true, timedOutId));
        Raw(white, 53, new object[] { sessionAfterUndo, "restart", timedOutId, true });
        PumpFor(1000);
        Check("late live accept does not reset either peer", restartA == 0 && restartB == 0 && Session(black) == sessionAfterUndo && Session(white) == sessionAfterUndo);
        automaticallyReply = true;
        acceptRequests = true;
        white.RequestRestart();
        PumpUntil(() => restartA == 1 && restartB == 1, "restart request accepted on both clients");
        Raw(white, 51, new object[] { sessionBeforeRestart, 8, 8, 2 });
        PumpFor(1500);
        Check("delayed move from old restart session ignored", receivedA == 1);
        Check("post-restart move queued", black.SendMove(7, 7, 1));
        PumpUntil(() => receivedB == 2, "moves work after restart session changes");
        white.LeaveRoom();
        PumpUntil(() => !white.InRoom && opponentLeft, "leave room notifies other peer");
        black.LeaveRoom();
        PumpUntil(() => !black.InRoom && roomsB.All(r => r.Name != room), "empty acceptance room removed");
        PumpUntil(() => black.Snapshot.Phase == LobbyPhase.Lobby && white.Snapshot.Phase == LobbyPhase.Lobby, "both clients return to structured lobby state");
        white.Disconnect();
        Check("real explicit disconnect blocks actions until transport stopped", white.Snapshot.Phase is LobbyPhase.Disconnecting or LobbyPhase.Disconnected);
        PumpUntil(() => white.Snapshot.Phase == LobbyPhase.Disconnected, "real explicit disconnect completes");
        startedA = startedB = false;
        black.CreateRoom(room + "-19", 19);
        PumpUntil(() => black.Snapshot.Phase == LobbyPhase.RoomWaiting, "19-line room created after leaving");
        white.Connect("Liss pre-reconnect edit");
        white.ConnectAndJoinRoom("Liss reconnected B", room + "-19");
        white.ConnectAndJoinRoom("Liss reconnected B", room + "-19");
        Check("real connecting join queues once with new nickname", white.Snapshot.Phase == LobbyPhase.Connecting
            && white.Snapshot.PendingOperation == LobbyPendingOperation.JoinRoom && !white.Snapshot.CanCreateOrJoin);
        PumpUntil(() => startedA && startedB && sizeA == 19 && sizeB == 19, "19-line match and rejoin handshake agreed");
        Check("real reconnect is fresh match and connecting nickname edit reaches opponent", opponentA == "Liss reconnected B"
            && black.Snapshot.Phase == LobbyPhase.Match && white.Snapshot.Phase == LobbyPhase.Match && statesOnOwnerThread);
    }
    finally
    {
        black.Disconnect(); white.Disconnect();
        var cleanup = Stopwatch.StartNew();
        while ((black.Snapshot.Phase != LobbyPhase.Disconnected || white.Snapshot.Phase != LobbyPhase.Disconnected) && cleanup.Elapsed.TotalSeconds < 5)
        { black.Service(); white.Service(); Thread.Sleep(10); }
        liveDetails = new { room, cancelledRooms = new[] { room + "-cancelled", room + "-cancel2" },
            phasesA = phasesA.Select(phase => phase.ToString()).ToArray(), phasesB = phasesB.Select(phase => phase.ToString()).ToArray(),
            statesOnOwnerThread, finalA = black.Snapshot.Phase.ToString(), finalB = white.Snapshot.Phase.ToString() };
        Check("real acceptance clients explicitly disconnect and release their room", black.Snapshot.Phase == LobbyPhase.Disconnected && white.Snapshot.Phase == LobbyPhase.Disconnected);
    }
}
if (args.FirstOrDefault(a => a.StartsWith("--report=")) is string reportArg)
{
    var path = Path.GetFullPath(reportArg[9..]);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(new { atUtc = DateTimeOffset.UtcNow, runtime = Environment.Version.ToString(), live = args.Contains("--live"), liveDetails, checks }, new JsonSerializerOptions { WriteIndented = true }));
}
if (checks.Any(check => !(bool)check.GetType().GetProperty("passed")!.GetValue(check)!))
    throw new Exception("An asynchronous callback acceptance check failed.");
Console.WriteLine("ALL CHECKS PASSED: " + checks.Count);
