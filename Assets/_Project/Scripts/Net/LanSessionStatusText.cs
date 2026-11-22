using TMPro;
using UnityEngine;

/// <summary>
/// <see cref="LanSession.Status"/>를 텍스트에 표시한다. 보이스챗(<see cref="LanVoiceChat"/>)이 있으면 그 상태를 아랫줄에 덧붙인다.
/// </summary>
public class LanSessionStatusText : MonoBehaviour
{
    [SerializeField] TMP_Text m_Text;

    LanSession m_Session;
    LanVoiceChat m_Voice;

    void Start()
    {
        m_Session = LanSession.Instance;
        if (m_Session == null)
        {
            m_Text.text = "No LanSession in the scene";
            return;
        }

        m_Session.StatusChanged += OnChanged;
        m_Voice = LanVoiceChat.Instance;
        if (m_Voice != null)
            m_Voice.StatusChanged += OnChanged;
        Refresh();
    }

    void OnDestroy()
    {
        if (m_Session != null)
            m_Session.StatusChanged -= OnChanged;
        if (m_Voice != null)
            m_Voice.StatusChanged -= OnChanged;
    }

    void OnChanged(string _) => Refresh();

    void Refresh() => m_Text.text = m_Voice != null ? $"{m_Session.Status}\n{m_Voice.Status}" : m_Session.Status;
}
