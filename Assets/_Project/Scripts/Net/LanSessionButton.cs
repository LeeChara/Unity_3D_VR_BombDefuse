using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 임시 접속 버튼. 손(레이 포함)으로 가리키고 트리거를 누르면 호스트 열기, 접속, 게임 시작, 나가기 중 하나를 한다.
/// 지금 누를 수 없는 버튼은 글자를 흐리게 한다.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class LanSessionButton : TriggerInteraction
{
    public enum Command
    {
        Host,
        Join,
        Leave,
        StartGame,
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
                default:
                    return !session.IsActive;
            }
        }
    }

    protected override void Update()
    {
        base.Update();
        if (m_Label != null)
            m_Label.color = IsAvailable ? Color.white : k_DisabledLabelColor;
    }

    protected override bool CanUse(XRBaseInputInteractor interactor, out string prompt)
    {
        prompt = m_Prompt;
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
        }
    }
}
