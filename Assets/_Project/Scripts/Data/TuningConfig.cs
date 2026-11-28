using UnityEngine;

/// <summary>
/// 게임 수치 모음. 기준은 docs/01_rules_essential.md 9절 수치 표이고, 표를 바꾸면 이 에셋도 같이 바꾼다.
/// 에셋은 Assets/_Project/Data/TuningConfig.asset 하나만 둔다.
/// 게임 진행 컨트롤러가 이 에셋을 연결해서 필요한 값만 각 로직에 넘긴다. Core 클래스는 이 에셋을 직접 읽지 않는다.
/// </summary>
[CreateAssetMenu(fileName = "TuningConfig", menuName = "BombDefuse/Tuning Config")]
public class TuningConfig : ScriptableObject
{
    [Header("Seating")]
    [SerializeField, Min(0f), Tooltip("착석 안내부터 경고까지(초)")] float m_SeatingWarningDelay = 15f;
    [SerializeField, Min(0f), Tooltip("경고부터 강제 착석까지(초)")] float m_ForceSeatDelay = 10f;

    [Header("Revolver")]
    [SerializeField, Min(1), Tooltip("약실 수")] int m_ChamberCount = 6;
    [SerializeField, Min(1), Tooltip("시작할 때 넣는 총알 수. 약실 수를 넘을 수 없다")] int m_StartingBullets = 1;
    [SerializeField, Min(0), Tooltip("진행자가 재장전할 때마다 넣는 총알이 늘어나는 수. 최대는 약실 수")] int m_ReloadIncrement = 1;
    [SerializeField, Min(0), Tooltip("총을 받을 때 함께 놓이는 여분 총알 수")] int m_SpareBullets = 2;

    [Header("Quiz")]
    [SerializeField, Tooltip("공포 단계 0~최대의 순발력 문제(difficulty 1) 제한시간(초). 칸 수는 공포 단계 최대 + 1")] float[] m_QuizTimeLimits = { 15f, 12f, 10f, 7f };
    [SerializeField, Tooltip("공포 단계 0~최대의 사고력 문제(difficulty 2)와 괴상한 문제(difficulty 3) 제한시간(초). 고민하면 풀 수 있게 순발력보다 길게")]
    float[] m_ThinkingTimeLimits = { 22f, 20f, 18f, 16f };
    [SerializeField, Tooltip("공포 단계 0~최대마다 문제 종류가 나올 비율(%). X = 순발력, Y = 사고력, Z = 괴상한 문제. 합이 100이 아니어도 비율로 나눈다. 그 종류 문제가 없으면 빠진다")]
    Vector3[] m_QuizDifficultyWeights = { new Vector3(70f, 30f, 0f), new Vector3(50f, 50f, 0f), new Vector3(40f, 60f, 0f), new Vector3(35f, 45f, 20f) };
    [SerializeField, Min(0f), Tooltip("퀴즈의 남은 시간이 이 초가 되면 진행자가 한 번 압박한다. 0이면 압박하지 않는다")] float m_PressureLineInterval = 5f;

    [Header("Shooter Turn")]
    [SerializeField, Min(0f), Tooltip("사수 턴 제한시간(초)")] float m_ShooterTurnTimeLimit = 20f;
    [SerializeField, Min(0f), Tooltip("방아쇠를 당긴 뒤 공이치기를 젖히는 연출 시간(초)")] float m_HammerDelay = 0.6f;
    [SerializeField, Min(0f), Tooltip("떨어뜨린 총이 테이블로 돌아오기까지(초)")] float m_GunReturnDelay = 2f;

    [Header("Damage")]
    [SerializeField, Min(0), Tooltip("몸통 피격 허용 횟수. 이 횟수를 넘으면 사망")] int m_TorsoHitsAllowed = 1;

    [Header("Dread")]
    [SerializeField, Min(0), Tooltip("공포 단계 최대값")] int m_MaxDreadLevel = 3;

