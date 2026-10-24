// Ported from Liss v2.3: algorithm structure retained; safety fixes for v3.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 五子棋
{
    class MyComparer : IComparer<StoneMove>
    {
        public int Compare(StoneMove x, StoneMove y)
        {
            return -x.Score.CompareTo(y.Score);
        }
    }
    /// <summary>
    /// 历史启发类
    /// </summary>
    class HistoryHeuristic
    {

        int[,] historyTable;//历史得分表
        public HistoryHeuristic(int gridNum)
        {
            historyTable = new int[gridNum, gridNum];
        }
        public int getHistoryScore(StonePosition move)
        {
            return historyTable[move.X, move.Y];
        }
        public void enterHistoryScore(StonePosition move,int depth)
        {
            historyTable[move.X, move.Y] = Math.Min(1_000_000, historyTable[move.X, move.Y] + (1 << Math.Min(depth, 16)));//增量为2的depth次方
        }
    }
}
