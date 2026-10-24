// Ported from Liss v2.3: algorithm structure retained; safety fixes for v3.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 五子棋
{
    class FAlphaBeta_HH : SearchEngine //置换表对于五子棋无效
    {
        HistoryHeuristic hh;
        protected StoneMove bestMove;
        public override string name
        {
            get
            {
                return "结合历史启发的αβ搜索引擎";
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
            int score;

            if (ply <= 0)
            {
                score = iEvaluation.evaluate(board, nowBlack);
                nodeCount++;
                return score;
            }
            StoneMove[] moves = iMoveGenerator.allLegalMove(board);
            //取所有走法的历史得分
            for (int i = 0; i < moves.Length; i++)
            {
                moves[i].Score = hh.getHistoryScore(moves[i].Pos);
            }
            //对所有走法按历史得分排序
            Array.Sort(moves, new MyComparer());
            int bestMoveIndex=-1;

            for (int i = 0; i < moves.Length; i++)
            {
                putStone(moves[i]);
                score = -FalphaBeta(ply - 1, -beta, -alpha);
                delStone(moves[i]);
                if (score > alpha)
                {
                    alpha = score;//取最大值
                    if (ply == searchDepth)
                    {
                        bestMove = moves[i];
                    }
                    bestMoveIndex = i;//记录最佳走法位置
                }
                if (alpha >= beta)
                {
                    bestMoveIndex = i;//记录最佳走法位置
                    break;//剪枝
                } 
            }
            if (bestMoveIndex != -1)
            {
                hh.enterHistoryScore(moves[bestMoveIndex].Pos, ply);
            }
            return alpha;//返回极大值
        }

        protected override StonePosition getResultFromEngine()
        {
            int gridNum = board.GetLength(0);
            hh = new HistoryHeuristic(gridNum);
            FalphaBeta(searchDepth, int.MinValue + 100000, int.MaxValue - 100000);
            return bestMove.Pos;
        }
    }
}
