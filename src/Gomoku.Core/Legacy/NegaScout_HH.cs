// Ported from Liss v2.3: algorithm structure retained; safety fixes for v3.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 五子棋
{
    class NegaScout_HH : SearchEngine
    {
        public override string name
        {
            get
            {
                return "结合历史启发的PVS/NegaScout";
            }
        }
        HistoryHeuristic hh;

        protected override StonePosition getResultFromEngine()
        {
            int gridNum = board.GetLength(0);
            hh = new HistoryHeuristic(gridNum);

            negaScout(searchDepth, int.MinValue+100000, int.MaxValue-100000);
            return bestMove.Pos;
        }
        StoneMove bestMove;
        int negaScout(int depth,int alpha,int beta)
        {
            CheckBudget();
            if (TryTerminal(nowBlack, out int terminalScore)) return terminalScore;
            if (depth<=0)
            {
                nodeCount++;
                return iEvaluation.evaluate(board, nowBlack);
            }
            //产生所有走法
            StoneMove[] moves=iMoveGenerator.allLegalMove(board);
            //取所有走法的历史得分
            for (int i = 0; i < moves.Length; i++)
            {
                moves[i].Score = hh.getHistoryScore(moves[i].Pos);
            }
            //将走法按历史得分排序
            Array.Sort(moves, new MyComparer());

            int bestMoveIndex=0;//原来是-1
            int a = alpha, b = beta;
            int t;

            for (int i = 0; i < moves.Length; i++)
            {
                putStone(moves[i]);//产生子节点

                t = - negaScout(depth - 1, -b, -a);
                if (t > a && t < beta && i > 0) 
                {//对于第一个后的节点，如果上面的搜索fail high
                    a = -negaScout(depth - 1, -beta, -t);//重新搜索

                    if (depth==searchDepth)
                    {
                        bestMove = moves[i];
                    }
                    bestMoveIndex = i;//记住最佳走法的索引
                }
                
                //撤销子节点
                delStone(moves[i]);

                if (a < t) 
                {
                    a = t;
                    bestMoveIndex = i;
                    if (depth==searchDepth)
                    {
                        bestMove = moves[i];
                    }
                }
                if (a>=beta)
                {
                    //将当前走法汇入历史记录
                    hh.enterHistoryScore(moves[i].Pos, depth);
                    return a;//beta剪枝
                }
                b = a + 1;//设定新的空窗     
            }
            //将最佳走法汇入历史记录
            hh.enterHistoryScore(moves[bestMoveIndex].Pos, depth);

            return a;
        }
    }
}
