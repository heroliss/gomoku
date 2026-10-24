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
    /// 渴望搜索引擎
    /// </summary>
    class AspirationSearch : FAlphaBeta
    {
        public override string name
        {
            get
            {
                return "渴望搜索（Fail-soft αβ的改进算法）";
            }
        }
        protected override StonePosition getResultFromEngine()
        {
            const int window = 500;
            //先进行浅层搜索,猜测目标值范围
            int x = FalphaBeta(searchDepth-1, -1100000, 1100000);
            //对目标值附近的小窗口进行搜索
            int y = FalphaBeta(searchDepth, x - window, x + window);
            if (y<=x-window)//fail-low re-search
            {
                FalphaBeta(searchDepth, -1100000, y);
            }
            if (y>=x+window)//fail-high re-search
            {
                FalphaBeta(searchDepth, y, 1100000);
            }
            
            /*int alpha = x - window;
            int beta = x + window;
            int score;

            for (int i = 0; i < length; i++)
            {
                score = base.FalphaBeta(depth, alpha, beta);
                if (score <= alpha)
                {
                    alpha = -win;
                }
                else if (score >= beta)
                {
                    beta = win;
                }
                else
                    break;
    
            }*/
            return bestMove.Pos;
        }

    }
}
