using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 손(레이 포함)으로 가리키고 트리거를 누르면 앉는다. 앉아 있을 때 다시 누르면 일어난다.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class Seat : TriggerInteraction
{
    [SerializeField, Tooltip("앉았을 때 눈 위치. Z축(파란 화살표)이 정면")] Transform m_EyePoint;
    [SerializeField, Tooltip("일어날 때 눈 위치에서 뒤로 물러나는 거리(m)")] float m_StandUpDistance = 0.6f;
    [SerializeField] string m_SitPrompt = "Press Trigger to sit";
    [SerializeField] string m_StandUpPrompt = "Press Trigger to stand up";

    Collider m_Collider;

    public Transform EyePoint => m_EyePoint;

    /// <summary>일어날 때 설 위치. 의자 뒤쪽 바닥.</summary>
    public Vector3 StandPosition
    {
        get
        {
            var position = m_EyePoint.position - m_EyePoint.forward * m_StandUpDistance;
            position.y = m_Collider != null ? m_Collider.bounds.min.y : transform.position.y;
            return position;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        m_Collider = GetComponent<Collider>();
    }

    protected override bool CanUse(XRBaseInputInteractor interactor, out string prompt)
    {
        prompt = null;
        var player = interactor.GetComponentInParent<SeatedPlayer>();
        if (player == null)
            return false;

        if (player.CurrentSeat == this)
            prompt = player.IsSeatLocked ? null : m_StandUpPrompt;
        else if (player.CurrentSeat == null && (player.AssignedSeat == null || player.AssignedSeat == this))
            prompt = m_SitPrompt;

        // 다른 의자에 앉아 있거나, 일어날 수 없게 잠겨 있거나, 상대 좌석이면 이 의자는 반응하지 않는다
        return prompt != null;
    }

    protected override void OnTriggerPressed(XRBaseInputInteractor interactor)
    {
        var player = interactor.GetComponentInParent<SeatedPlayer>();
        if (player.CurrentSeat == this)
            player.StandUp();
        else
            player.SitOn(this);
    }
}
