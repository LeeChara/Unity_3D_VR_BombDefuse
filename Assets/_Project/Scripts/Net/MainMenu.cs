using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 메인 메뉴. 접속 패널(LanSessionPanel)에 붙인다. 게임 이름, 접속 상태, 방 만들기·참가·시작·나가기, 마이크, 음량, 게임 종료 버튼이 있다.
/// 버튼은 <see cref="LanSessionButton"/>이 맡고, 이 컴포넌트는 메뉴를 보이고 숨기고 음량을 관리한다.
/// 메뉴는 로비(<see cref="GameState.Lobby"/>)에서만 보이고 게임 중에는 숨는다. 한 판이 끝나면 로비로 돌아와 다시 보인다.
/// 음량은 게임 소리 전체(<see cref="AudioListener.volume"/>)이며 기기에 저장된다.
/// </summary>
public class MainMenu : MonoBehaviour
{
    const string k_VolumeKey = "MainMenu.Volume";
    const int k_VolumeSteps = 10;

    [SerializeField] GameFlowController m_Flow;
    [SerializeField, Tooltip("지금 음량을 보여 줄 글자 (비워도 된다)")] TMP_Text m_VolumeLabel;

    static bool s_VolumeLoaded;
    readonly List<GameObject> m_Content = new List<GameObject>();
    bool m_Shown = true;

    /// <summary>게임 소리 전체 음량 (0 ~ 1).</summary>
    public static float Volume
    {
        get
        {
            LoadVolume();
            return AudioListener.volume;
        }
    }

    /// <summary>음량을 한 칸(10%) 올리거나(+1) 내린다(-1).</summary>
    public static void ChangeVolume(int steps)
    {
        LoadVolume();
        var level = Mathf.Clamp(Mathf.RoundToInt(AudioListener.volume * k_VolumeSteps) + steps, 0, k_VolumeSteps);
        AudioListener.volume = (float)level / k_VolumeSteps;
        PlayerPrefs.SetFloat(k_VolumeKey, AudioListener.volume);
        PlayerPrefs.Save();
    }

    /// <summary>게임을 끈다 (에디터에서는 Play를 멈춘다).</summary>
    public static void QuitGame()
    {
        var session = LanSession.Instance;
        if (session != null && session.IsActive)
            session.Leave();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    static void LoadVolume()
    {
        if (s_VolumeLoaded)
            return;
        s_VolumeLoaded = true;
        AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(k_VolumeKey, 1f));
    }

    void Awake()
    {
        LoadVolume();
        // 패널의 자식 전부가 메뉴 내용이다. 패널 자체(이 오브젝트)는 켜 둔 채 자리를 따라간다(LanSessionPanelFollower)
        foreach (Transform child in transform)
            m_Content.Add(child.gameObject);
    }

    void OnEnable()
    {
        if (m_Flow != null)
            m_Flow.StateMachine.StateChanged += OnStateChanged;
        SetShown(m_Flow == null || m_Flow.StateMachine.Current == GameState.Lobby);
    }

    void OnDisable()
    {
        if (m_Flow != null)
            m_Flow.StateMachine.StateChanged -= OnStateChanged;
    }

    void OnStateChanged(GameState previous, GameState next) => SetShown(next == GameState.Lobby);

    void SetShown(bool shown)
    {
        if (m_Shown == shown)
            return;
        m_Shown = shown;
        foreach (var content in m_Content)
            if (content != null)
                content.SetActive(shown);
    }

    void Update()
    {
        if (m_VolumeLabel != null && m_Shown)
            m_VolumeLabel.text = $"음량 {Mathf.RoundToInt(Volume * 100f)}%";
    }
}
