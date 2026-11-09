using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// XR Origin에 붙인다. 의자에 앉으면 시점을 옮기고 이동·회전·텔레포트를 끈다.
/// 머리 추적은 그대로 남아서 고개를 돌리거나 몸을 기울일 수 있다.
/// </summary>
[RequireComponent(typeof(XROrigin))]
public class SeatedPlayer : MonoBehaviour
{
    [SerializeField, Tooltip("XR Origin 아래 Locomotion 오브젝트 (Move, Turn, Teleportation 등)")] GameObject m_Locomotion;
    [SerializeField, Tooltip("양손 컨트롤러의 ControllerInputActionManager. 스틱으로 텔레포트 레이를 켜는 것을 막는다")] Behaviour[] m_ControllerInputManagers;

    XROrigin m_Origin;

    public Seat CurrentSeat { get; private set; }

    /// <summary>구속된 동안에는 의자에서 일어날 수 없다. 총이 상대에게 지급될 때 건다.</summary>
    public bool IsRestrained { get; private set; }

    void Awake() => m_Origin = GetComponent<XROrigin>();

    /// <summary>앉아 있을 때만 구속할 수 있다.</summary>
    public void Restrain()
    {
        if (CurrentSeat != null)
            IsRestrained = true;
    }

    public void Release() => IsRestrained = false;

    public void SitOn(Seat seat)
    {
        if (CurrentSeat != null)
            return;

        CurrentSeat = seat;

        // 1) 카메라가 의자 정면을 보도록 리그 전체를 돌린다 (앉는 순간 한 번만)
        m_Origin.MatchOriginUpCameraForward(Vector3.up, seat.EyePoint.forward);
        // 2) 카메라(머리)를 눈 위치로 옮긴다
        m_Origin.MoveCameraToWorldLocation(seat.EyePoint.position);

        // 3) 이동·회전·텔레포트를 끈다
        SetLocomotionEnabled(false);
    }

    public void StandUp()
    {
        if (CurrentSeat == null || IsRestrained)
            return;

        var standPosition = CurrentSeat.StandPosition;
        CurrentSeat = null;

        // 머리는 의자 뒤로 옮기고, 리그 바닥은 바닥 높이에 맞춘다 (실제 키는 머리 추적이 더해 준다)
        var camera = m_Origin.Camera.transform.position;
        var origin = m_Origin.transform.position;
        m_Origin.transform.position = new Vector3(
            origin.x + standPosition.x - camera.x,
            standPosition.y,
            origin.z + standPosition.z - camera.z);
        // Character Controller가 옮긴 위치를 바로 알도록 물리 위치를 맞춘다
        Physics.SyncTransforms();

        SetLocomotionEnabled(true);
    }

    void SetLocomotionEnabled(bool enabled)
    {
        if (m_Locomotion != null)
            m_Locomotion.SetActive(enabled);
        foreach (var manager in m_ControllerInputManagers)
        {
            if (manager != null)
                manager.enabled = enabled;
        }
    }
}
