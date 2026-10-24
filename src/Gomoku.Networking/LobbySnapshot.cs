namespace Liss.Gomoku.Networking;

public enum LobbyPhase
{
    Disconnected,
    Connecting,
    Lobby,
    CreatingRoom,
    JoiningRoom,
    RoomWaiting,
    Match,
    LeavingRoom,
    Disconnecting,
    Error
}

public enum LobbyPendingOperation
{
    None,
    CreateRoom,
    JoinRoom
}

/// <summary>An atomic, UI-independent view of the lobby workflow.</summary>
public sealed record LobbySnapshot(
    LobbyPhase Phase,
    string Message,
    string RoomName,
    string PendingRoomName,
    LobbyPendingOperation PendingOperation,
    bool CanConnect,
    bool CanCancel,
    bool CanCreateOrJoin,
    bool CanLeaveRoom);
