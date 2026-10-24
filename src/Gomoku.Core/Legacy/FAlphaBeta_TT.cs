// Ported from Liss v2.3: algorithm structure retained; safety fixes for v3.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 五子棋
{
    class FAlphaBeta_TT : SearchEngine //置换表对于五子棋无效
    {
        TranspositionTable tt;
        protected StoneMove bestMove;
        public override string name
        {
            get
            {
                return "结合置换表的Fail-softαβ搜索引擎";
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
            int? scoreTT;

            scoreTT = tt.lookUpHashTable(alpha, beta, ply, nowBlack);//在置换表中查找该节点
            if (scoreTT!=null)
            {
                hitTTcount++;
                return (int)scoreTT;
            }
            if (ply <= 0)
            {
                score = iEvaluation.evaluate(board, nowBlack);
                //将求得的估值放入置换表
                tt.enterHashTable(EntryType.Exact, score,ply, nowBlack);
                nodeCount++;
                return score;
            }
            StoneMove[] moves = iMoveGenerator.allLegalMove(board);
            bool evalIsExact = false;//数据类型标志
            for (int i = 0; i < moves.Length; i++)
            {
                tt.hash_MakeMove(moves[i].Pos, nowBlack); //这里的顺序是个问题
                putStone(moves[i]);
                score = -FalphaBeta(ply - 1, -beta, -alpha);
                delStone(moves[i]);
                tt.hash_UnMakMove(moves[i].Pos, nowBlack);

                if (score > alpha)
                {
                    alpha = score;//取最大值
                    evalIsExact = true;
                    if (ply == searchDepth)
                    {
                        bestMove = moves[i];
                    }
                }
                if (alpha >= beta)
                {//beta剪枝
                    tt.enterHashTable(EntryType.LowerBound, score, ply, nowBlack);
                    return score;//返回下边界
                }
            }
            if (evalIsExact)
            {
                tt.enterHashTable(EntryType.Exact, alpha, ply, nowBlack);
            }
            else
            {
                tt.enterHashTable(EntryType.UpperBound, alpha, ply, nowBlack);
            }
            return alpha;//返回极大值或上界
        }

        protected override StonePosition getResultFromEngine()
        {
            tt = new TranspositionTable(board.GetLength(0));
            tt.CalculateInitHashKey(board);//计算初始棋盘的哈希值
            FalphaBeta(searchDepth, int.MinValue + 100000, int.MaxValue - 100000);
            return bestMove.Pos;
        }
    }
}
