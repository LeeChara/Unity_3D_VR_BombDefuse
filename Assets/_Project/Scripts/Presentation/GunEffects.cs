using UnityEngine;

/// <summary>
/// 쏜 것을 알 수 있게 하는 임시 이펙트. 리볼버 모델과 효과음이 오면 바꾼다.
/// 실탄: 총구 섬광(빛) + 궤적(선) + 맞은 지점 표시(작은 구). 공포탄: 총구에 회색 표시만 잠깐.
/// </summary>
[RequireComponent(typeof(Gun))]
public class GunEffects : MonoBehaviour
{
    static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

    [SerializeField, Tooltip("궤적을 그릴 LineRenderer (월드 좌표)")] LineRenderer m_Tracer;
    [SerializeField, Tooltip("총구 섬광 빛. 평소에는 꺼 둔다")] Light m_MuzzleFlash;

    [Header("Timing")]
    [SerializeField] float m_TracerDuration = 0.15f;
    [SerializeField] float m_FlashDuration = 0.06f;
    [SerializeField] float m_ImpactDuration = 0.4f;
    [SerializeField] float m_BlankDuration = 0.15f;

    [Header("Marker")]
    [SerializeField, Tooltip("맞은 지점에 띄울 구의 크기(m)")] float m_ImpactSize = 0.04f;
    [SerializeField] Color m_ImpactColor = new Color(1f, 0.55f, 0.1f);
    [SerializeField, Tooltip("공포탄일 때 총구에 띄울 구의 크기(m)")] float m_BlankSize = 0.03f;
    [SerializeField] Color m_BlankColor = new Color(0.6f, 0.6f, 0.6f);

    Gun m_Gun;
    Transform m_Marker;
    Renderer m_MarkerRenderer;
    MaterialPropertyBlock m_Block;
    float m_TracerHideTime;
    float m_FlashHideTime;
    float m_MarkerHideTime;

    void Awake()
    {
        m_Gun = GetComponent<Gun>();
        m_Block = new MaterialPropertyBlock();

        if (m_Tracer != null)
            m_Tracer.enabled = false;
        if (m_MuzzleFlash != null)
            m_MuzzleFlash.enabled = false;

        // 맞은 지점 표시는 총이 움직여도 제자리에 있어야 해서 총 밖(씬 최상위)에 둔다
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = $"{name}_ShotMarker";
        Destroy(marker.GetComponent<Collider>());
        m_MarkerRenderer = marker.GetComponent<Renderer>();
        if (m_Tracer != null)
            m_MarkerRenderer.sharedMaterial = m_Tracer.sharedMaterial;   // 빛을 받지 않는 Unlit 재질을 같이 쓴다
        m_MarkerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        m_Marker = marker.transform;
        marker.SetActive(false);
    }

    void OnDestroy()
    {
        if (m_Marker != null)
            Destroy(m_Marker.gameObject);
    }

    void OnEnable() => m_Gun.Fired += OnFired;

    void OnDisable()
    {
        m_Gun.Fired -= OnFired;
        if (m_Tracer != null)
            m_Tracer.enabled = false;
        if (m_MuzzleFlash != null)
            m_MuzzleFlash.enabled = false;
        if (m_Marker != null)
            m_Marker.gameObject.SetActive(false);
    }

    void OnFired(Gun.ShotResult result)
    {
        if (!result.IsLive)
        {
            ShowMarker(result.Origin, m_BlankSize, m_BlankColor, m_BlankDuration);
            return;
        }

        if (m_Tracer != null)
        {
            m_Tracer.positionCount = 2;
            m_Tracer.SetPosition(0, result.Origin);
            m_Tracer.SetPosition(1, result.EndPoint);
            m_Tracer.enabled = true;
            m_TracerHideTime = Time.time + m_TracerDuration;
        }

        if (m_MuzzleFlash != null)
        {
            m_MuzzleFlash.enabled = true;
            m_FlashHideTime = Time.time + m_FlashDuration;
        }

        if (result.DidHit)
            ShowMarker(result.EndPoint, m_ImpactSize, m_ImpactColor, m_ImpactDuration);
    }

    void ShowMarker(Vector3 position, float size, Color color, float duration)
    {
        m_Marker.position = position;
        m_Marker.localScale = Vector3.one * size;
        m_Block.SetColor(k_BaseColor, color);
        m_MarkerRenderer.SetPropertyBlock(m_Block);
        m_Marker.gameObject.SetActive(true);
        m_MarkerHideTime = Time.time + duration;
    }

    void Update()
    {
        if (m_Tracer != null && m_Tracer.enabled && Time.time >= m_TracerHideTime)
            m_Tracer.enabled = false;
        if (m_MuzzleFlash != null && m_MuzzleFlash.enabled && Time.time >= m_FlashHideTime)
            m_MuzzleFlash.enabled = false;
        if (m_Marker.gameObject.activeSelf && Time.time >= m_MarkerHideTime)
            m_Marker.gameObject.SetActive(false);
    }
}
