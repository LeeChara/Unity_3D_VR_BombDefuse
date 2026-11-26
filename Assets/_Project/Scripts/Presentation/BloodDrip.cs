using UnityEngine;

/// <summary>
/// 절단된 팔 끝(<see cref="ArmHitboxes"/>의 절단면)에 붙인다. 켜지는 순간 피가 튀고, 켜져 있는 동안 핏방울이 떨어진다.
/// 핏방울은 작은 구를 몇 개 돌려 쓴다. 절단면이 보이지 않으면(자기 아바타라 렌더러가 꺼져 있으면) 떨어뜨리지 않는다.
/// </summary>
public class BloodDrip : MonoBehaviour
{
    static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int k_Color = Shader.PropertyToID("_Color");

    /// <summary>피 색 (절단면도 같은 색을 쓴다).</summary>
    public static readonly Color BloodColor = new Color(0.42f, 0.02f, 0.02f);

    [SerializeField, Tooltip("핏방울 사이 간격(초), 최소~최대")] Vector2 m_DripInterval = new Vector2(0.12f, 0.35f);
    [SerializeField, Tooltip("켜지는 순간 튀는 핏방울 수")] int m_BurstCount = 10;
    [SerializeField, Tooltip("핏방울이 사라지기까지(초)")] float m_Lifetime = 0.9f;
    [SerializeField, Tooltip("핏방울 지름(m)")] float m_DropSize = 0.018f;

    const int k_PoolSize = 24;

    Renderer m_Stump;
    Material m_Material;
    readonly Transform[] m_Drops = new Transform[k_PoolSize];
    readonly Vector3[] m_Velocities = new Vector3[k_PoolSize];
    readonly float[] m_Ages = new float[k_PoolSize];
    int m_Next;
    float m_NextDripTime;
    MaterialPropertyBlock m_Block;

    /// <summary>절단면 렌더러와 핏방울 재질을 정한다. 만든 직후 한 번 부른다.</summary>
    public void Init(Renderer stump, Material material)
    {
        m_Stump = stump;
        m_Material = material;
    }

    /// <summary>렌더러에 피 색을 입힌다 (재질은 그대로 두고 색만 바꾼다).</summary>
    public static void Tint(Renderer target, MaterialPropertyBlock block)
    {
        target.GetPropertyBlock(block);
        block.SetColor(k_BaseColor, BloodColor);
        block.SetColor(k_Color, BloodColor);
        target.SetPropertyBlock(block);
    }

    bool IsVisible => m_Stump == null || m_Stump.enabled;

    void OnEnable()
    {
        m_NextDripTime = Time.time + Random.Range(m_DripInterval.x, m_DripInterval.y);
        if (!IsVisible)
            return;

        for (var i = 0; i < m_BurstCount; i++)
            Emit(Random.onUnitSphere * 1.2f + Vector3.up * 0.6f);
    }

    void OnDisable()
    {
        foreach (var drop in m_Drops)
            if (drop != null)
                drop.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        foreach (var drop in m_Drops)
            if (drop != null)
                Destroy(drop.gameObject);
    }

    void Update()
    {
        if (IsVisible && Time.time >= m_NextDripTime)
        {
            m_NextDripTime = Time.time + Random.Range(m_DripInterval.x, m_DripInterval.y);
            Emit(Random.insideUnitSphere * 0.05f);
        }

        var dt = Time.deltaTime;
        for (var i = 0; i < k_PoolSize; i++)
        {
            var drop = m_Drops[i];
            if (drop == null || !drop.gameObject.activeSelf)
                continue;

            m_Ages[i] += dt;
            if (m_Ages[i] >= m_Lifetime)
            {
                drop.gameObject.SetActive(false);
                continue;
            }

            m_Velocities[i] += Physics.gravity * dt;
            drop.position += m_Velocities[i] * dt;
        }
    }

    void Emit(Vector3 velocity)
    {
        var index = m_Next;
        m_Next = (m_Next + 1) % k_PoolSize;
        var drop = m_Drops[index];
        if (drop == null)
        {
            drop = CreateDrop();
            m_Drops[index] = drop;
        }

        // 핏방울은 팔을 따라 움직이지 않도록 부모 없이 월드에 둔다
        drop.position = transform.position;
        drop.gameObject.SetActive(true);
        m_Velocities[index] = velocity;
        m_Ages[index] = 0f;
    }

    Transform CreateDrop()
    {
        var drop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        drop.name = "BloodDrop";
        Destroy(drop.GetComponent<Collider>());
        drop.transform.localScale = Vector3.one * m_DropSize;
        var dropRenderer = drop.GetComponent<MeshRenderer>();
        if (m_Material != null)
            dropRenderer.sharedMaterial = m_Material;
        dropRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        m_Block ??= new MaterialPropertyBlock();
        Tint(dropRenderer, m_Block);
        return drop.transform;
    }
}
