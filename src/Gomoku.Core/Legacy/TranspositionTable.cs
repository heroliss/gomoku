namespace 五子棋;

/// <summary>Fixed-size per-search Zobrist table. Full keys prevent index collisions being treated as hits.</summary>
sealed class TranspositionTable
{
    private readonly ulong[,,] keys;
    private readonly Entry[] entries = new Entry[1 << 16];
    private ulong hash;
    private readonly record struct Entry(ulong Key, bool Black, int Depth, int Value, EntryType Type, bool Valid);
    public TranspositionTable(int size)
    {
        keys = new ulong[3, size, size];
        ulong state = 0x9e3779b97f4a7c15UL;
        for (int color = 1; color <= 2; color++) for (int x = 0; x < size; x++) for (int y = 0; y < size; y++)
            keys[color, x, y] = Next(ref state);
    }
    internal static ulong Next(ref ulong state)
    { state += 0x9e3779b97f4a7c15UL; ulong z = state; z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL; z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL; return z ^ (z >> 31); }
    public void CalculateInitHashKey(Stone[,] board)
    {
        hash = 0;
        for (int x = 0; x < board.GetLength(0); x++) for (int y = 0; y < board.GetLength(1); y++)
            if (board[x, y] != Stone.空) hash ^= keys[(int)board[x, y], x, y];
    }
    public void hash_MakeMove(StonePosition move, bool black) => hash ^= keys[black ? 1 : 2, move.X, move.Y];
    public void hash_UnMakMove(StonePosition move, bool black) => hash_MakeMove(move, black);
    private int Index(bool black) => (int)((hash ^ (black ? 0x68e31da4UL : 0xb5297a4dUL)) & (ulong)(entries.Length - 1));
    public int? lookUpHashTable(int alpha, int beta, int depth, bool black)
    {
        Entry e = entries[Index(black)];
        if (!e.Valid || e.Key != hash || e.Black != black || e.Depth < depth) return null;
        return e.Type == EntryType.Exact || e.Type == EntryType.LowerBound && e.Value >= beta || e.Type == EntryType.UpperBound && e.Value <= alpha ? e.Value : null;
    }
    public void enterHashTable(EntryType type, int value, int depth, bool black)
    {
        int index = Index(black);
        if (!entries[index].Valid || entries[index].Key != hash || depth >= entries[index].Depth)
            entries[index] = new Entry(hash, black, depth, value, type, true);
    }
}
