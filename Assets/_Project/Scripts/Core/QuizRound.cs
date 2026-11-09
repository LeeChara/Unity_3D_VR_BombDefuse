using System;

/// <summary>퀴즈 한 문제가 끝난 방식.</summary>
public enum QuizOutcome
{
    /// <summary>누군가 정답을 맞혔다.</summary>
    Correct,
    /// <summary>제한시간 안에 아무도 못 맞혔다.</summary>
    Timeout,
    /// <summary>두 사람 모두 오답으로 잠겼다.</summary>
    AllWrong,
}

/// <summary>퀴즈 한 문제의 결과.</summary>
public readonly struct QuizResult
{
    public readonly QuizOutcome Outcome;
    /// <summary>정답자 좌석 번호(1, 2). 정답자가 없으면 0.</summary>
    public readonly int WinnerSeat;

    public QuizResult(QuizOutcome outcome, int winnerSeat)
    {
        Outcome = outcome;
        WinnerSeat = winnerSeat;
    }

    public bool HasWinner => Outcome == QuizOutcome.Correct;
}

/// <summary>버튼 입력 하나를 처리한 결과.</summary>
public enum QuizSubmitResult
{
    Correct,
    Wrong,
    /// <summary>이미 끝났거나, 잠겼거나, 시간이 지났거나, 잘못된 좌석·선택지라서 무시했다.</summary>
    Ignored,
}

/// <summary>
/// 퀴즈 한 문제의 진행. 네트워크와 씬을 모르는 순수 C#이다.
/// 시각은 직접 읽지 않고 밖에서 받는다. 멀티에서는 누른 순간의 네트워크 시각을 넘기면 된다.
/// 규칙: 먼저 정답을 누른 사람이 이긴다. 오답을 누른 사람은 이 문제에서 잠긴다.
/// 두 사람 모두 잠기거나 제한시간이 지나면 정답자 없이 끝난다 (docs/01_rules_essential.md 3절).
/// </summary>
public sealed class QuizRound
{
    public const int SeatCount = 2;

    readonly bool[] m_Locked = new bool[SeatCount];

    /// <summary>오답이 나왔을 때 (좌석 번호, 고른 선택지 0~3).</summary>
    public event Action<int, int> AnswerWrong;

    /// <summary>문제가 끝났을 때 한 번.</summary>
    public event Action<QuizResult> Resolved;

    public QuizQuestion Question { get; }
    public double StartTime { get; }
    public float TimeLimit { get; }
    public double EndTime => StartTime + TimeLimit;
    public bool IsResolved { get; private set; }
    public QuizResult Result { get; private set; }

    public QuizRound(QuizQuestion question, double startTime, float timeLimit)
    {
        Question = question ?? throw new ArgumentNullException(nameof(question));
        StartTime = startTime;
        TimeLimit = Math.Max(0f, timeLimit);
    }

    /// <param name="seat">좌석 번호 (1, 2)</param>
    /// <param name="choiceIndex">고른 선택지 (0 = A ~ 3 = D)</param>
    /// <param name="pressTime">누른 시각. 제한시간이 지난 뒤의 입력은 무시한다</param>
    public QuizSubmitResult Submit(int seat, int choiceIndex, double pressTime)
    {
        if (IsResolved || seat < 1 || seat > SeatCount || choiceIndex < 0 || choiceIndex > 3)
            return QuizSubmitResult.Ignored;
        if (m_Locked[seat - 1] || pressTime > EndTime)
            return QuizSubmitResult.Ignored;

        if (choiceIndex == Question.AnswerIndex)
        {
            Resolve(new QuizResult(QuizOutcome.Correct, seat));
            return QuizSubmitResult.Correct;
        }

        m_Locked[seat - 1] = true;
        AnswerWrong?.Invoke(seat, choiceIndex);

        if (Array.TrueForAll(m_Locked, locked => locked))
            Resolve(new QuizResult(QuizOutcome.AllWrong, 0));

        return QuizSubmitResult.Wrong;
    }

    /// <summary>매 프레임 현재 시각을 넘긴다. 제한시간이 지나면 시간 초과로 끝낸다.</summary>
    public void Tick(double now)
    {
        if (!IsResolved && now >= EndTime)
            Resolve(new QuizResult(QuizOutcome.Timeout, 0));
    }

    public bool IsLocked(int seat) => seat >= 1 && seat <= SeatCount && m_Locked[seat - 1];

    /// <summary>남은 시간 비율 (1 = 시작, 0 = 끝).</summary>
    public float GetRemainingRatio(double now)
    {
        if (TimeLimit <= 0f)
            return 0f;
        var ratio = (EndTime - now) / TimeLimit;
        return (float)Math.Max(0.0, Math.Min(1.0, ratio));
    }

    void Resolve(QuizResult result)
    {
        IsResolved = true;
        Result = result;
        Resolved?.Invoke(result);
    }
}
