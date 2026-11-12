using UnityEngine;

/// <summary>
/// 상대 플레이어 아바타의 맞을 수 있는 몸. <see cref="PlayerBody"/>와 같은 모양의 히트박스를 아바타 머리 기준으로 만든다.
/// 머리(구)는 아바타 머리를 따라가고, 몸통(캡슐)은 머리 아래에 붙어 고개 방향(수평)만 따라 돈다.
/// 자기 아바타(그 기기의 플레이어 본인)에서는 <see cref="SetHitboxesEnabled"/>로 끈다. 본인의 몸은 XR Origin의 PlayerBody가 맡는다.
/// </summary>
[RequireComponent(typeof(Damageable))]
public class AvatarBody : MonoBehaviour
{
    [SerializeField, Tooltip("따라갈 머리 (NetworkPlayer의 Head)")] Transform m_Head;
    [SerializeField, Tooltip("머리 구의 반지름(m)")] float m_HeadRadius = 0.11f;
    [SerializeField, Tooltip("눈에서 몸통 중심까지 아래로(m)")] float m_TorsoDrop = 0.42f;
    [SerializeField, Tooltip("몸통 중심을 눈보다 뒤로(m)")] float m_TorsoBack = 0.06f;
    [SerializeField] float m_TorsoRadius = 0.16f;
    [SerializeField] float m_TorsoHeight = 0.55f;

    Transform m_Torso;
    Collider[] m_Colliders;

    public Damageable Damageable { get; private set; }

    void Awake()
    {
        Damageable = GetComponent<Damageable>();

        var head = new GameObject("Hitbox_Head");
        head.transform.SetParent(m_Head, false);
        var headCollider = head.AddComponent<SphereCollider>();
        headCollider.radius = m_HeadRadius;
        head.AddComponent<Hitbox>().SetPart(BodyPart.Head);

        // 몸통은 머리 자식으로 두면 고개를 숙일 때 같이 기울어서, 아바타 아래에 두고 위치만 따라가게 한다
        var torso = new GameObject("Hitbox_Torso");
        torso.transform.SetParent(transform, false);
        var torsoCollider = torso.AddComponent<CapsuleCollider>();
        torsoCollider.radius = m_TorsoRadius;
        torsoCollider.height = m_TorsoHeight;
        torso.AddComponent<Hitbox>().SetPart(BodyPart.Torso);
        m_Torso = torso.transform;

        m_Colliders = new Collider[] { headCollider, torsoCollider };
    }

    /// <summary>히트박스를 켜고 끈다. 자기 아바타에서는 꺼서 XR Origin의 몸과 겹치지 않게 한다.</summary>
    public void SetHitboxesEnabled(bool enabled)
    {
        foreach (var hitboxCollider in m_Colliders)
            hitboxCollider.enabled = enabled;
    }

    void LateUpdate()
    {
        var forward = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(m_Head.up, Vector3.up);
        forward.Normalize();

        m_Torso.SetPositionAndRotation(
            m_Head.position + Vector3.down * m_TorsoDrop - forward * m_TorsoBack,
            Quaternion.LookRotation(forward, Vector3.up));
    }
}
