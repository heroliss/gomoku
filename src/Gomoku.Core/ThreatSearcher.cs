using 五子棋;

namespace Liss.Gomoku.Core;

/// <summary>
/// V3 engine: iterative deepening drives a principal-variation search. Forced wins and blocks
/// precede a ranked local frontier; a bounded Zobrist table, killer moves and history order it.
/// This is a selective Gomoku opponent, not an exhaustive proof solver or a Renju rules engine.
/// </summary>
internal sealed class ThreatSearcher
{
    internal const int Mate = 1_000_000;
    private readonly Stone[,] board;
    private readonly int size;
    private readonly SearchBudget budget;
    private readonly string evaluationId;
    private readonly Stone rootColor;
    private readonly ulong[,,] keys;
    private readonly ulong sideKey;
    private readonly Entry[] table = new Entry[1 << 16];
    private readonly int[,,] history;
    private readonly Move?[] killers = new Move?[40];
    private ulong hash;
    internal int Hits { get; private set; }
    private readonly record struct Entry(ulong Key, int Depth, int Score, byte Bound, Move Best, bool Valid);
    private readonly record struct Candidate(Move Move, int Rank);
    private static readonly int[] WindowValues = [0, 3, 45, 900, 25000, Mate];

    internal ThreatSearcher(Stone[,] board, bool black, SearchBudget budget, string evaluationId)
    {
        this.board = board; size = board.GetLength(0); this.budget = budget; this.evaluationId = evaluationId;
        rootColor = black ? Stone.黑子 : Stone.白子;
        history = new int[3, size, size];
        keys = new ulong[3, size, size];
        ulong state = 0x243f6a8885a308d3UL;
        for (int stone = 1; stone <= 2; stone++) for (int x = 0; x < size; x++) for (int y = 0; y < size; y++)
            keys[stone, x, y] = TranspositionTable.Next(ref state);
        sideKey = TranspositionTable.Next(ref state);
        for (int x = 0; x < size; x++) for (int y = 0; y < size; y++)
            if (board[x, y] != Stone.空) hash ^= keys[(int)board[x, y], x, y];
        if (!black) hash ^= sideKey;
    }

    internal (Move Move, int Score) Search(int depth, Move? previous)
    {
        budget.Check();
        var candidates = Generate(rootColor, depth, 0, previous);
        Move best = candidates[0].Move;
        int alpha = -Mate - 1;
        for (int i = 0; i < candidates.Count; i++)
        {
            Move move = candidates[i].Move;
            Make(move, rootColor);
            int score;
            try
            {
                if (BoardRules.HasFive(board, move.X, move.Y)) score = Mate - 1;
                else
                {
                    score = i == 0 ? -Negamax(depth - 1, -Mate - 1, -alpha, SearchHelpers.Opponent(rootColor), 1, move)
                        : -Negamax(depth - 1, -alpha - 1, -alpha, SearchHelpers.Opponent(rootColor), 1, move);
                    if (i > 0 && score > alpha) score = -Negamax(depth - 1, -Mate - 1, -alpha, SearchHelpers.Opponent(rootColor), 1, move);
                }
            }
            finally { Unmake(move, rootColor); }
            if (score > alpha) { alpha = score; best = move; }
        }
        Store(depth, alpha, 0, best);
        return (best, alpha);
    }

    private int Negamax(int depth, int alpha, int beta, Stone color, int ply, Move last)
    {
        budget.Visit();
        if (BoardRules.HasFive(board, last.X, last.Y)) return -Mate + ply;
        int originalAlpha = alpha;
        Entry cached = table[(int)(hash & (ulong)(table.Length - 1))];
        Move? preferred = null;
        if (cached.Valid && cached.Key == hash)
        {
            preferred = cached.Best;
            if (cached.Depth >= depth)
            {
                Hits++;
                // Mate scores depend on the current ply; normalise when moving in/out of the table.
                int score = Decode(cached.Score, ply);
                if (cached.Bound == 0 || cached.Bound == 1 && score >= beta || cached.Bound == 2 && score <= alpha) return score;
            }
        }
        if (depth <= 0) return Evaluate(color);
        List<Candidate> candidates = Generate(color, depth, ply, preferred);
        if (candidates.Count == 0) return 0;
        int bestScore = -Mate - 1;
        Move best = candidates[0].Move;
        for (int i = 0; i < candidates.Count; i++)
        {
            Move move = candidates[i].Move;
            Make(move, color);
            int score;
            try
            {
                score = i == 0 ? -Negamax(depth - 1, -beta, -alpha, SearchHelpers.Opponent(color), ply + 1, move)
                    : -Negamax(depth - 1, -alpha - 1, -alpha, SearchHelpers.Opponent(color), ply + 1, move);
                if (i > 0 && score > alpha && score < beta) score = -Negamax(depth - 1, -beta, -alpha, SearchHelpers.Opponent(color), ply + 1, move);
            }
            finally { Unmake(move, color); }
            if (score > bestScore) { bestScore = score; best = move; }
            alpha = Math.Max(alpha, score);
            if (alpha >= beta)
            {
                if (ply < killers.Length) killers[ply] = move;
                history[(int)color, move.X, move.Y] = Math.Min(50000, history[(int)color, move.X, move.Y] + depth * depth);
                break;
            }
        }
        Store(depth, Encode(bestScore, ply), bestScore >= beta ? (byte)1 : bestScore <= originalAlpha ? (byte)2 : (byte)0, best);
        return bestScore;
    }

