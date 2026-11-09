using UnityEngine;

/// <summary>
/// 몸의 한 부위. 같은 오브젝트의 콜라이더가 총에 맞으면 부모의 <see cref="Damageable"/>에 알린다.
/// 총의 레이캐스트는 트리거 콜라이더를 무시하므로, 콜라이더는 Is Trigger를 끈다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Hitbox : MonoBehaviour
{
    [SerializeField] BodyPart m_Part = BodyPart.Torso;

    Damageable m_Owner;

    public BodyPart Part => m_Part;
    public Damageable Owner => m_Owner != null ? m_Owner : m_Owner = GetComponentInParent<Damageable>();

    /// <summary>코드로 만든 히트박스의 부위를 정한다.</summary>
    public void SetPart(BodyPart part) => m_Part = part;

    public DamageOutcome ApplyHit() => Owner != null ? Owner.ApplyHit(m_Part) : DamageOutcome.Ignored;
}
