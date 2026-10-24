using System.Diagnostics;
using System.Text.Json;
using Liss.Gomoku.Networking;

var checks = new List<object>();
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
    Exception? wrongThread = null;
    var thread = new Thread(() => { try { local.Service(); } catch (Exception e) { wrongThread = e; } });
    thread.Start(); thread.Join();
    Check("wrong thread rejected", wrongThread is InvalidOperationException);
    local.LeaveRoom(); Check("offline move reports failure", !local.SendMove(0, 0, 1)); local.RequestUndo(); local.ReplyRequest(true);
    Check("offline operations safe", !local.InRoom);
    local.Dispose(); local.Dispose(); local.Service();
    Rejects<ObjectDisposedException>("disposed use rejected", () => local.Connect("unit"));
}
Rejects<ArgumentException>("invalid AppId rejected", () => new PhotonLobbyService("not-an-app-id"));
RequestProtocolChecks.Run(Check);
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
    bool lobbyA = false, lobbyB = false, startedA = false, startedB = false, opponentLeft = false;
    int sizeA = 0, sizeB = 0, receivedA = 0, receivedB = 0, restartA = 0, restartB = 0, undoA = 0, undoB = 0, declinedA = 0, declinedB = 0;
    bool acceptRequests = true;
    bool automaticallyReply = true;
    string opponentA = "", opponentB = "";
    IReadOnlyList<RoomListing> roomsB = Array.Empty<RoomListing>();
    black.StatusChanged += status => { Console.WriteLine("A " + status); if (status.Contains("大厅")) lobbyA = true; };
    white.StatusChanged += status => { Console.WriteLine("B " + status); if (status.Contains("大厅")) lobbyB = true; };
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
        black.Connect("Liss acceptance A"); white.Connect("Liss acceptance B");
        PumpUntil(() => lobbyA && lobbyB, "two real Photon Asia connections", 65);
        black.CreateRoom(room, 15);
        PumpUntil(() => black.InRoom && roomsB.Any(r => r.Name == room && r.Players == 1 && r.Capacity == 2), "created own isolated room appears in lobby");
        white.JoinRoom(room);
        PumpUntil(() => startedA && startedB, "both peers start with opposite colors");
        Check("board and nickname agreement", sizeA == 15 && sizeB == 15 && opponentA == "Liss acceptance B" && opponentB == "Liss acceptance A");
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
        PumpUntil(() => black.Status.Contains("大厅") && white.Status.Contains("大厅"), "both clients return to lobby");
        startedA = startedB = false;
        black.CreateRoom(room + "-19", 19);
        PumpUntil(() => black.InRoom && roomsB.Any(r => r.Name == room + "-19"), "19-line room created after leaving");
        white.JoinRoom(room + "-19");
        PumpUntil(() => startedA && startedB && sizeA == 19 && sizeB == 19, "19-line match and rejoin handshake agreed");
    }
    finally
    {
        if (black.InRoom) black.LeaveRoom();
        if (white.InRoom) white.LeaveRoom();
        for (var i = 0; i < 30; i++) { black.Service(); white.Service(); Thread.Sleep(10); }
    }
}
if (args.FirstOrDefault(a => a.StartsWith("--report=")) is string reportArg)
{
    var path = Path.GetFullPath(reportArg[9..]);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(new { atUtc = DateTimeOffset.UtcNow, runtime = Environment.Version.ToString(), live = args.Contains("--live"), checks }, new JsonSerializerOptions { WriteIndented = true }));
}
if (checks.Any(check => !(bool)check.GetType().GetProperty("passed")!.GetValue(check)!))
    throw new Exception("An asynchronous callback acceptance check failed.");
Console.WriteLine("ALL CHECKS PASSED: " + checks.Count);
