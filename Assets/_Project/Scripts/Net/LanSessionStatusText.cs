using TMPro;
using UnityEngine;

/// <summary>
/// <see cref="LanSession.Status"/>를 텍스트에 표시한다.
/// </summary>
public class LanSessionStatusText : MonoBehaviour
{
    [SerializeField] TMP_Text m_Text;

    LanSession m_Session;

    void Start()
    {
        m_Session = LanSession.Instance;
        if (m_Session == null)
        {
            m_Text.text = "No LanSession in the scene";
            return;
        }

        m_Session.StatusChanged += Show;
        Show(m_Session.Status);
    }

    void OnDestroy()
    {
        if (m_Session != null)
            m_Session.StatusChanged -= Show;
    }

    void Show(string status) => m_Text.text = status;
}
