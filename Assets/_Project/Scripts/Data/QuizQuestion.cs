using System;
using UnityEngine;

/// <summary>
/// 퀴즈 한 문제. 문제 CSV 한 줄에서 만든다 (<see cref="QuizCsvParser"/>).
/// </summary>
[Serializable]
public class QuizQuestion
{
    public string Id;
    [Tooltip("1(하) 2(중) 3(상)")] public int Difficulty = 1;
    public string Category;
    [TextArea] public string Question;
    [Tooltip("A B C D 순서")] public string[] Choices = new string[4];
    [Tooltip("정답 선택지 순서 (0 = A ~ 3 = D). CSV의 answer는 1~4라서 1을 뺀 값")] public int AnswerIndex;
}
