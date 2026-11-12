using System;
using System.Collections.Generic;

/// <summary>
/// 출제할 문제 묶음. 한 번 낸 문제는 모든 문제를 다 낼 때까지 다시 내지 않는다.
/// 다 쓰면 다시 섞어서 처음부터 쓴다. 공포 단계별 난이도 고르기는 공포 단계 작업에서 더한다.
/// </summary>
public sealed class QuizDeck
{
    readonly List<QuizQuestion> m_Questions;
    readonly List<int> m_Remaining = new List<int>();
    readonly Random m_Random;
    int m_LastIndex = -1;

    public QuizDeck(IReadOnlyList<QuizQuestion> questions, Random random = null)
    {
        m_Questions = new List<QuizQuestion>(questions ?? Array.Empty<QuizQuestion>());
        m_Random = random ?? new Random();
    }

    public int Count => m_Questions.Count;

    /// <summary>이번 묶음에서 아직 안 낸 문제 수.</summary>
    public int RemainingCount => m_Remaining.Count;

    /// <summary>
    /// 문제 하나를 꺼낸다. 문제가 없으면 null.
    /// 모든 문제를 다 낸 뒤 처음부터 다시 쓰게 되면 <paramref name="reshuffled"/>가 true다.
    /// </summary>
    public QuizQuestion Draw(out bool reshuffled)
    {
        reshuffled = false;
        if (m_Questions.Count == 0)
            return null;

        if (m_Remaining.Count == 0)
        {
            reshuffled = m_LastIndex >= 0;
            for (var i = 0; i < m_Questions.Count; i++)
                m_Remaining.Add(i);
        }

        var pick = m_Random.Next(m_Remaining.Count);
        // 다시 섞은 직후 바로 앞 문제가 또 나오지 않게 한다
        if (m_Remaining.Count > 1 && m_Remaining[pick] == m_LastIndex)
            pick = (pick + 1) % m_Remaining.Count;

        var index = m_Remaining[pick];
        m_Remaining.RemoveAt(pick);
        m_LastIndex = index;
        return m_Questions[index];
    }

    public QuizQuestion Draw() => Draw(out _);
}