    private List<Candidate> Generate(Stone color, int depth, int ply, Move? preferred)
    {
        var candidates = new List<Candidate>(100);
        var wins = new List<Candidate>();
        var blocks = new List<Candidate>();
        Stone other = SearchHelpers.Opponent(color);
        for (int x = 0; x < size; x++) for (int y = 0; y < size; y++)
        {
            budget.Check();
            if (board[x, y] != Stone.空 || !SearchHelpers.HasNeighbor(board, x, y, 2)) continue;
            var move = new Move(x, y);
            int own = PointScore(move, color), opponent = PointScore(move, other);
            int rank = own * 2 + opponent;
            if (own >= Mate) wins.Add(new Candidate(move, rank));
            if (opponent >= Mate) blocks.Add(new Candidate(move, rank));
            if (move == preferred) rank += 4_000_000;
            if (ply < killers.Length && move == killers[ply]) rank += 1000;
            rank += history[(int)color, x, y] - (Math.Abs(x - size / 2) + Math.Abs(y - size / 2));
            candidates.Add(new Candidate(move, rank));
        }
        if (wins.Count > 0) return wins;
        if (blocks.Count > 0) return blocks;
        candidates.Sort((a, b) => { int score = b.Rank.CompareTo(a.Rank); return score != 0 ? score : (a.Move.X * size + a.Move.Y).CompareTo(b.Move.X * size + b.Move.Y); });
        // Explicit selective width keeps the engine responsive. All forced wins/blocks bypass this cap.
        int limit = ply == 0 ? 24 : depth >= 3 ? 12 : 16;
        if (candidates.Count > limit) candidates.RemoveRange(limit, candidates.Count - limit);
        return candidates;
    }

    private int PointScore(Move move, Stone color)
    {
        board[move.X, move.Y] = color;
        try
        {
            if (BoardRules.HasFive(board, move.X, move.Y)) return Mate;
            int result = 0, fours = 0, openThrees = 0;
            foreach (var (dx, dy) in BoardRules.Directions)
            {
                int same = 1, open = 0;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    int step = 1;
                    while (true)
                    {
                        int x = move.X + dx * step * sign, y = move.Y + dy * step * sign;
                        if ((uint)x >= size || (uint)y >= size) break;
                        if (board[x, y] == color) { same++; step++; continue; }
                        if (board[x, y] == Stone.空) open++;
                        break;
                    }
                }
                result += (same, open) switch { (4, 2) => 85000, (4, 1) => 18000, (3, 2) => 5500, (3, 1) => 450, (2, 2) => 180, (2, 1) => 20, _ => 0 };
                if (same == 4 && open > 0) fours++;
                if (same == 3 && open == 2) openThrees++;
                // Five-cell windows also recognise jump fours and broken threes.
                for (int offset = -4; offset <= 0; offset++)
                {
                    int startX = move.X + dx * offset, startY = move.Y + dy * offset;
                    if ((uint)startX >= size || (uint)startY >= size || (uint)(startX + dx * 4) >= size || (uint)(startY + dy * 4) >= size) continue;
                    int count = 0; bool blocked = false;
                    for (int n = 0; n < 5; n++)
                    {
                        Stone s = board[startX + dx * n, startY + dy * n];
                        if (s == color) count++; else if (s != Stone.空) { blocked = true; break; }
                    }
                    if (!blocked) result += WindowValues[count] / 2;
                }
            }
            if (fours >= 2 || fours > 0 && openThrees > 0) result += 100000;
            if (openThrees >= 2) result += 25000;
            return Math.Min(400000, result);
        }
        finally { board[move.X, move.Y] = Stone.空; }
    }

    private int Evaluate(Stone color)
    {
        if (evaluationId == "quintuple") return new EvaluationQuintuple().evaluate(board, color == Stone.黑子);
        long score = 0;
        foreach (var (dx, dy) in BoardRules.Directions)
            for (int x = 0; x < size; x++) for (int y = 0; y < size; y++)
            {
                if ((uint)(x + dx * 4) >= size || (uint)(y + dy * 4) >= size) continue;
                int own = 0, other = 0;
                for (int n = 0; n < 5; n++) { Stone s = board[x + dx * n, y + dy * n]; if (s == color) own++; else if (s != Stone.空) other++; }
                if (own > 0 && other > 0) continue;
                int count = Math.Max(own, other);
                if (count == 0) continue;
                Stone owner = own > 0 ? color : SearchHelpers.Opponent(color);
                int beforeX = x - dx, beforeY = y - dy, afterX = x + dx * 5, afterY = y + dy * 5;
                int open = 0;
                if ((uint)beforeX < size && (uint)beforeY < size && board[beforeX, beforeY] == Stone.空) open++;
                if ((uint)afterX < size && (uint)afterY < size && board[afterX, afterY] == Stone.空) open++;
                int value = WindowValues[count] * (open + 1);
                score += owner == color ? value : -value;
            }
        return (int)Math.Clamp(score, -500_000L, 500_000L);
    }
    private static int Encode(int score, int ply) => score > Mate - 100 ? score + ply : score < -Mate + 100 ? score - ply : score;
    private static int Decode(int score, int ply) => score > Mate - 100 ? score - ply : score < -Mate + 100 ? score + ply : score;
    private void Store(int depth, int score, byte bound, Move best)
    {
        int index = (int)(hash & (ulong)(table.Length - 1));
        if (!table[index].Valid || table[index].Key != hash || depth >= table[index].Depth) table[index] = new Entry(hash, depth, score, bound, best, true);
    }
    private void Make(Move move, Stone color) { board[move.X, move.Y] = color; hash ^= keys[(int)color, move.X, move.Y] ^ sideKey; }
    private void Unmake(Move move, Stone color) { board[move.X, move.Y] = Stone.空; hash ^= keys[(int)color, move.X, move.Y] ^ sideKey; }
}
