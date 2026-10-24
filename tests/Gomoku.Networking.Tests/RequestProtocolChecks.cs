using Liss.Gomoku.Networking;

internal static class RequestProtocolChecks
{
    internal static void Run(Action<string, bool> check)
    {
        {
            var f = new Fixture(); f.AcceptUntilCommit();
            check("accepted reply does not mutate either board before server commit", f.AppliedA == 0 && f.AppliedB == 0 && f.A.IsPending && f.B.IsPending);
            f.Drain();
            check("server All commit applies once to both and unlocks", f.AppliedA == 1 && f.AppliedB == 1 && !f.A.IsPending && !f.B.IsPending && f.A.Session == f.B.Session && f.A.Session != Fixture.InitialSession);
            f.Deliver(new(1, f.LastCommit!, true));
            check("duplicate commit is ignored after session epoch change", f.AppliedA == 1 && f.AppliedB == 1);
            string old = f.A.Session;
            f.AcceptUntilCommit("undo"); f.Drain();
            check("undo also rotates session to reject delayed old moves", f.AppliedA == 2 && f.AppliedB == 2 && f.A.Session == f.B.Session && f.A.Session != old);
        }
        {
            var f = new Fixture(); f.A.Begin("restart"); f.DeliverOne();
            f.B.Reply(false); f.Drain();
            check("decline changes no board and resolves both", f.AppliedA == 0 && f.AppliedB == 0 && f.RejectedA == 1 && f.RejectedB == 1 && !f.A.IsPending && !f.B.IsPending);
        }
        {
            var f = new Fixture(); f.A.Begin("restart"); f.DeliverOne(); string oldId = f.B.IncomingId!;
            f.B.Reply(true); f.Clock.Advance(TimeSpan.FromSeconds(31));
            f.DeliverOne(); f.Drain();
            check("late accepted reply after requester deadline cannot apply unilaterally", f.AppliedA == 0 && f.AppliedB == 0 && !f.A.IsPending && !f.B.IsPending);
            f.A.Begin("undo"); f.DeliverOne();
            check("stale dialog cannot answer a later request", !f.B.Reply(true, oldId) && f.B.IsPending);
            f.B.Reply(true, f.B.IncomingId); f.Drain();
            check("new request succeeds after stale reply rejection", f.AppliedA == 1 && f.AppliedB == 1);
        }
        {
            var f = new Fixture(); f.A.Begin("restart"); f.DeliverOne();
            f.Clock.Advance(TimeSpan.FromSeconds(31)); f.A.Tick(); f.Drain();
            check("decision timeout broadcasts cancellation to both peers", f.RejectedA == 1 && f.RejectedB == 1 && !f.A.IsPending && !f.B.IsPending);
        }
        {
            var f = new Fixture(); f.AcceptUntilCommit();
            f.Clock.Advance(TimeSpan.FromSeconds(11)); f.B.Tick();
            f.DeliverOne(0); f.Drain();
            check("commit before racing abort applies to both", f.AppliedA == 1 && f.AppliedB == 1 && f.A.Session == f.B.Session);
        }
        {
            var f = new Fixture(); f.AcceptUntilCommit();
            f.Clock.Advance(TimeSpan.FromSeconds(11)); f.B.Tick();
            f.DeliverOne(1); f.Drain();
            check("abort before racing commit changes neither peer", f.AppliedA == 0 && f.AppliedB == 0 && f.RejectedA == 1 && f.RejectedB == 1 && !f.A.IsPending && !f.B.IsPending);
        }
        {
            var f = new Fixture(); f.AcceptUntilCommit(); f.Queue.Clear();
            f.Clock.Advance(TimeSpan.FromSeconds(11)); f.A.Tick(); f.B.Tick(); f.Queue.Clear();
            f.Clock.Advance(TimeSpan.FromSeconds(11)); f.A.Tick(); f.B.Tick();
            check("lost commit and abort fail closed within bounded time", f.FailedA == 1 && f.FailedB == 1 && !f.A.IsPending && !f.B.IsPending && f.AppliedA == 0 && f.AppliedB == 0);
        }
        {
            var f = new Fixture(); f.AcceptUntilCommit();
            RequestWire commit = f.Queue[0].Message;
            f.A.Receive(commit, 2); f.B.Receive(commit, 2);
            check("commit from non-requester actor is ignored", f.AppliedA == 0 && f.AppliedB == 0);
            f.A.Receive(commit with { Session = Guid.NewGuid().ToString("N") }, 1);
            f.B.Receive(commit with { Id = "bad-id" }, 1);
            check("wrong session and malformed request id cannot commit", f.AppliedA == 0 && f.AppliedB == 0);
            f.Drain(); check("valid commit still succeeds after rejected fixtures", f.AppliedA == 1 && f.AppliedB == 1);
        }
        {
            var f = new Fixture(); f.A.Begin("restart"); f.B.Begin("undo"); f.Drain();
            check("simultaneous requests resolve without locking either side", !f.A.IsPending && !f.B.IsPending && f.AppliedA == 0 && f.AppliedB == 0);
        }
        {
            var f = new Fixture(); f.AcceptUntilCommit(); f.A.Clear(); f.B.Clear(); f.Drain();
            check("leaving clears pending and ignores delayed commit", !f.A.IsPending && !f.B.IsPending && f.AppliedA == 0 && f.AppliedB == 0);
        }
        {
            var f = new Fixture { SendEnabled = false }; f.A.Begin("restart");
            check("send failure ends pending state without applying request", f.FailedA == 1 && !f.A.IsPending && f.AppliedA == 0);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        internal void Advance(TimeSpan duration) => ticks += duration.Ticks;
    }
    private sealed record Packet(int Sender, RequestWire Message, bool All);
    private sealed class Fixture
    {
        internal const string InitialSession = "11111111111111111111111111111111";
        internal readonly MatchRequestProtocol A, B;
        internal readonly ManualClock Clock = new();
        internal readonly List<Packet> Queue = [];
        internal int AppliedA, AppliedB, RejectedA, RejectedB, FailedA, FailedB;
        internal bool SendEnabled = true;
        internal RequestWire? LastCommit;
        internal Fixture()
        {
            bool Send(int actor, RequestWire message, bool all)
            {
                if (!SendEnabled) return false;
                Queue.Add(new(actor, message, all));
                if (message.Type == RequestWireKind.Commit) LastCommit = message;
                return true;
            }
            A = new((message, all) => Send(1, message, all), Clock);
            B = new((message, all) => Send(2, message, all), Clock);
            A.Start(InitialSession, 1, 2); B.Start(InitialSession, 2, 1);
            A.Resolved += (_, accepted) => { if (accepted) AppliedA++; else RejectedA++; };
            B.Resolved += (_, accepted) => { if (accepted) AppliedB++; else RejectedB++; };
            A.Failed += () => { FailedA++; A.Clear(); }; B.Failed += () => { FailedB++; B.Clear(); };
        }
        internal void AcceptUntilCommit(string kind = "restart")
        {
            A.Begin(kind); DeliverOne(); B.Reply(true); DeliverOne();
        }
        internal void DeliverOne(int index = 0) { Packet p = Queue[index]; Queue.RemoveAt(index); Deliver(p); }
        internal void Deliver(Packet p)
        {
            if (p.All || p.Sender != 1) A.Receive(p.Message, p.Sender);
            if (p.All || p.Sender != 2) B.Receive(p.Message, p.Sender);
        }
        internal void Drain()
        {
            int deliveries = 0;
            while (Queue.Count > 0) { if (++deliveries > 100) throw new Exception("Protocol fixture did not quiesce."); DeliverOne(); }
        }
    }
}
