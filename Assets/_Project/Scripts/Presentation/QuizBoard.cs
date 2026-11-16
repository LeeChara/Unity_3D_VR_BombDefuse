using UnityEngine;

/// <summary>
/// 두 플레이어의 퀴즈 화면을 함께 다룬다. 같은 문제와 남은 시간을 모든 화면에 동시에 띄운다.
/// 남은 시간이 갱신되는 동안 째깍 소리를 낸다. 남은 시간이 짧으면 더 빠르고 높게 낸다. 시간이 멈추면(퀴즈가 끝나면) 소리도 멈춘다.
/// </summary>
public class QuizBoard : MonoBehaviour
{
    [SerializeField] QuizScreen[] m_Screens;

    [Header("째깍 소리")]
    [SerializeField, Tooltip("비워 두면 소리 없음")] AudioClip m_TickClip;
    [SerializeField, Range(0f, 1f)] float m_TickVolume = 0.5f;
    [SerializeField, Min(0.05f), Tooltip("평소 간격(초)")] float m_TickInterval = 1f;
    [SerializeField, Min(0f), Tooltip("남은 시간이 이 초 이하면 빠르게")] float m_FastBelow = 5f;
    [SerializeField, Min(0.05f), Tooltip("빠를 때 간격(초)")] float m_FastInterval = 0.5f;
    [SerializeField, Tooltip("빠를 때 음높이")] float m_FastPitch = 1.25f;

    AudioSource m_TickSource;
    float m_NextTickTime;

    // Start가 아니라 Awake에서 비워야 다른 스크립트가 Start에서 띄운 문제를 지우지 않는다
    void Awake()
    {
        Clear();
        m_TickSource = gameObject.AddComponent<AudioSource>();
        m_TickSource.playOnAwake = false;
        m_TickSource.spatialBlend = 1f;
        m_TickSource.minDistance = 1f;
        m_TickSource.maxDistance = 15f;
    }

    /// <summary>화면들의 가운데 (퀴즈 효과음이 나는 곳). 화면이 없으면 이 오브젝트 위치.</summary>
    public Vector3 Center
    {
        get
        {
            var sum = Vector3.zero;
            var count = 0;
            foreach (var screen in m_Screens)
            {
                if (screen == null)
                    continue;
                sum += screen.transform.position;
                count++;
            }
            return count > 0 ? sum / count : transform.position;
        }
    }

    public void Show(QuizQuestion question)
    {
        foreach (var screen in m_Screens)
            screen.Show(question);
        m_NextTickTime = Time.time + m_TickInterval;
    }

    /// <summary>남은 시간 비율 (1 = 가득, 0 = 없음). 남은 초를 주면 그에 맞춰 째깍 소리를 낸다.</summary>
    public void SetRemainingTime(float ratio, float secondsLeft = -1f)
    {
        foreach (var screen in m_Screens)
            screen.SetRemainingTime(ratio);
        if (secondsLeft > 0f)
            Tick(secondsLeft);
    }

    void Tick(float secondsLeft)
    {
        if (m_TickClip == null || Time.time < m_NextTickTime)
            return;

        var fast = secondsLeft <= m_FastBelow;
        m_NextTickTime = Time.time + (fast ? m_FastInterval : m_TickInterval);
        m_TickSource.transform.position = Center;
        m_TickSource.pitch = fast ? m_FastPitch : 1f;
        m_TickSource.PlayOneShot(m_TickClip, m_TickVolume);
    }

    public void Clear()
    {
        foreach (var screen in m_Screens)
            screen.Clear();
    }
}
