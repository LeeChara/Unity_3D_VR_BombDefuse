using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 진행자 대사. 대사 CSV(event,id,text,emotion)를 불러와 이벤트 이름으로 대사를 고르고 자막으로 띄운다.
/// 이벤트 이름은 docs/07_event_list.md와 같다. 녹음이 오면 같은 이벤트에 음성을 붙인다.
/// 멀티: 대사는 호스트가 고른다. 고른 대사를 <see cref="LineShown"/>으로 알리면 네트워크 쪽이 클라이언트에 보내고, 클라이언트는 <see cref="ShowLine"/>으로 똑같이 띄운다.
/// </summary>
public class HostDialogue : MonoBehaviour
{
    [SerializeField, Tooltip("대사 CSV (Assets/_Project/Data/Dialogue/host_lines.csv)")] TextAsset m_LinesCsv;
    [SerializeField] HostSubtitle m_Subtitle;

    [Header("표시 시간 = 기본 + 글자 수 × 글자당 시간 (최소값 이상)")]
    [SerializeField, Min(0f)] float m_BaseDuration = 0.8f;
    [SerializeField, Min(0f), Tooltip("한 글자당 읽는 시간(초)")] float m_SecondsPerCharacter = 0.08f;
    [SerializeField, Min(0f)] float m_MinDuration = 1.5f;

    HostLinePool m_Pool;

    /// <summary>대사를 띄웠을 때 (대사 id, 화면에 띄운 글, 표시 시간 초).</summary>
    public event Action<string, string, float> LineShown;

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
        Debug.Log($"[Host] {text} ({eventName}/{line.Id})", this);
        ShowLine(line.Id, text, duration);
        return duration;
    }

    /// <summary>고른 대사를 띄운다. 멀티의 클라이언트는 호스트가 고른 대사를 이것으로 받는다.</summary>
    public void ShowLine(string lineId, string text, float duration)
    {
        if (m_Subtitle != null)
            m_Subtitle.Show(text, duration);
        LineShown?.Invoke(lineId, text, duration);
    }
}
