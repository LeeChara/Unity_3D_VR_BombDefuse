using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// 플레이어의 맞을 수 있는 몸. XR Origin에 붙이면 시작할 때 히트박스를 만든다.
/// 머리(구)는 카메라를 따라가고, 몸통(캡슐)은 카메라 아래에 붙어 고개 방향(수평)만 따라 돈다.
/// 자신 쏘기 판정과 피해에 쓴다. 팔 히트박스와 아바타 연결은 멀티 아바타가 생기면 한다.
/// </summary>
[RequireComponent(typeof(XROrigin), typeof(Damageable))]
public class PlayerBody : MonoBehaviour
{
    [SerializeField, Tooltip("머리 구의 반지름(m)")] float m_HeadRadius = 0.11f;
    [SerializeField, Tooltip("눈에서 몸통 중심까지 아래로(m)")] float m_TorsoDrop = 0.42f;
    [SerializeField, Tooltip("몸통 중심을 눈보다 뒤로(m)")] float m_TorsoBack = 0.06f;
    [SerializeField] float m_TorsoRadius = 0.16f;
    [SerializeField] float m_TorsoHeight = 0.55f;

    XROrigin m_Origin;
    Transform m_Torso;

    public Damageable Damageable { get; private set; }

    void Awake()
    {
        m_Origin = GetComponent<XROrigin>();
        Damageable = GetComponent<Damageable>();

        var camera = m_Origin.Camera.transform;

        var head = new GameObject("Hitbox_Head");
        head.transform.SetParent(camera, false);
        var headCollider = head.AddComponent<SphereCollider>();
        headCollider.radius = m_HeadRadius;
        head.AddComponent<Hitbox>().SetPart(BodyPart.Head);

        // 몸통은 카메라 자식으로 두면 고개를 숙일 때 같이 기울어서, 리그 아래에 두고 위치만 따라가게 한다
        var torso = new GameObject("Hitbox_Torso");
        torso.transform.SetParent(transform, false);
        var torsoCollider = torso.AddComponent<CapsuleCollider>();
        torsoCollider.radius = m_TorsoRadius;
        torsoCollider.height = m_TorsoHeight;
        torso.AddComponent<Hitbox>().SetPart(BodyPart.Torso);
        m_Torso = torso.transform;

        // 리그의 이동용 캡슐(CharacterController)이 자기 히트박스에 걸리지 않게 한다
        if (TryGetComponent<CharacterController>(out var controller))
        {
            Physics.IgnoreCollision(controller, headCollider);
            Physics.IgnoreCollision(controller, torsoCollider);
        }
    }

    void LateUpdate()
    {
        var camera = m_Origin.Camera.transform;
        var forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(camera.up, Vector3.up);
        forward.Normalize();

        m_Torso.SetPositionAndRotation(
            camera.position + Vector3.down * m_TorsoDrop - forward * m_TorsoBack,
            Quaternion.LookRotation(forward, Vector3.up));
    }
}
