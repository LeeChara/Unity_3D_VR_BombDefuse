using UnityEngine;

/// <summary>
/// 총의 장전 영역 (총 자식의 트리거 콜라이더). 손에서 놓인 <see cref="SpareBullet"/>이 닿아 있으면 총에 넣는다.
/// 총알을 총에 대고 그립을 놓거나, 총을 테이블 위 총알에 갖다 대면 들어간다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GunLoadZone : MonoBehaviour
{
    [SerializeField] Gun m_Gun;

    void Reset() => m_Gun = GetComponentInParent<Gun>();

    void OnTriggerStay(Collider other)
    {
        if (m_Gun == null)
            return;

        var bullet = other.GetComponentInParent<SpareBullet>();
        if (bullet != null && !bullet.IsHeld)
            m_Gun.TryInsert(bullet);
    }
}
