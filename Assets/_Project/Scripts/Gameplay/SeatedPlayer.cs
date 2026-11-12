using System;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// XR Origin에 붙인다. 의자에 앉으면 시점을 옮기고 이동·회전·텔레포트를 끈다.
/// 머리 추적은 그대로 남아서 고개를 돌리거나 몸을 기울일 수 있다.
/// 게임 중에는 착석한 뒤 일어날 수 없게 잠근다 (<see cref="LockSeat"/>).
/// </summary>
[RequireComponent(typeof(XROrigin))]
public class SeatedPlayer : MonoBehaviour
{
    [SerializeField, Tooltip("XR Origin 아래 Locomotion 오브젝트 (Move, Turn, Teleportation 등)")] GameObject m_Locomotion;
    [SerializeField, Tooltip("양손 컨트롤러의 ControllerInputActionManager. 스틱으로 텔레포트 레이를 켜는 것을 막는다")] Behaviour[] m_ControllerInputManagers;

    XROrigin m_Origin;

    public Seat CurrentSeat { get; private set; }

    /// <summary>앉을 수 있는 유일한 의자. null이면 아무 의자에나 앉을 수 있다. 접속하면 좌석 배정으로 정해진다.</summary>
    public Seat AssignedSeat { get; set; }

    /// <summary>잠긴 동안에는 의자에서 일어날 수 없다. 게임 중 착석하면 잠그고, 게임이 끝나면 푼다.</summary>
    public bool IsSeatLocked { get; private set; }

    /// <summary>의자에 앉았을 때 (스스로 앉든 강제로 앉든).</summary>
    public event Action<Seat> Seated;

    void Awake() => m_Origin = GetComponent<XROrigin>();

    /// <summary>앉아 있을 때만 잠글 수 있다.</summary>
    public void LockSeat()
    {
        if (CurrentSeat != null)
            IsSeatLocked = true;
    }

    public void UnlockSeat() => IsSeatLocked = false;

    public void SitOn(Seat seat)
    {
        if (CurrentSeat != null || seat == null)
            return;

        CurrentSeat = seat;

        // 1) 카메라가 의자 정면을 보도록 리그 전체를 돌린다 (앉는 순간 한 번만)
        m_Origin.MatchOriginUpCameraForward(Vector3.up, seat.EyePoint.forward);
        // 2) 카메라(머리)를 눈 위치로 옮긴다
        m_Origin.MoveCameraToWorldLocation(seat.EyePoint.position);

        // 3) 이동·회전·텔레포트를 끈다
        SetLocomotionEnabled(false);

        Seated?.Invoke(seat);
    }

    public void StandUp()
    {
        if (CurrentSeat == null || IsSeatLocked)
            return;

        var standPosition = CurrentSeat.StandPosition;
        CurrentSeat = null;
        MoveTo(standPosition);
        SetLocomotionEnabled(true);
    }

    /// <summary>
    /// 의자 뒤에 세운다 (게임 시작 때 등장 위치). 앉아 있었으면 잠금을 풀고 일어난다. 의자 쪽을 보게 돌린다.
    /// </summary>
    public void PlaceBehind(Seat seat)
    {
        IsSeatLocked = false;
        CurrentSeat = null;
        m_Origin.MatchOriginUpCameraForward(Vector3.up, seat.EyePoint.forward);
        MoveTo(seat.StandPosition);
        SetLocomotionEnabled(true);
    }

    // 머리를 position의 수평 위치로 옮기고, 리그 바닥은 position 높이(바닥)에 맞춘다 (실제 키는 머리 추적이 더해 준다)
    void MoveTo(Vector3 position)
    {
        var camera = m_Origin.Camera.transform.position;
        var origin = m_Origin.transform.position;
        m_Origin.transform.position = new Vector3(
            origin.x + position.x - camera.x,
            position.y,
            origin.z + position.z - camera.z);
        // Character Controller가 옮긴 위치를 바로 알도록 물리 위치를 맞춘다
        Physics.SyncTransforms();
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
