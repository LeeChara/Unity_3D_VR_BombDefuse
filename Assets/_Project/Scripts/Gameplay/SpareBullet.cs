using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 사수 앞에 놓이는 여분 총알. 그립으로 집을 수 있다.
/// 총의 실린더가 열려 있을 때(손목 스냅) 총을 쥔 손이나 실린더에 가까이 가져가거나 던지면 총에 들어간다 (<see cref="Gun"/>이 판정).
/// 게임 진행 컨트롤러가 총과 함께 만들고, 턴이 끝나면 남은 것을 지운다. 멀티에서는 사수 기기의 총알만 잡을 수 있다.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
public class SpareBullet : MonoBehaviour
{
    static readonly List<SpareBullet> s_Active = new List<SpareBullet>();

    XRGrabInteractable m_Grab;

    /// <summary>지금 씬에 있는 여분 총알.</summary>
    public static IReadOnlyList<SpareBullet> Active => s_Active;

    /// <summary>이 총알을 넣을 총.</summary>
    public Gun Gun { get; private set; }

    /// <summary>지금 손에 들려 있는지.</summary>
    public bool IsHeld => m_Grab != null && m_Grab.isSelected;

    /// <summary>쥔 손의 위치. 들려 있지 않으면 총알 위치.</summary>
    public Vector3 HoldingHandPosition => IsHeld ? m_Grab.firstInteractorSelecting.transform.position : transform.position;

    /// <summary>총에 들어가서 사라졌는지.</summary>
    public bool IsConsumed { get; private set; }

    void Awake() => m_Grab = GetComponent<XRGrabInteractable>();

    void OnEnable() => s_Active.Add(this);

    void OnDisable() => s_Active.Remove(this);

    /// <summary>이 총알을 넣을 총.</summary>
    public void SetGun(Gun gun) => Gun = gun;

    /// <summary>잡을 수 있는지. 멀티에서 사수가 아닌 기기의 총알은 끈다.</summary>
    public void SetGrabbable(bool grabbable)
    {
        if (m_Grab == null)
            m_Grab = GetComponent<XRGrabInteractable>();
        m_Grab.enabled = grabbable;
    }

    /// <summary>총에 들어갔다. 손에 들려 있으면 놓게 하고 바로 사라진다.</summary>
    public void Consume()
    {
        if (IsConsumed)
            return;
        IsConsumed = true;
        if (IsHeld && m_Grab.interactionManager != null)
            m_Grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)m_Grab);
        Destroy(gameObject);
    }
}
