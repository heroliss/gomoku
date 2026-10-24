// Ported from Liss v2.3: algorithm structure retained; safety fixes for v3.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 五子棋
{
    /// <summary>
    /// 走法生成器接口
    /// </summary>
    public interface IMoveGenerator
    {
        string name { get; }
        StoneMove[] allLegalMove(Stone[,] board);
    }
}
