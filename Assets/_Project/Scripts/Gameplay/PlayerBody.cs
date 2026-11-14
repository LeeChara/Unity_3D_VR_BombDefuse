using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

/// <summary>
/// 플레이어의 맞을 수 있는 몸. XR Origin에 붙이면 시작할 때 히트박스를 만든다.
/// 머리(구)는 카메라를 따라가고, 몸통(캡슐)은 카메라 아래에 붙어 고개 방향(수평)만 따라 돈다.
/// 팔(캡슐)은 어깨에서 손(손 추적 중이면 손, 아니면 컨트롤러)까지 이어진다 (<see cref="ArmHitboxes"/>).
/// 자신 쏘기 판정과 피해에 쓴다. 상대 기기에서는 아바타의 <see cref="AvatarBody"/>가 같은 모양으로 맞는다.
/// 몸통과 팔은 히트박스와 같은 크기의 캡슐로 그려서, 내려다보면 내 몸이 보인다 (머리는 시야를 가리므로 그리지 않는다).
/// </summary>
[RequireComponent(typeof(XROrigin), typeof(Damageable))]
public class PlayerBody : MonoBehaviour
{
    [SerializeField, Tooltip("머리 구의 반지름(m)")] float m_HeadRadius = 0.11f;
    [SerializeField, Tooltip("눈에서 몸통 중심까지 아래로(m)")] float m_TorsoDrop = 0.42f;
    [SerializeField, Tooltip("몸통 중심을 눈보다 뒤로(m)")] float m_TorsoBack = 0.06f;
    [SerializeField] float m_TorsoRadius = 0.16f;
    [SerializeField] float m_TorsoHeight = 0.55f;
    [SerializeField] ArmHitboxes m_Arms = new ArmHitboxes();
    [SerializeField, Tooltip("내 몸통·팔 캡슐의 재질 (상대 아바타와 같은 것)")] Material m_ShapeMaterial;

    XROrigin m_Origin;
    Transform m_Torso;
    XRInputModalityManager m_InputModality;

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
        AvatarBody.CreateShape(m_Torso, new Vector3(m_TorsoRadius * 2f, m_TorsoHeight * 0.5f, m_TorsoRadius * 2f), m_ShapeMaterial);

        m_Arms.Create(camera, transform);
        AvatarBody.CreateShape(m_Arms.GetArm(0), Vector3.one * m_Arms.Radius * 2f, m_ShapeMaterial);
        AvatarBody.CreateShape(m_Arms.GetArm(1), Vector3.one * m_Arms.Radius * 2f, m_ShapeMaterial);
        m_InputModality = GetComponentInChildren<XRInputModalityManager>(true);

        // 리그의 이동용 캡슐(CharacterController)이 자기 히트박스에 걸리지 않게 한다
        if (TryGetComponent<CharacterController>(out var controller))
        {
            Physics.IgnoreCollision(controller, headCollider);
            Physics.IgnoreCollision(controller, torsoCollider);
            foreach (var arm in m_Arms.Colliders)
                Physics.IgnoreCollision(controller, arm);
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
        m_Arms.Update(GetHand(true), GetHand(false));
    }

    // 손 추적 중이면 손, 아니면 컨트롤러 (NetworkPlayer가 아바타 손으로 보내는 것과 같다)
    Transform GetHand(bool left)
    {
        if (m_InputModality == null)
            return null;

        var hand = left ? m_InputModality.leftHand : m_InputModality.rightHand;
        var controller = left ? m_InputModality.leftController : m_InputModality.rightController;
        if (hand != null && hand.activeInHierarchy)
            return hand.transform;
        return controller != null && controller.activeInHierarchy ? controller.transform : null;
    }
}
