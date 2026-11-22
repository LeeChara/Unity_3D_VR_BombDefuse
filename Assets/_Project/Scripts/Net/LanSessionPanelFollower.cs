using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// 접속 패널(LanSessionPanel)을 이 기기 플레이어의 옆, 진행자 반대쪽에 둔다. 앞에 두면 테이블과 진행자를 가리기 때문이다.
/// 좌석이 배정되어 자리가 바뀌면 따라간다. 패널은 네트워크 오브젝트가 아니라 기기마다 하나씩만 있고, 상대 기기의 패널은 내 화면에 없다.
/// 플레이어가 조금씩 움직일 때마다 흔들리지 않게, 리그 위치가 충분히 바뀌었을 때만 옮긴다.
/// </summary>
public class LanSessionPanelFollower : MonoBehaviour
{
    [SerializeField, Tooltip("진행자. 패널은 플레이어 기준으로 이 반대쪽에 놓인다. 비워 두면 플레이어 오른쪽")] Transform m_Host;
    [SerializeField, Tooltip("플레이어에서 옆으로 떨어지는 거리(m)")] float m_SideDistance = 0.9f;
    [SerializeField, Tooltip("플레이어에서 앞으로 떨어지는 거리(m). 0이면 바로 옆, 양수면 조금 앞")] float m_ForwardDistance = 0.15f;
    [SerializeField, Tooltip("패널 높이(m)")] float m_Height = 1.35f;
    [SerializeField, Tooltip("패널이 이 눈높이(m)를 바라본다")] float m_EyeHeight = 1.6f;
    [SerializeField, Tooltip("리그가 이만큼(m) 넘게 움직이면 패널을 다시 놓는다")] float m_MoveThreshold = 0.05f;
    [SerializeField, Tooltip("리그가 이만큼(도) 넘게 돌면 패널을 다시 놓는다")] float m_TurnThreshold = 5f;

    XROrigin m_Origin;
    Vector3 m_LastOriginPosition;
    Quaternion m_LastOriginRotation;
    bool m_Placed;

    void Start()
    {
        m_Origin = FindAnyObjectByType<XROrigin>();
        if (m_Origin == null)
            Debug.LogWarning("[LanSessionPanelFollower] No XR Origin in the scene", this);
    }

    void LateUpdate()
    {
        if (m_Origin == null)
            return;

        var origin = m_Origin.transform;
        var moved = !m_Placed
            || (origin.position - m_LastOriginPosition).sqrMagnitude > m_MoveThreshold * m_MoveThreshold
            || Quaternion.Angle(origin.rotation, m_LastOriginRotation) > m_TurnThreshold;
        if (!moved)
            return;

        Place(origin);
        m_LastOriginPosition = origin.position;
        m_LastOriginRotation = origin.rotation;
        m_Placed = true;
    }

    void Place(Transform origin)
    {
        // 리그(origin)의 축은 플레이어가 보는 방향과 다를 수 있다. 좌석에 앉힐 때 카메라가 좌석 정면을 보도록 리그를 돌리기 때문에,
        // 리그가 이미 돌아가 있던 만큼 리그 축이 틀어진다. 그래서 방향과 자리는 카메라(머리) 기준으로 잰다
        var camera = m_Origin.Camera != null ? m_Origin.Camera.transform : origin;
        var forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(origin.forward, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, forward).normalized;
        var feet = new Vector3(camera.position.x, origin.position.y, camera.position.z);

        // 진행자가 플레이어의 왼쪽에 있으면 오른쪽에, 오른쪽에 있으면 왼쪽에 둔다
        var side = right;
        if (m_Host != null && Vector3.Dot(m_Host.position - feet, side) > 0f)
            side = -side;

        var position = feet + side * m_SideDistance + forward * m_ForwardDistance + Vector3.up * m_Height;
        var eye = feet + Vector3.up * m_EyeHeight;
        var toPanel = Vector3.ProjectOnPlane(position - eye, Vector3.up);
        if (toPanel.sqrMagnitude < 0.0001f)
            toPanel = side;
        // 앞면(-Z)이 플레이어를 본다
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(toPanel, Vector3.up));
    }
}
