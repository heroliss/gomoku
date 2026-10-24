// Ported from Liss v2.3: algorithm structure retained; safety fixes for v3.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 五子棋
{
    class NegaScout_HH_TT : SearchEngine
    {
        public override string name
        {
            get
            {
                return "结合历史启发和置换表的PVS/NegaScout";
            }
        }
        HistoryHeuristic hh;
        TranspositionTable tt;

        protected override StonePosition getResultFromEngine()
        {
            int gridNum = board.GetLength(0);
            hh = new HistoryHeuristic(gridNum);
            tt = new TranspositionTable(gridNum);
            tt.CalculateInitHashKey(board);//初始化棋盘哈希值  
            negaScout(searchDepth, int.MinValue+100000, int.MaxValue-100000);
            return bestMove.Pos;
        }
        StoneMove bestMove;
        int negaScout(int depth,int alpha,int beta)
        {
            CheckBudget();
            if (TryTerminal(nowBlack, out int terminalScore)) return terminalScore;
            int score;
            int? scoreTT = tt.lookUpHashTable(alpha, beta, depth, nowBlack);
            if (scoreTT!=null)
            {
                hitTTcount++;
                return (int)scoreTT;//命中，直接返回估值
            }

            if (depth<=0)
            {
                nodeCount++;
                score=iEvaluation.evaluate(board, nowBlack);
                tt.enterHashTable(EntryType.Exact, score, depth, nowBlack);
                return score;
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

            int bestMoveIndex=0;
            int a = alpha, b = beta;
            int t;
            bool evalIsExact = false;

            for (int i = 0; i < moves.Length; i++)
            {
                tt.hash_MakeMove(moves[i].Pos, nowBlack);//产生子节点哈希值
                putStone(moves[i]);//产生子节点

                t = - negaScout(depth - 1, -b, -a);
                if (t > a && t < beta && i > 0) 
                {//对于第一个后的节点，如果上面的搜索fail high
                    a = -negaScout(depth - 1, -beta, -t);//重新搜索
                    evalIsExact = true;//设置数据类型为精确值
                    if (depth==searchDepth)
                    {
                        bestMove = moves[i];
                    }
                    bestMoveIndex = i;//记住最佳走法的索引
                }
                
                //撤销子节点
                delStone(moves[i]);
                //恢复当前结点的哈希值
                tt.hash_UnMakMove(moves[i].Pos, nowBlack);

                if (a < t) 
                {//第一次搜索命中
                    evalIsExact = true;
                    a = t;
                    bestMoveIndex = i;
                    if (depth==searchDepth)
                    {
                        bestMove = moves[i];
                    }
                }
                if (a>=beta)
                {
                    //将下边界存入置换表
                    tt.enterHashTable(EntryType.LowerBound, a, depth, nowBlack);
                    //将当前走法汇入历史记录
                    hh.enterHistoryScore(moves[i].Pos, depth);
                    return a;//beta剪枝
                }
                b = a + 1;//设定新的空窗     
            }
            //将最佳走法汇入历史记录
            hh.enterHistoryScore(moves[bestMoveIndex].Pos, depth);
            //将搜索结果放入置换表
            if (evalIsExact)
            {
                tt.enterHashTable(EntryType.Exact, a, depth, nowBlack);
            }
            else
            {
                tt.enterHashTable(EntryType.UpperBound, a, depth, nowBlack);
            }
            return a;
        }
    }
}
