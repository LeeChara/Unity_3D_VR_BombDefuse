using TMPro;
using UnityEngine;

/// <summary>
/// 시야 아래쪽에 상호작용 안내 문구를 띄운다. Main Camera 아래 World Space Canvas에 붙인다.
/// 마지막으로 Show를 부른 대상의 문구를 보여 주고, 그 대상이 Hide를 부르면 숨긴다.
/// </summary>
public class InteractionPrompt : MonoBehaviour
{
    [SerializeField, Tooltip("문구를 표시할 텍스트")] TMP_Text m_Text;
    [SerializeField, Tooltip("켜고 끌 오브젝트 (배경과 텍스트를 담은 자식)")] GameObject m_Panel;

    static InteractionPrompt s_Instance;

    Object m_Owner;

    void Awake()
    {
        s_Instance = this;
        m_Panel.SetActive(false);
    }

    void OnDestroy()
    {
        if (s_Instance == this)
            s_Instance = null;
    }

    public static void Show(Object owner, string message)
    {
        if (s_Instance == null)
            return;

        s_Instance.m_Owner = owner;
        if (s_Instance.m_Text.text != message)
            s_Instance.m_Text.text = message;
        s_Instance.m_Panel.SetActive(true);
    }

    public static void Hide(Object owner)
    {
        if (s_Instance == null || s_Instance.m_Owner != owner)
            return;

        s_Instance.m_Owner = null;
        s_Instance.m_Panel.SetActive(false);
    }
}
