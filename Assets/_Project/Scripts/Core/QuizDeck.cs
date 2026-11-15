using System;
using System.Collections.Generic;

/// <summary>
/// 출제할 문제 묶음. 한 번 낸 문제는 모든 문제를 다 낼 때까지 다시 내지 않는다.
/// 다 쓰면 다시 섞어서 처음부터 쓴다. 난이도를 주면 그 난이도에서만 고르고, 그 난이도를 다 쓰면 그 난이도만 다시 채운다.
/// 공포 단계는 <see cref="DrawWeighted"/>로 난이도를 비율대로 먼저 뽑고 그 난이도의 문제를 꺼낸다.
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

    /// <summary>
    /// 난이도를 비율대로 뽑은 뒤 그 난이도의 문제를 꺼낸다. weights[0] = 하, [1] = 중, [2] = 상.
    /// 문제가 하나도 없는 난이도는 비율에서 빼고 나머지끼리 나눈다. 모든 비율이 0이면 아무 문제나 꺼낸다.
    /// </summary>
    public QuizQuestion DrawWeighted(IReadOnlyList<float> weights, out bool reshuffled)
    {
        var total = 0f;
        var usable = new float[3];
        for (var i = 0; i < 3; i++)
        {
            var weight = weights != null && i < weights.Count ? Math.Max(0f, weights[i]) : 0f;
            usable[i] = HasDifficulty(i + 1) ? weight : 0f;
            total += usable[i];
        }

        if (total <= 0f)
            return Draw(out reshuffled);

        var roll = m_Random.NextDouble() * total;
        var difficulty = 3;
        for (var i = 0; i < 3; i++)
        {
            if (roll < usable[i])
            {
                difficulty = i + 1;
                break;
            }

            roll -= usable[i];
        }

        return Draw(difficulty, difficulty, out reshuffled);
    }

    bool HasDifficulty(int difficulty)
    {
        foreach (var question in m_Questions)
            if (question.Difficulty == difficulty)
                return true;
        return false;
    }

    /// <summary>
    /// 난이도가 min~max인 문제 하나를 꺼낸다 (공포 단계용). 그 난이도 문제가 하나도 없으면 아무 문제나 꺼낸다.
    /// 그 범위의 문제를 다 낸 뒤 그 범위만 다시 채우게 되면 <paramref name="reshuffled"/>가 true다.
    /// </summary>
    public QuizQuestion Draw(int minDifficulty, int maxDifficulty, out bool reshuffled)
    {
        reshuffled = false;
        if (m_Questions.Count == 0)
            return null;

        var candidates = new List<int>();
        foreach (var index in m_Remaining)
            if (InRange(index, minDifficulty, maxDifficulty))
                candidates.Add(index);

        if (candidates.Count == 0)
        {
            for (var i = 0; i < m_Questions.Count; i++)
                if (InRange(i, minDifficulty, maxDifficulty))
                {
                    candidates.Add(i);
                    if (!m_Remaining.Contains(i))
                        m_Remaining.Add(i);
                }

            if (candidates.Count == 0)
                return Draw(out reshuffled);
            reshuffled = m_LastIndex >= 0;
        }

        var pick = m_Random.Next(candidates.Count);
        if (candidates.Count > 1 && candidates[pick] == m_LastIndex)
            pick = (pick + 1) % candidates.Count;

        var chosen = candidates[pick];
        m_Remaining.Remove(chosen);
        m_LastIndex = chosen;
        return m_Questions[chosen];
    }

    bool InRange(int index, int min, int max)
    {
        var difficulty = m_Questions[index].Difficulty;
        return difficulty >= min && difficulty <= max;
    }
}
