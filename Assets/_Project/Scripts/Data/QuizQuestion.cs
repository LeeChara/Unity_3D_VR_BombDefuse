using System;
using UnityEngine;

/// <summary>
/// 퀴즈 한 문제. 나중에 문제 CSV에서 읽어 만든다.
/// </summary>
[Serializable]
public class QuizQuestion
{
    [TextArea] public string Question;
    [Tooltip("A B C D 순서")] public string[] Choices = new string[4];
}
