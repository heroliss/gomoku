using System.Collections.ObjectModel;
using 五子棋;

namespace Liss.Gomoku.Core;

public readonly record struct Move(int X, int Y);

/// <summary>Freestyle Gomoku: black starts; an unbroken line of at least five wins.</summary>
public sealed class GameBoard
{
    private readonly List<Move> moves = [];
    private readonly ReadOnlyCollection<Move> readOnlyMoves;
    private IReadOnlyList<Move> winningLine = Array.Empty<Move>();
    public GameBoard(int size = 15)
    {
        if (size is < 5 or > 25) throw new ArgumentOutOfRangeException(nameof(size), "Board size must be between 5 and 25.");
        Size = size;
        Cells = new Stone[size, size];
        readOnlyMoves = moves.AsReadOnly();
    }
    public int Size { get; }
    /// <summary>Read-only by convention for rendering. Change the position through TryPlay/Undo/Redo.</summary>
    public Stone[,] Cells { get; }
    public IReadOnlyList<Move> Moves => readOnlyMoves;
    public int Cursor { get; private set; }
    public bool BlackToMove => Cursor % 2 == 0;
    public Stone Winner { get; private set; }
    public bool IsDraw => Winner == Stone.空 && Cursor == Size * Size;
    public bool IsFinished => Winner != Stone.空 || IsDraw;
    public IReadOnlyList<Move> WinningLine => winningLine;

    public bool TryPlay(int x, int y)
    {
        if ((uint)x >= Size || (uint)y >= Size || IsFinished || Cells[x, y] != Stone.空) return false;
        if (Cursor < moves.Count) moves.RemoveRange(Cursor, moves.Count - Cursor);
        var move = new Move(x, y);
        Cells[x, y] = BlackToMove ? Stone.黑子 : Stone.白子;
        moves.Add(move);
        Cursor++;
        UpdateWinner(move);
        return true;
    }
    public bool Undo(int steps = 1)
    {
        if (steps <= 0 || Cursor == 0) return false;
        int stop = Math.Max(0, Cursor - steps);
        while (Cursor > stop) { var move = moves[--Cursor]; Cells[move.X, move.Y] = Stone.空; }
        Winner = Stone.空;
        winningLine = Array.Empty<Move>();
        return true;
    }
    public bool Redo()
    {
        if (Cursor >= moves.Count || IsFinished) return false;
        var move = moves[Cursor];
        Cells[move.X, move.Y] = BlackToMove ? Stone.黑子 : Stone.白子;
        Cursor++;
        UpdateWinner(move);
        return true;
    }
    public void Reset()
    {
        Array.Clear(Cells);
        moves.Clear();
        Cursor = 0;
        Winner = Stone.空;
        winningLine = Array.Empty<Move>();
    }
    public GameBoard Clone()
    {
        var copy = new GameBoard(Size);
        Array.Copy(Cells, copy.Cells, Cells.Length);
        copy.moves.AddRange(moves);
        copy.Cursor = Cursor;
        copy.Winner = Winner;
        copy.winningLine = winningLine.ToArray();
        return copy;
    }
    private void UpdateWinner(Move move)
    {
        Stone stone = Cells[move.X, move.Y];
        foreach (var (dx, dy) in BoardRules.Directions)
        {
            var line = new List<Move> { move };
            for (int sign = -1; sign <= 1; sign += 2)
                for (int step = 1; ; step++)
                {
                    int x = move.X + dx * sign * step, y = move.Y + dy * sign * step;
                    if ((uint)x >= Size || (uint)y >= Size || Cells[x, y] != stone) break;
                    line.Add(new Move(x, y));
                }
            if (line.Count < 5) continue;
            Winner = stone;
            winningLine = line.OrderBy(p => p.X * Size + p.Y).ToArray();
            return;
        }
    }
}

internal static class BoardRules
{
    internal static readonly (int X, int Y)[] Directions = [(1, 0), (0, 1), (1, 1), (1, -1)];
    internal static bool HasFive(Stone[,] cells, int x, int y)
    {
        int size = cells.GetLength(0);
        Stone stone = cells[x, y];
        if (stone == Stone.空) return false;
        foreach (var (dx, dy) in Directions)
        {
            int count = 1;
            for (int sign = -1; sign <= 1; sign += 2)
                for (int n = 1; ; n++)
                {
                    int xx = x + dx * n * sign, yy = y + dy * n * sign;
                    if ((uint)xx >= size || (uint)yy >= size || cells[xx, yy] != stone) break;
                    count++;
                }
            if (count >= 5) return true;
        }
        return false;
    }
}
