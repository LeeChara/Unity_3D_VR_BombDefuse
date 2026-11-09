using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 사수 앞에 놓이는 여분 총알. 그립으로 집을 수 있다.
/// 집으면 총의 실린더가 열리고, 손에서 놓인 채 총의 장전 영역에 닿으면 총에 들어간다(<see cref="GunLoadZone"/>).
/// 게임 진행 컨트롤러가 총과 함께 만들고, 턴이 끝나면 남은 것을 지운다.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
public class SpareBullet : MonoBehaviour
{
    XRGrabInteractable m_Grab;
    Gun m_Gun;

    /// <summary>지금 손에 들려 있는지.</summary>
    public bool IsHeld => m_Grab != null && m_Grab.isSelected;

    /// <summary>총에 들어가서 사라졌는지.</summary>
    public bool IsConsumed { get; private set; }

    void Awake() => m_Grab = GetComponent<XRGrabInteractable>();

    void OnEnable() => m_Grab.selectEntered.AddListener(OnSelectEntered);

    void OnDisable() => m_Grab.selectEntered.RemoveListener(OnSelectEntered);

    /// <summary>이 총알을 넣을 총. 집으면 이 총의 실린더가 열린다.</summary>
    public void SetGun(Gun gun) => m_Gun = gun;

    /// <summary>잡을 수 있는지. 멀티에서 사수가 아닌 기기에서는 끈다.</summary>
    public void SetGrabbable(bool grabbable) => m_Grab.enabled = grabbable;

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (m_Gun != null)
            m_Gun.OpenCylinder();
    }

    /// <summary>총에 들어갔다. 바로 사라진다.</summary>
    public void Consume()
    {
        if (IsConsumed)
            return;
        IsConsumed = true;
        Destroy(gameObject);
    }
}
