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

    [Header("Quiz")]
    [SerializeField, Tooltip("공포 단계 0~최대의 퀴즈 제한시간(초). 칸 수는 공포 단계 최대 + 1")] float[] m_QuizTimeLimits = { 15f, 12f, 10f, 7f };
    [SerializeField, Min(0f), Tooltip("두 사람의 입력 차이가 이 안이면 동시로 본다(초)")] float m_SimultaneousWindow = 0.15f;
    [SerializeField, Min(0f), Tooltip("퀴즈 중 진행자 압박 대사 간격(초)")] float m_PressureLineInterval = 5f;

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
    public float SimultaneousWindow => m_SimultaneousWindow;
    public float PressureLineInterval => m_PressureLineInterval;
    public float ShooterTurnTimeLimit => m_ShooterTurnTimeLimit;
    public float HammerDelay => m_HammerDelay;
    public float GunReturnDelay => m_GunReturnDelay;
    public int TorsoHitsAllowed => m_TorsoHitsAllowed;
    public int MaxDreadLevel => m_MaxDreadLevel;

    /// <summary>공포 단계에 맞는 퀴즈 제한시간(초). 범위를 벗어나면 가장 가까운 단계 값을 쓴다.</summary>
    public float GetQuizTimeLimit(int dreadLevel)
    {
        var index = Mathf.Clamp(dreadLevel, 0, m_QuizTimeLimits.Length - 1);
        return m_QuizTimeLimits[index];
    }

    void OnValidate()
    {
        m_StartingBullets = Mathf.Clamp(m_StartingBullets, 1, m_ChamberCount);

        // 제한시간은 공포 단계(0~최대)마다 하나씩. 칸이 늘면 마지막 값으로 채운다
        var count = m_MaxDreadLevel + 1;
        if (m_QuizTimeLimits == null || m_QuizTimeLimits.Length == 0)
            m_QuizTimeLimits = new[] { 15f };
        if (m_QuizTimeLimits.Length != count)
        {
            var last = m_QuizTimeLimits[m_QuizTimeLimits.Length - 1];
            var oldLength = m_QuizTimeLimits.Length;
            System.Array.Resize(ref m_QuizTimeLimits, count);
            for (var i = oldLength; i < count; i++)
                m_QuizTimeLimits[i] = last;
        }

        for (var i = 0; i < m_QuizTimeLimits.Length; i++)
            m_QuizTimeLimits[i] = Mathf.Max(0f, m_QuizTimeLimits[i]);
    }
}
