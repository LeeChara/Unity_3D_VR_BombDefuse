using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 진행자 대사. 대사 CSV(event,id,text,emotion)를 불러와 이벤트 이름으로 대사를 고르고 자막으로 띄운다.
/// 이벤트 이름은 docs/07_event_list.md와 같다. 대사 id에 녹음(<see cref="VoiceBank"/>)이 있으면 자막과 함께 재생하고, 녹음이 더 길면 그만큼 기다린다.
/// 멀티: 대사는 호스트가 고른다. 고른 대사를 <see cref="LineShown"/>으로 알리면 네트워크 쪽이 클라이언트에 보내고, 클라이언트는 <see cref="ShowLine"/>으로 똑같이 띄운다.
/// </summary>
public class HostDialogue : MonoBehaviour
{
    [SerializeField, Tooltip("대사 CSV (Assets/_Project/Data/Dialogue/host_lines.csv)")] TextAsset m_LinesCsv;
    [SerializeField] HostSubtitle m_Subtitle;
    [SerializeField, Tooltip("대사 녹음 (비워 두면 자막만)")] VoiceBank m_Voice;
    [SerializeField, Tooltip("진행자 목소리를 낼 곳 (진행자 위치의 AudioSource). 비워 두면 이 오브젝트에 만든다")] AudioSource m_VoiceSource;
    [SerializeField, Min(0f), Tooltip("녹음이 끝나고 다음으로 넘어가기 전 쉬는 시간(초)")] float m_VoiceTail = 0.3f;

    [Header("표시 시간 = 기본 + 글자 수 × 글자당 시간 (최소값 이상)")]
    [SerializeField, Min(0f)] float m_BaseDuration = 0.8f;
    [SerializeField, Min(0f), Tooltip("한 글자당 읽는 시간(초)")] float m_SecondsPerCharacter = 0.08f;
    [SerializeField, Min(0f)] float m_MinDuration = 1.5f;

    HostLinePool m_Pool;

    /// <summary>대사를 띄웠을 때 (대사 id, 화면에 띄운 글, 표시 시간 초, 총알 개수 또는 -1).</summary>
    public event Action<string, string, float, int> LineShown;

    public VoiceBank Voice => m_Voice;

    /// <summary>진행자 목소리가 나는 곳 (진행자 쪽 효과음도 여기서 난다).</summary>
    public Transform Speaker => m_VoiceSource != null ? m_VoiceSource.transform : transform;

    void Awake()
    {
        var lines = new List<HostLine>();
        if (m_LinesCsv != null)
        {
            var warnings = new List<string>();
            lines = HostLineCsvParser.Parse(m_LinesCsv.text, warnings);
            foreach (var warning in warnings)
                Debug.LogWarning($"[Host] {m_LinesCsv.name}.csv: {warning}", m_LinesCsv);
            Debug.Log($"[Host] {m_LinesCsv.name}.csv에서 대사 {lines.Count}줄을 불러왔다", this);
        }
        else
        {
            Debug.LogWarning("[Host] 대사 CSV가 연결되지 않았다", this);
        }

        m_Pool = new HostLinePool(lines);

        if (m_VoiceSource == null)
        {
            m_VoiceSource = gameObject.AddComponent<AudioSource>();
            m_VoiceSource.playOnAwake = false;
            m_VoiceSource.spatialBlend = 1f;
            m_VoiceSource.minDistance = 1.5f;
            m_VoiceSource.maxDistance = 20f;
        }
    }

    /// <summary>
    /// 이 이벤트의 대사를 하나 골라 띄우고, 다 읽는 데 걸리는 시간(초)을 돌려준다. 대사가 없으면 0.
    /// </summary>
    /// <param name="bullets">대사의 {bullets}를 바꿀 총알 개수. 음수면 바꾸지 않는다</param>
    public float Say(string eventName, int bullets = -1)
    {
        var line = m_Pool?.Pick(eventName);
        if (line == null)
        {
            Debug.LogWarning($"[Host] '{eventName}' 대사가 없다", this);
            return 0f;
        }

        var text = line.Text;
        if (bullets >= 0)
            text = text.Replace("{bullets}", bullets.ToString());

        var duration = Mathf.Max(m_MinDuration, m_BaseDuration + text.Length * m_SecondsPerCharacter);
        var voice = m_Voice != null ? m_Voice.Find(line.Id, bullets) : null;
        if (voice != null)
            duration = Mathf.Max(duration, voice.length + m_VoiceTail);
        Debug.Log($"[Host] {text} ({eventName}/{line.Id}{(voice != null ? ", 음성 " + voice.name : "")})", this);
        ShowLine(line.Id, text, duration, bullets);
        return duration;
    }

    /// <summary>고른 대사를 띄우고 녹음이 있으면 재생한다. 멀티의 클라이언트는 호스트가 고른 대사를 이것으로 받는다.</summary>
    /// <param name="bullets">숫자별 녹음을 고를 총알 개수. 없으면 -1</param>
    public void ShowLine(string lineId, string text, float duration, int bullets = -1)
    {
        if (m_Subtitle != null)
            m_Subtitle.Show(text, duration);
        PlayVoice(m_Voice != null ? m_Voice.Find(lineId, bullets) : null);
        LineShown?.Invoke(lineId, text, duration, bullets);
    }

    // 앞 대사의 녹음이 남아 있으면 끊고 새 대사를 말한다
    void PlayVoice(AudioClip clip)
    {
        if (m_VoiceSource == null)
            return;
        m_VoiceSource.Stop();
        if (clip == null)
            return;
        m_VoiceSource.clip = clip;
        m_VoiceSource.Play();
    }
}
