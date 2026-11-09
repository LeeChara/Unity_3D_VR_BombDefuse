using UnityEngine;

/// <summary>
/// 테스트용 마네킹의 겉모습. 상대 자리에 앉아 있고, 퀴즈는 풀지 않는다.
/// 맞으면 색이 바뀐다: 중상은 붉은색, 사망은 검은색. 상대 플레이어가 생기면 지운다.
/// </summary>
[RequireComponent(typeof(Damageable))]
public class TestMannequin : MonoBehaviour
{
    static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

    [SerializeField] Color m_NormalColor = new Color(0.75f, 0.72f, 0.68f);
    [SerializeField] Color m_WoundedColor = new Color(0.55f, 0.08f, 0.06f);
    [SerializeField] Color m_DeadColor = new Color(0.08f, 0.07f, 0.07f);
    [SerializeField, Min(0f), Tooltip("죽은 뒤 되살아나기까지(초). 0이면 되살아나지 않는다 (게임 진행 컨트롤러가 다시 시작할 때, 또는 Damageable 메뉴의 Revive로 되살림)")]
    float m_ReviveDelay;

    Damageable m_Damageable;
    Renderer[] m_Renderers;
    MaterialPropertyBlock m_Block;

    void Awake()
    {
        m_Damageable = GetComponent<Damageable>();
        m_Renderers = GetComponentsInChildren<Renderer>();
        m_Block = new MaterialPropertyBlock();
        SetColor(m_NormalColor);
    }

    void OnEnable()
    {
        m_Damageable.Damaged += OnDamaged;
        m_Damageable.Revived += OnRevived;
    }

    void OnDisable()
    {
        m_Damageable.Damaged -= OnDamaged;
        m_Damageable.Revived -= OnRevived;
    }

    void OnDamaged(BodyPart part, DamageOutcome outcome)
    {
        if (outcome == DamageOutcome.Killed)
        {
            SetColor(m_DeadColor);
            if (m_ReviveDelay > 0f)
                Invoke(nameof(Revive), m_ReviveDelay);
        }
        else
        {
            SetColor(m_WoundedColor);
        }
    }

    void Revive() => m_Damageable.Revive();

    void OnRevived()
    {
        CancelInvoke(nameof(Revive));
        SetColor(m_NormalColor);
    }

    void SetColor(Color color)
    {
        m_Block.SetColor(k_BaseColor, color);
        foreach (var r in m_Renderers)
            r.SetPropertyBlock(m_Block);
    }
}