    public float SeatingWarningDelay => m_SeatingWarningDelay;
    public float ForceSeatDelay => m_ForceSeatDelay;
    public int ChamberCount => m_ChamberCount;
    public int StartingBullets => m_StartingBullets;
    public int ReloadIncrement => m_ReloadIncrement;
    public int SpareBullets => m_SpareBullets;
    public float PressureLineInterval => m_PressureLineInterval;
    public float ShooterTurnTimeLimit => m_ShooterTurnTimeLimit;
    public float HammerDelay => m_HammerDelay;
    public float GunReturnDelay => m_GunReturnDelay;
    public int TorsoHitsAllowed => m_TorsoHitsAllowed;
    public int MaxDreadLevel => m_MaxDreadLevel;

    /// <summary>
    /// 공포 단계와 문제 종류(difficulty: 1 = 순발력, 2 = 사고력, 3 = 괴상한 문제)에 맞는 퀴즈 제한시간(초).
    /// 범위를 벗어나면 가장 가까운 단계 값을 쓴다.
    /// </summary>
    public float GetQuizTimeLimit(int dreadLevel, int difficulty)
    {
        var limits = difficulty >= 2 && m_ThinkingTimeLimits != null && m_ThinkingTimeLimits.Length > 0 ? m_ThinkingTimeLimits : m_QuizTimeLimits;
        var index = Mathf.Clamp(dreadLevel, 0, limits.Length - 1);
        return limits[index];
    }

    /// <summary>공포 단계에 맞는 문제 종류 비율 (0 = 순발력, 1 = 사고력, 2 = 괴상한 문제). 범위를 벗어나면 가장 가까운 단계 값을 쓴다.</summary>
    public float[] GetQuizDifficultyWeights(int dreadLevel)
    {
        if (m_QuizDifficultyWeights == null || m_QuizDifficultyWeights.Length == 0)
            return new[] { 1f, 1f, 1f };
        var weights = m_QuizDifficultyWeights[Mathf.Clamp(dreadLevel, 0, m_QuizDifficultyWeights.Length - 1)];
        return new[] { weights.x, weights.y, weights.z };
    }

    void OnValidate()
    {
        m_StartingBullets = Mathf.Clamp(m_StartingBullets, 1, m_ChamberCount);

        // 제한시간은 공포 단계(0~최대)마다 하나씩. 칸이 늘면 마지막 값으로 채운다
        var count = m_MaxDreadLevel + 1;
        m_QuizTimeLimits = FitLimits(m_QuizTimeLimits, count, 15f);
        m_ThinkingTimeLimits = FitLimits(m_ThinkingTimeLimits, count, 22f);

        // 난이도 비율도 공포 단계마다 하나씩
        if (m_QuizDifficultyWeights == null || m_QuizDifficultyWeights.Length == 0)
            m_QuizDifficultyWeights = new[] { new Vector3(1f, 1f, 1f) };
        if (m_QuizDifficultyWeights.Length != count)
        {
            var last = m_QuizDifficultyWeights[m_QuizDifficultyWeights.Length - 1];
            var oldLength = m_QuizDifficultyWeights.Length;
            System.Array.Resize(ref m_QuizDifficultyWeights, count);
            for (var i = oldLength; i < count; i++)
                m_QuizDifficultyWeights[i] = last;
        }

        for (var i = 0; i < m_QuizDifficultyWeights.Length; i++)
            m_QuizDifficultyWeights[i] = Vector3.Max(m_QuizDifficultyWeights[i], Vector3.zero);
    }

    static float[] FitLimits(float[] limits, int count, float fallback)
    {
        if (limits == null || limits.Length == 0)
            limits = new[] { fallback };
        if (limits.Length != count)
        {
            var last = limits[limits.Length - 1];
            var oldLength = limits.Length;
            System.Array.Resize(ref limits, count);
            for (var i = oldLength; i < count; i++)
                limits[i] = last;
        }

        for (var i = 0; i < limits.Length; i++)
            limits[i] = Mathf.Max(0f, limits[i]);
        return limits;
    }
}
