using TMPro;
using UnityEngine;

/// <summary>
/// 진행자 대사 자막. Main Camera 아래 World Space Canvas에 붙여서 시야 아래쪽에 띄운다.
/// 새 대사가 오면 바로 바꾸고, 정해진 시간이 지나면 숨긴다.
/// </summary>
public class HostSubtitle : MonoBehaviour
{
    [SerializeField] TMP_Text m_Text;
    [SerializeField, Tooltip("켜고 끌 오브젝트 (배경과 텍스트를 담은 자식)")] GameObject m_Panel;
    [SerializeField, Tooltip("대사 앞에 붙는 말하는 사람 이름")] string m_SpeakerName = "진행자";
    [SerializeField] Color m_SpeakerColor = new Color(0.85f, 0.25f, 0.2f);

    float m_HideTime;

    void Awake() => Hide();

    /// <summary>대사를 duration초 동안 띄운다.</summary>
    public void Show(string line, float duration)
    {
        var color = ColorUtility.ToHtmlStringRGB(m_SpeakerColor);
        m_Text.text = string.IsNullOrEmpty(m_SpeakerName) ? line : $"<color=#{color}>{m_SpeakerName}</color>  {line}";
        m_Panel.SetActive(true);
        m_HideTime = Time.time + duration;
    }

    public void Hide()
    {
        m_Panel.SetActive(false);
        m_Text.text = string.Empty;
    }

    void Update()
    {
        if (m_Panel.activeSelf && Time.time >= m_HideTime)
            Hide();
    }
}
