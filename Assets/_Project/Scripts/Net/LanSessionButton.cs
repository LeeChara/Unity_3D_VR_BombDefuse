using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 임시 접속 버튼. 손(레이 포함)으로 가리키고 트리거를 누르면 호스트 열기, 접속, 나가기 중 하나를 한다.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class LanSessionButton : TriggerInteraction
{
    public enum Command
    {
        Host,
        Join,
        Leave,
    }

    [SerializeField] Command m_Command;
    [SerializeField] string m_Prompt = "Press Trigger";

    protected override bool CanUse(XRBaseInputInteractor interactor, out string prompt)
    {
        prompt = m_Prompt;
        var session = LanSession.Instance;
        if (session == null)
            return false;

        // 나가기는 접속 중에만, 호스트·접속은 접속 전에만
        return m_Command == Command.Leave ? session.IsActive : !session.IsActive;
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
        }
    }
}
