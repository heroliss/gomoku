// Ported from Liss v2.3: algorithm structure retained; safety fixes for v3.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 五子棋
{
    class FAlphaBeta : SearchEngine
    {
        protected StoneMove bestMove;
        public override string name
        {
            get
            {
                return "Fail-softαβ搜索引擎";
            }
        }

        

        /// <summary>
        /// Fail-soft αβ剪枝搜索引擎
        /// </summary>
        /// <param name="ply"></param>
        /// <param name="alpha"></param>
        /// <param name="beta"></param>
        protected int FalphaBeta(int ply, int alpha, int beta)
        {
            CheckBudget();
            if (TryTerminal(nowBlack, out int terminalScore)) return terminalScore;
            int current = int.MinValue;
            int score;
            if (ply <= 0)
            {
                score = iEvaluation.evaluate(board, nowBlack);
                nodeCount++;
                return score;
            }
            StoneMove[] moves = iMoveGenerator.allLegalMove(board);

            for (int i = 0; i < moves.Length; i++)
            {
                putStone(moves[i]);
                score = -FalphaBeta(ply - 1, -beta, -alpha);
                delStone(moves[i]);
                if (score > current)
                {
                    current = score;//保留极大值
                    if (ply == searchDepth)
                    {
                        bestMove = moves[i];
                    }
                    if (score >= alpha)
                    {
                        alpha = score;//修改alpha边界
                    }
                    if (alpha >= beta)
                    {
                        break;//剪枝
                    }
                }

            }
            return current;//返回极大值或边界值
        }

        protected override StonePosition getResultFromEngine()
        {
            FalphaBeta(searchDepth, int.MinValue + 100000, int.MaxValue - 100000);
            return bestMove.Pos;
        }
    }
}
