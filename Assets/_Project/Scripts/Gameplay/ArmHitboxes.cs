using System;
using UnityEngine;

/// <summary>
/// 양팔 히트박스. 어깨(머리 위치와 고개 방향으로 어림한 곳)에서 손까지 캡슐을 매 프레임 맞춘다.
/// 팔은 필수 버전에서 몸통과 같이 처리한다 (<see cref="BodyPart.Arm"/>, docs/01_rules_essential.md 6절).
/// <see cref="PlayerBody"/>와 <see cref="AvatarBody"/>가 같이 쓴다. 손 위치를 모르면 그 팔은 끈다.
/// </summary>
[Serializable]
public class ArmHitboxes
{
    [Tooltip("눈에서 어깨까지 아래로(m)")] public float ShoulderDrop = 0.22f;
    [Tooltip("몸 가운데에서 어깨까지 옆으로(m)")] public float ShoulderWidth = 0.18f;
    [Tooltip("어깨를 눈보다 뒤로(m)")] public float ShoulderBack = 0.06f;
    [Tooltip("팔 캡슐 반지름(m)")] public float Radius = 0.05f;
    [Tooltip("보이는 팔 캡슐을 손보다 이만큼 짧게(m). 히트박스 길이는 그대로다. 내 몸은 손 모델을 가리지 않게 띄운다")] public float ShapeHandGap;

    Transform m_Head;
    readonly Transform[] m_Arms = new Transform[2];
    readonly CapsuleCollider[] m_Colliders = new CapsuleCollider[2];

    public Collider[] Colliders => m_Colliders;

    /// <summary>팔 오브젝트(자식에 모양을 붙일 수 있다). 0 = 왼팔, 1 = 오른팔.</summary>
    public Transform GetArm(int index) => m_Arms[index];

    /// <summary>히트박스를 만든다. 팔은 parent 아래에 두고 위치만 맞춘다 (머리 자식이면 고개를 숙일 때 같이 기운다).</summary>
    public void Create(Transform head, Transform parent)
    {
        m_Head = head;
        for (var i = 0; i < 2; i++)
        {
            var arm = new GameObject(i == 0 ? "Hitbox_LeftArm" : "Hitbox_RightArm");
            arm.transform.SetParent(parent, false);
            var capsule = arm.AddComponent<CapsuleCollider>();
            capsule.radius = Radius;
            capsule.direction = 1;
            arm.AddComponent<Hitbox>().SetPart(BodyPart.Arm);
            m_Arms[i] = arm.transform;
            m_Colliders[i] = capsule;
        }
    }

    /// <summary>어깨에서 손까지 맞춘다. 손이 null이면 그 팔을 끈다.</summary>
    public void Update(Transform leftHand, Transform rightHand)
    {
        if (m_Head == null)
            return;

        var forward = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(m_Head.up, Vector3.up);
        forward.Normalize();
        var right = Vector3.Cross(Vector3.up, forward);
        var chest = m_Head.position + Vector3.down * ShoulderDrop - forward * ShoulderBack;

        Place(0, chest - right * ShoulderWidth, leftHand);
        Place(1, chest + right * ShoulderWidth, rightHand);
    }

    void Place(int index, Vector3 shoulder, Transform hand)
    {
        var arm = m_Arms[index];
        if (arm == null)
            return;

        var active = hand != null && hand.gameObject.activeInHierarchy;
        if (arm.gameObject.activeSelf != active)
            arm.gameObject.SetActive(active);
        if (!active)
            return;

        var toHand = hand.position - shoulder;
        var length = toHand.magnitude;
        if (length < 0.001f)
            return;

        arm.SetPositionAndRotation(shoulder + toHand * 0.5f, Quaternion.FromToRotation(Vector3.up, toHand / length));
        // 캡슐은 반지름만큼 끝이 둥글게 나오므로, 손 끝까지 덮도록 길이에 지름을 더한다
        m_Colliders[index].height = length + Radius * 2f;
        if (arm.childCount > 0)
        {
            // 손 쪽 끝만 줄이고 어깨 쪽 끝은 그대로 둔다
            var gap = Mathf.Min(ShapeHandGap, length);
            var shape = arm.GetChild(0);
            shape.localPosition = Vector3.down * (gap * 0.5f);
            shape.localScale = new Vector3(Radius * 2f, (length - gap + Radius * 2f) * 0.5f, Radius * 2f);
        }
    }

    public void SetEnabled(bool enabled)
    {
        foreach (var capsule in m_Colliders)
            if (capsule != null)
                capsule.enabled = enabled;
    }
}
