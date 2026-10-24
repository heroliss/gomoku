// Ported from Liss v2.3: algorithm structure retained; safety fixes for v3.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 五子棋
{
    class PVS : SearchEngine
    {
        public override string name
        {
            get
            {
                return "PVS/极小窗口搜索/NegaScout";
            }
        }


        protected override StonePosition getResultFromEngine()
        {
            PrincipalVariation(searchDepth, int.MinValue+100000, int.MaxValue-100000);
            return bestMove.Pos;
        }
        StoneMove bestMove;
        int PrincipalVariation(int depth,int alpha,int beta)
        {
            CheckBudget();
            if (TryTerminal(nowBlack, out int terminalScore)) return terminalScore;
            if (depth<=0)
            {
                nodeCount++;
                return iEvaluation.evaluate(board, nowBlack);
            }
            StoneMove[] moves=iMoveGenerator.allLegalMove(board);
            //产生第一个节点
            putStone(moves[0]);
            //全局窗口搜索第一个节点
            int bestValue = -PrincipalVariation(depth - 1, -beta, -alpha);
            delStone(moves[0]);
            if (depth==searchDepth)//搜索至根节点保存最佳走法
            {
                bestMove = moves[0];
            }
            int value;
            for (int i = 1; i < moves.Length; i++)
            {
                if (bestValue<beta)//如果不能被beta剪枝
                {
                    if (bestValue>alpha)
                    {
                        alpha = bestValue;
                    }
                    putStone(moves[i]);//产生子节点
                    //空窗探测
                    value = -PrincipalVariation(depth - 1, -alpha - 1, -alpha);
                    //如果探测结果为fail-high，重新搜索
                    if (value>alpha&&value<beta)
                    {
                        bestValue = -PrincipalVariation(depth - 1, -beta, -value);
                        if (depth==searchDepth)
                        {
                            bestMove = moves[i];
                        }
                    }
                    else if (value>bestValue)
                    {
                        bestValue = value;//窄窗搜索命中
                        if (depth==searchDepth)
                        {
                            bestMove = moves[i];
                        }
                    }
                    delStone(moves[i]);
                }        
            }
            return bestValue;
        }
    }
}
