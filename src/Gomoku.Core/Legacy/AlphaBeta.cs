// Ported from Liss v2.3: algorithm structure retained; safety fixes for v3.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 五子棋
{
    class AlphaBeta : SearchEngine
    {
        public override string name
        {
            get
            {
               return "基于负极大值结构的αβ剪枝搜索引擎";
            }
        }

        StoneMove bestMove;
        
        /// <summary>
        /// 负数极大值αβ剪枝搜索引擎
        /// </summary>
        /// <param name="ply"></param>
        /// <param name="alpha"></param>
        /// <param name="beta"></param>
        int alphaBeta(int ply,int alpha,int beta)
        {
            CheckBudget();
            if (TryTerminal(nowBlack, out int terminalScore)) return terminalScore;
            int score;
            if (ply<=0)
            {
                score = iEvaluation.evaluate(board,nowBlack);
                nodeCount++;
                return score;
            }
            StoneMove[] moves = iMoveGenerator.allLegalMove(board);
            
            for (int i = 0; i < moves.Length; i++)
            {
                putStone(moves[i]);
                score = -alphaBeta(ply - 1, -beta, -alpha);
                delStone(moves[i]);

                if (score>alpha)
                {
                    alpha = score;
                    if (ply == searchDepth)
                    {
                        bestMove = moves[i];
                    }
                }
                if (alpha >= beta)
                {
                    break;//剪枝
                }
            }
            return alpha;//返回极大值
        }

        protected override StonePosition getResultFromEngine()
        {
            alphaBeta(searchDepth, -1100000, 1100000);
            return bestMove.Pos;
        }
    }
}
