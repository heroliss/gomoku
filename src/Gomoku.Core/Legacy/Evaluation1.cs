// Ported from Liss v2.3: algorithm structure retained; safety fixes for v3.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 五子棋
{
    class Evaluation1 : IEvaluation
    {
        int gridNum;
        //棋盘位置价值表
        int[,] PosValue;

        //历史启发数组
        public int[,] HistoryHeuristic;
        //存放分析结果,已分析(-1),眠二(1),眠三(2),冲四(3),活二(4),活三(5),活四(6),五连(7)
        private int[,,] TypeRecord;
        private int[] LineRecord;
        private int[,] TypeCount = new int[3, 10];
        public Evaluation1(int gridNum)
        {
            this.gridNum = gridNum;
            LineRecord = new int[gridNum];
            PosValue = CreatePosValueTable(gridNum);
            HistoryHeuristic = new int[gridNum, gridNum];
            TypeRecord = new int[gridNum, gridNum, 4];
        }

        private int[,] CreatePosValueTable(int gridNum)
        {
            int[,] posValue = new int[gridNum,gridNum];
            double maxDistance = (double)Math.Sqrt(2.0 * gridNum * 10 / 2.0 * gridNum * 10 / 2.0);
            double mid = gridNum * 10 / 2.0 ;
            for (int i = 0; i < gridNum; i++)
            {
                for (int j = 0; j < gridNum; j++)
                {
                    posValue[i, j] = (int)(maxDistance - Math.Sqrt(Math.Pow(i * 10 - mid, 2) + Math.Pow(j * 10 - mid, 2)));
                }
            }
            return posValue;
        }

        public string name
        {
            get
            {
                return "一般估值函数";
            }
        }

        public void ResetHistoryTable()
        {
            for (int i = 0; i < gridNum; i++)
            {
                for (int j = 0; j < gridNum; j++) HistoryHeuristic[i, j] = PosValue[i, j];
            }
        }
        /// <summary>
        /// 返回结果（1：胜利/2：平局/3：继续）
        /// </summary>
        /// <param name="i"></param>
        /// <param name="j"></param>
        /// <param name="Squares"></param>
        /// <param name="Count"></param>
        /// <returns></returns>
        public int Result(int i, int j, Stone[,] Squares, int Count)
        {
            //检查是否胜利
            if (AnalysisHorizon(Squares, i, j) == 7 || AnalysisVertical(Squares, i, j) == 7 || AnalysisLeft(Squares, i, j) == 7 || AnalysisRight(Squares, i, j) == 7)
                return 1;

            //检查是否平局
            if (Count == gridNum * gridNum) return 2;

            //继续
            return 3;
        }

        public int Eveluate(Stone[,] Squares, Stone CurrentStone)
        {
            //初始化
            for (int i = 0; i < gridNum; i++)
                for (int j = 0; j < gridNum; j++)
                    for (int k = 0; k < 4; k++) TypeRecord[i, j, k] = 0;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 10; j++) TypeCount[i, j] = 0;

            //对每个棋子进行分析
            for (int i = 0; i < gridNum; i++)
            {
                for (int j = 0; j < gridNum; j++)
                {
                    if (Squares[i, j] != 0)
                    {
                        if (TypeRecord[i, j, 0] == 0) AnalysisHorizon(Squares, i, j);
                        if (TypeRecord[i, j, 1] == 0) AnalysisVertical(Squares, i, j);
                        if (TypeRecord[i, j, 2] == 0) AnalysisLeft(Squares, i, j);
                        if (TypeRecord[i, j, 3] == 0) AnalysisRight(Squares, i, j);
                    }
                }
            }
            //对分析结果进行统计，得到每种棋型的数量
            for (int i = 0; i < gridNum; i++)
            {
                for (int j = 0; j < gridNum; j++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        if (Squares[i, j] != 0 && TypeRecord[i, j, k] > 0) TypeCount[(int)Squares[i, j], TypeRecord[i, j, k]]++;
                    }
                }
            }
            if (TypeCount[(int)CurrentStone, 7] > 0)//五连
            {
                return 99990;
            }
            if (TypeCount[3 - (int)CurrentStone, 7] > 0)//五连
            {
                return -99990;
            }
            for (int i = 1; i <= 2; i++) if ((int)TypeCount[i, 3] > 1) TypeCount[i, 6]++;//活四

            if (TypeCount[(int)CurrentStone, 6] > 0)//活四
            {
                return 99900;
            }

            if (TypeCount[(int)CurrentStone, 3] > 0)//冲四
            {
                return 99800;
            }

            if (TypeCount[3 - (int)CurrentStone, 6] > 0)//活四 
            {
                return -99700;
            }


            if (TypeCount[3 - (int)CurrentStone, 3] > 0 && TypeCount[3 - (int)CurrentStone, 5] > 0)//冲四且活三
            {
                return -99600;
            }

            if (TypeCount[(int)CurrentStone, 5] > 0 && TypeCount[3 - (int)CurrentStone, 3] == 0)//活三 冲四             
            {
                return 99500;
            }

            if ((int)TypeCount[3 - (int)CurrentStone, 5] > 1 &&
                TypeCount[(int)CurrentStone, 3] == 0 &&
                TypeCount[(int)CurrentStone, 5] == 0 &&
                TypeCount[(int)CurrentStone, 2] == 0)
            {
                return -99400;
            }

            int CValue = 0, PValue = 0;
            if ((int)TypeCount[(int)CurrentStone, 5] > 1)
                CValue += 2000;
            else
            {
                if (TypeCount[(int)CurrentStone, 5] > 0)
                    CValue += 200;
            }

            if ((int)TypeCount[3 - (int)CurrentStone, 5] > 1)
                PValue += 500;
            else
            {
                if (TypeCount[3 - (int)CurrentStone, 5] > 0)
                    PValue += 100;
            }


            if (TypeCount[(int)CurrentStone, 2] > 0)
                CValue += TypeCount[(int)CurrentStone, 2] * 10;

            if (TypeCount[3 - (int)CurrentStone, 2] > 0)
                PValue += TypeCount[3 - (int)CurrentStone, 2] * 10;

            if (TypeCount[(int)CurrentStone, 4] > 0)
                CValue += TypeCount[(int)CurrentStone, 4] * 4;

            if (TypeCount[3 - (int)CurrentStone, 4] > 0)
                PValue += TypeCount[3 - (int)CurrentStone, 4] * 4;

            if (TypeCount[(int)CurrentStone, 2] > 0)
                CValue += TypeCount[(int)CurrentStone, 2];

            if (TypeCount[3 - (int)CurrentStone, 2] > 0)
                PValue += TypeCount[3 - (int)CurrentStone, 2];

            for (int i = 0; i < gridNum; i++)
            {
                for (int j = 0; j < gridNum; j++)
                {
                    if (Squares[i, j] != 0)
                    {
                        if (Squares[i, j] == CurrentStone) CValue += PosValue[i, j];
                        else PValue += PosValue[i, j];
                    }
                }
            }
            return (CValue - PValue)*10;
        }

        private int AnalysisHorizon(Stone[,] Squares, int i, int j)
        {
            Stone[] Line = new Stone[gridNum];
            for (int s = 0; s < gridNum; s++) Line[s] = Squares[i, s];
            AnalysisLine(Line, gridNum, j);
            for (int s = 0; s < gridNum; s++) if (LineRecord[s] != 0) TypeRecord[i, s, 0] = LineRecord[s];
            return TypeRecord[i, j, 0];
        }

        private int AnalysisVertical(Stone[,] Squares, int i, int j)
        {
            Stone[] Line = new Stone[gridNum];
            for (int s = 0; s < gridNum; s++) Line[s] = Squares[s, j];
            AnalysisLine(Line, gridNum, i);
            for (int s = 0; s < gridNum; s++) if (LineRecord[s] != 0) TypeRecord[s, j, 1] = LineRecord[s];
            return TypeRecord[i, j, 1];
        }

        private int AnalysisLeft(Stone[,] Squares, int i, int j)
        {
            Stone[] Line = new Stone[gridNum];
            int x, y;
            if (i < j)
            {
                y = 0;
                x = j - i;
            }
            else
            {
                x = 0;
                y = i - j;
            }
            int k;
            for (k = 0; k < gridNum; k++)
            {
                if (x + k > (gridNum - 1) || y + k > (gridNum - 1))
                    break;
                Line[k] = Squares[y + k, x + k];
            }
            AnalysisLine(Line, k, j - x);
            for (int s = 0; s < k; s++)
            {
                if (LineRecord[s] != 0)
                    TypeRecord[y + s, x + s, 2] = LineRecord[s];

            }
            return TypeRecord[i, j, 2];
        }

        private int AnalysisRight(Stone[,] Squares, int i, int j)
        {
            Stone[] Line = new Stone[gridNum];
            int x, y, realnum;

            if ((gridNum - 1) - i < j)
            {
                y = (gridNum - 1);
                x = j - (gridNum - 1) + i;
                realnum = (gridNum - 1) - i;
            }
            else
            {
                x = 0;
                y = i + j;
                realnum = j;
            }
            int k;
            for (k = 0; k < gridNum; k++)
            {
                if (x + k > (gridNum - 1) || y - k < 0)
                    break;
                Line[k] = Squares[y - k, x + k];
            }
            AnalysisLine(Line, k, j - x);
            for (int s = 0; s < k; s++)
            {
                if (LineRecord[s] != 0)
                    TypeRecord[y - s, x + s, 3] = LineRecord[s];

            }
            return TypeRecord[i, j, 3];
        }

        //分析给定行上某点及其周边的棋型
        private int AnalysisLine(Stone[] Line, int Num, int Pos)
        {
            for (int i = 0; i < Num; i++) LineRecord[i] = 0;
            if (Num < 5)
            {
                for (int i = 0; i < Num; i++) LineRecord[i] = -1;
                return -1;
            }
            int LeftEdge, RightEdge;
            int LeftRange, RightRange;
            LeftEdge = RightEdge = Pos;
            while (LeftEdge > 0)
            {
                if (Line[LeftEdge - 1] != Line[Pos]) break;
                LeftEdge--;
            }
            while (RightEdge < Num - 1)
            {
                if (Line[RightEdge + 1] != Line[Pos]) break;
                RightEdge++;
            }
            LeftRange = LeftEdge;
            RightRange = RightEdge;
            while (LeftRange > 0)
            {
                if (Line[LeftRange - 1] == 3 - Line[Pos]) break;
                LeftRange--;
            }
            while (RightRange < Num - 1)
            {
                if (Line[RightRange + 1] == 3 - Line[Pos]) break;
                RightRange++;
            }
            if (RightRange - LeftRange < 4)
            {
                for (int k = LeftRange; k <= RightRange; k++)
                    LineRecord[k] = -1;
                return -1;
            }
            for (int k = LeftEdge; k <= RightEdge; k++) LineRecord[k] = -1;
            if (RightEdge - LeftEdge > 3)//五连
            {
                LineRecord[Pos] = 7;
                return 7;
            }
            if (RightEdge - LeftEdge == 3)//4连
            {
                if (LeftEdge == LeftRange || RightEdge == RightRange) LineRecord[Pos] = 3;
                else LineRecord[Pos] = 6;
                return LineRecord[Pos];
            }
            if (RightEdge - LeftEdge == 2)//3连
            {
                bool LeftThree = false;
                if (LeftEdge != LeftRange)
                {
                    if (LeftEdge > 1 && Line[LeftEdge - 2] == Line[LeftEdge])
                    {
                        LineRecord[LeftEdge] = 3;
                        LineRecord[LeftEdge - 2] = -1;
                    }
                    else LeftThree = true;
                }
                if (RightEdge != RightRange)
                {
                    if (RightEdge < Num - 2 && Line[RightEdge + 2] == Line[RightEdge])
                    {
                        LineRecord[RightEdge] = 3;
                        LineRecord[RightEdge + 2] = -1;
                    }
                    else
                    {
                        if (LeftThree) LineRecord[Pos] = 5;
                        else LineRecord[Pos] = 2;
                    }
                }
                else
                {
                    if (LineRecord[LeftEdge] == 3) return LineRecord[LeftEdge];
                    if (LeftThree) LineRecord[Pos] = 2;
                }
                return LineRecord[Pos];
            }
            if (RightEdge - LeftEdge == 1)//2连
            {
                bool Lefttwo = false;
                if (LeftEdge > 2)
                {
                    if (Line[LeftEdge - 1] == 0)
                    {
                        if (LeftEdge - 1 > 1 && Line[LeftEdge - 2] == Line[LeftEdge])
                        {
                            if (Line[LeftEdge - 3] == Line[LeftEdge])
                            {
                                LineRecord[LeftEdge - 3] = -1;
                                LineRecord[LeftEdge - 2] = -1;
                                LineRecord[LeftEdge] = 3;
                            }
                            else
                                if (Line[LeftEdge - 3] == 0)
                            {
                                LineRecord[LeftEdge - 2] = -1;
                                LineRecord[LeftEdge] = 2;
                            }
                        }
                        else
                            Lefttwo = true;
                    }
                }
                if (RightEdge < Num - 1)
                {
                    if (Line[RightEdge + 1] == 0)
                    {
                        if (RightEdge + 1 < Num - 2 && Line[RightEdge + 2] == Line[RightEdge])
                        {
                            if (Line[RightEdge + 3] == Line[RightEdge])
                            {
                                LineRecord[RightEdge + 3] = -1;
                                LineRecord[RightEdge + 2] = -1;
                                LineRecord[RightEdge] = 3;
                            }
                            else
                            {
                                if (Line[RightEdge + 3] == 0)
                                {
                                    LineRecord[RightEdge + 2] = -1;
                                    if (Lefttwo == true)
                                        LineRecord[RightEdge] = 5;
                                    else
                                        LineRecord[RightEdge] = 2;
                                }
                            }

                        }
                        else
                        {
                            if (LineRecord[LeftEdge] == 3)
                                return LineRecord[LeftEdge];
                            if (LineRecord[LeftEdge] == 2)
                            {
                                LineRecord[LeftEdge] = 5;
                                return LineRecord[LeftEdge];
                            }

                            if (Lefttwo == true)
                                LineRecord[Pos] = 4;
                            else
                                LineRecord[Pos] = 1;
                        }
                    }
                    else
                    {
                        if (LineRecord[LeftEdge] == 3)
                            return LineRecord[LeftEdge];
                        if (Lefttwo == true)
                            LineRecord[Pos] = 1;
                    }

                }
                return LineRecord[Pos];
            }
            return -1;
        }

        public int evaluate(Stone[,] board, bool nowBlack)
        {

            return Eveluate(board, nowBlack ? Stone.黑子 : Stone.白子);
        }
    }
}
