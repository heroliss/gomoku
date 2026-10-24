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
    /// 负极大值搜索引擎
    /// </summary>
    class NegaMax : SearchEngine
    {
        public override string name
        {
            get
            {
                return "负极大值搜索引擎";
            }
        }
        
        StoneMove bestMove;



        int negaMaxSearch( int depth, bool nowBlack/*,StonePosition currentStone*/)
        {
            CheckBudget();
            if (TryTerminal(nowBlack, out int terminalScore)) return terminalScore;
            int value;
            if (depth <= 0)//如果depth小于等于0
            {
                value = iEvaluation.evaluate(board,nowBlack);//调用评估函数，并将结果赋给value
                nodeCount++;
                return value;//返回value值
            }
            StoneMove[] moves;//= new StoneMove[255];
            int bestVlaue = int.MinValue;                //都是极大点
            moves = this.iMoveGenerator.allLegalMove(board);

            for (int i = 0; i < moves.Length; i++)//对于每一步走法
            {
                board[moves[i].Pos.X, moves[i].Pos.Y] = nowBlack ? Stone.黑子 : Stone.白子;// 执行走法
                //currentStone = moves[i].Pos;
                value = -negaMaxSearch(depth - 1, !nowBlack);            //注意函数之前有负号
                board[moves[i].Pos.X, moves[i].Pos.Y] = Stone.空;//撤销走法
                
                if (value > bestVlaue)
                {
                    bestVlaue = value;
                    if (depth == searchDepth)
                    {
                        bestMove = moves[i];
                    }
                }
            }

            return bestVlaue; //返回某个搜索分支的最优评估值

        }

        protected override StonePosition getResultFromEngine()
        {
            negaMaxSearch(searchDepth, nowBlack);
            return bestMove.Pos;
        }
    }
}
