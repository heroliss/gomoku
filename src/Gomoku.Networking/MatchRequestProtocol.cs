using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Gomoku.Networking.Tests")]

namespace Liss.Gomoku.Networking;

internal enum RequestWireKind { Offer, Reply, Commit, Abort }
internal sealed record RequestWire(RequestWireKind Type, string Session, string Kind, string Id, bool Accept = false);

/// <summary>
/// A request is only applied on the server's reliable All broadcast, never on a
/// local click/reply. Commit and abort therefore compete in the same ordered
/// server event stream. If that stream stops, end the match instead of unlocking
/// one peer against an unknown remote board.
/// </summary>
internal sealed class MatchRequestProtocol(Func<RequestWire, bool, bool> send, TimeProvider? clock = null)
{
    internal static readonly TimeSpan DecisionTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan CommitTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan AbortTimeout = TimeSpan.FromSeconds(10);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private Pending? _pending;
    private int _local, _remote;
    private enum Stage { Deciding, AwaitCommit, AwaitAbort }
    private sealed record Pending(string Kind, string Id, bool Outgoing, Stage Stage, bool Accepted, long Started);
    internal string Session { get; private set; } = "";
    internal bool IsPending => _pending is not null;
    internal string? IncomingId => _pending is { Outgoing: false, Stage: Stage.Deciding } p ? p.Id : null;
    internal event Action<string>? Status;
    internal event Action<string>? Offered;
    internal event Action<string, bool>? Resolved;
    internal event Action? Failed;

    internal void Start(string session, int local, int remote)
    {
        Clear(); Session = session; _local = local; _remote = remote;
    }
    internal void Clear() { _pending = null; Session = ""; _local = _remote = 0; }
    private bool Expired(Pending p) => _clock.GetElapsedTime(p.Started) >= (p.Stage switch
    {
        Stage.Deciding => DecisionTimeout, Stage.AwaitCommit => CommitTimeout, _ => AbortTimeout
    });
    private RequestWire Wire(RequestWireKind type, Pending p, bool accept = false) => new(type, Session, p.Kind, p.Id, accept);

    internal void Begin(string kind)
    {
        if (Session.Length == 0) return;
        if (kind is not ("restart" or "undo")) throw new ArgumentException("未知的请求类型。", nameof(kind));
        if (_pending is not null) { Status?.Invoke("已有等待答复或提交的请求"); return; }
        var p = new Pending(kind, Guid.NewGuid().ToString("N"), true, Stage.Deciding, false, _clock.GetTimestamp());
        _pending = p;
        if (!send(Wire(RequestWireKind.Offer, p), false)) { Fail("请求发送失败，已结束联机以保持棋局一致"); return; }
        Status?.Invoke(kind == "restart" ? "已请求重新开始，等待对方确认" : "已请求悔棋，等待对方确认");
    }

    internal bool Reply(bool accept, string? expectedId = null)
    {
        if (_pending is not { Outgoing: false, Stage: Stage.Deciding } p || expectedId is not null && p.Id != expectedId) return false;
        if (Expired(p)) { Abort(p, "对方请求已超时"); return false; }
        if (accept) _pending = p with { Accepted = true, Stage = Stage.AwaitCommit, Started = _clock.GetTimestamp() };
        if (!send(Wire(RequestWireKind.Reply, p, accept), false)) { Fail("回复发送失败，已结束联机以保持棋局一致"); return false; }
        if (!accept) { _pending = null; Status?.Invoke("请求未被接受"); Resolved?.Invoke(p.Kind, false); }
        else Status?.Invoke("已同意，等待服务器向双方提交");
        return true;
    }

    internal void Receive(RequestWire message, int sender)
    {
        if (Session.Length == 0 || message.Session != Session || sender != _local && sender != _remote
            || message.Kind is not ("restart" or "undo") || !Guid.TryParseExact(message.Id, "N", out _)) return;
        if (message.Type == RequestWireKind.Offer)
        {
            if (sender != _remote) return;
            if (_pending is not null)
            {
                // Repeated delivery of the same offer must not reject an already accepted request.
                if (!_pending.Outgoing && _pending.Id == message.Id && _pending.Kind == message.Kind) return;
                if (!send(message with { Type = RequestWireKind.Reply, Accept = false }, false)) Fail("拒绝冲突请求失败，已结束联机");
                return;
            }
            _pending = new(message.Kind, message.Id, false, Stage.Deciding, false, _clock.GetTimestamp());
            Offered?.Invoke(message.Kind);
            return;
        }
        if (_pending is not { } p || p.Kind != message.Kind || p.Id != message.Id) return;
        switch (message.Type)
        {
            case RequestWireKind.Reply:
                if (sender != _remote || !p.Outgoing || p.Stage != Stage.Deciding) return;
                if (Expired(p)) { Abort(p, "答复已超时，等待双方取消"); return; }
                if (!message.Accept) { _pending = null; Status?.Invoke("请求未被接受"); Resolved?.Invoke(p.Kind, false); return; }
                _pending = p with { Accepted = true, Stage = Stage.AwaitCommit, Started = _clock.GetTimestamp() };
                if (!send(Wire(RequestWireKind.Commit, p), true)) { Fail("提交发送失败，已结束联机以保持棋局一致"); return; }
                Status?.Invoke("对方已同意，等待服务器向双方提交");
                break;
            case RequestWireKind.Commit:
                if (!p.Accepted || sender != (p.Outgoing ? _local : _remote)) return;
                // Even after locally requesting abort, consume whichever server event arrives first.
                // Both peers see Commit/Abort on the same reliable channel, including the sender.
                _pending = null;
                Session = p.Id; // Undo also gets a fresh epoch, rejecting delayed pre-undo moves.
                Status?.Invoke(p.Kind == "restart" ? "双方已提交重新开始" : "双方已提交悔棋");
                Resolved?.Invoke(p.Kind, true);
                break;
            case RequestWireKind.Abort:
                _pending = null;
                Status?.Invoke("请求已取消或超时，棋局保持原样");
                Resolved?.Invoke(p.Kind, false);
                break;
        }
    }

    internal void Tick()
    {
        if (_pending is not { } p || !Expired(p)) return;
        if (p.Stage == Stage.AwaitAbort) { Fail("请求同步超时，已结束联机；当前棋谱保留"); return; }
        Abort(p, p.Stage == Stage.Deciding ? "请求超时，等待双方取消" : "提交等待超时，正在取消请求");
    }
    private void Abort(Pending p, string reason)
    {
        _pending = p with { Stage = Stage.AwaitAbort, Started = _clock.GetTimestamp() };
        if (!send(Wire(RequestWireKind.Abort, p), true)) { Fail("取消发送失败，已结束联机；当前棋谱保留"); return; }
        Status?.Invoke(reason);
    }
    private void Fail(string reason)
    {
        _pending = null; Status?.Invoke(reason); Failed?.Invoke();
    }
}
