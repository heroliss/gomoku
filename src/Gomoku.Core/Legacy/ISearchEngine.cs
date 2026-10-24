// Ported from Liss v2.3: algorithm structure retained; safety fixes for v3.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 五子棋
{
    public interface ISearchEngine
    {
         StonePosition search(Stone[,] board,bool nowBlack);
         void init(IEvaluation iEvaluation, IMoveGenerator iMoveGenerator, int searchDepth,bool autoFive=true);
         string name { get; }
        int NodeCount { get; }//遍历节点数
        int HitTTCount { get; }//置换表命中次数
    }
}
