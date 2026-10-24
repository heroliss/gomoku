namespace 五子棋
{
    public struct StonePosition
    {
        public StonePosition(byte x,byte y)
        {
            X = x;
            Y = y;
        }
        public byte X;
        public byte Y;
    }
    public struct StoneMove
    {
        public StoneMove(StonePosition pos,byte score)
        {
            Pos = pos;
            Score = score;
        }
        public StonePosition Pos;
        public int Score;
    }
/// <summary>
    /// 数据类型
    /// </summary>
    public enum EntryType
    {
        Exact,LowerBound,UpperBound
    }
    public struct HashItem
    {
        public ulong checkSum;//64位校验码
        public EntryType entryType;//数据类型
        public int depth;//取得此值时的层次
        public int eval;//节点的值
    }
}