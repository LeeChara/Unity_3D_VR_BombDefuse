using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 메인 메뉴(<see cref="MainMenu"/>)의 버튼. 손(레이 포함)으로 가리키고 트리거를 누르면
/// 방 만들기, 참가, 시작, 나가기, 마이크 켜고 끄기, 음량 조절, 게임 종료 중 하나를 한다.
/// 지금 누를 수 없는 버튼은 글자를 흐리게 한다.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class LanSessionButton : TriggerInteraction
{
    // 순서를 바꾸지 않는다 (씬에 번호로 저장된다). 새 명령은 끝에 붙인다
    public enum Command
    {
        Host,
        Join,
        Leave,
        StartGame,
        ToggleMute,
        VolumeDown,
        VolumeUp,
        Quit,
    }

    static readonly Color k_DisabledLabelColor = new Color(1f, 1f, 1f, 0.25f);

    [SerializeField] Command m_Command;
    [SerializeField] string m_Prompt = "Press Trigger";
    [SerializeField, Tooltip("누를 수 없을 때 흐리게 할 글자 (비워도 된다)")] TMP_Text m_Label;

    bool IsAvailable
    {
        get
        {
            var session = LanSession.Instance;
            if (session == null)
                return false;

            switch (m_Command)
            {
                case Command.Leave:
                    return session.IsActive;
                case Command.StartGame:
                    return session.CanStartGame;
                case Command.ToggleMute:
                    return LanVoiceChat.Instance != null && LanVoiceChat.Instance.IsInChannel;
                case Command.VolumeDown:
                    return MainMenu.Volume > 0f;
                case Command.VolumeUp:
                    return MainMenu.Volume < 1f;
                case Command.Quit:
                    return true;
                default:
                    return !session.IsActive;
            }
        }
    }

    // 마이크 버튼은 지금 상태의 반대를 적는다: 켜져 있으면 "마이크 끄기", 꺼져 있으면 "마이크 켜기"
    static bool MicIsOn => LanVoiceChat.Instance != null && !LanVoiceChat.Instance.IsMuted;

    protected override void Update()
    {
        base.Update();
        if (m_Label == null)
            return;

        m_Label.color = IsAvailable ? Color.white : k_DisabledLabelColor;
        if (m_Command == Command.ToggleMute)
            m_Label.text = MicIsOn ? "마이크 끄기" : "마이크 켜기";
    }

    protected override bool CanUse(XRBaseInputInteractor interactor, out string prompt)
    {
        prompt = m_Command == Command.ToggleMute ? (MicIsOn ? "트리거를 눌러 마이크 끄기" : "트리거를 눌러 마이크 켜기") : m_Prompt;
        return IsAvailable;
    }

    protected override void OnTriggerPressed(XRBaseInputInteractor interactor)
    {
        var session = LanSession.Instance;
        switch (m_Command)
        {
            case Command.Host:
                session.StartHost();
                break;
            case Command.Join:
                session.Join();
                break;
            case Command.Leave:
                session.Leave();
                break;
            case Command.StartGame:
                session.StartGame();
                break;
            case Command.ToggleMute:
                LanVoiceChat.Instance?.ToggleMute();
                break;
            case Command.VolumeDown:
                MainMenu.ChangeVolume(-1);
                break;
            case Command.VolumeUp:
                MainMenu.ChangeVolume(1);
                break;
            case Command.Quit:
                MainMenu.QuitGame();
                break;
        }
    }
}
