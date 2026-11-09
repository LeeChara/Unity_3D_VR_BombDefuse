using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 쏜 것을 알 수 있게 하는 임시 이펙트. 리볼버 모델과 효과음이 오면 바꾼다.
/// 방아쇠: 공이치기 소리 + 약한 진동.
/// 실탄: 발사음 + 강한 진동 + 큰 반동 + 총구 섬광(빛) + 궤적(선) + 맞은 지점 표시(작은 구).
/// 공포탄: 딸깍 소리 + 중간 진동 + 작은 반동 + 총구에서 퍼지는 회색 연기 표시.
/// 소리 클립을 비워 두면 코드로 만든 임시 소리를 쓴다.
/// </summary>
[RequireComponent(typeof(Gun))]
public class GunEffects : MonoBehaviour
{
    static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

    [SerializeField, Tooltip("궤적을 그릴 LineRenderer (월드 좌표)")] LineRenderer m_Tracer;
    [SerializeField, Tooltip("총구 섬광 빛. 평소에는 꺼 둔다")] Light m_MuzzleFlash;
    [SerializeField, Tooltip("반동으로 흔들 모델 (총 자식). 콜라이더와 함께 움직인다")] Transform m_Model;

    [Header("Timing")]
    [SerializeField] float m_TracerDuration = 0.15f;
    [SerializeField] float m_FlashDuration = 0.06f;
    [SerializeField] float m_ImpactDuration = 0.4f;
    [SerializeField] float m_BlankDuration = 0.35f;

    [Header("Marker")]
    [SerializeField, Tooltip("맞은 지점에 띄울 구의 크기(m)")] float m_ImpactSize = 0.04f;
    [SerializeField] Color m_ImpactColor = new Color(1f, 0.55f, 0.1f);
    [SerializeField, Tooltip("공포탄일 때 총구에서 퍼지는 구의 처음·마지막 크기(m)")] Vector2 m_BlankSize = new Vector2(0.02f, 0.09f);
    [SerializeField] Color m_BlankColor = new Color(0.75f, 0.75f, 0.75f);

    [Header("Recoil (반동: 총구가 들리는 각도, 뒤로 밀리는 거리, 돌아오는 시간)")]
    [SerializeField] float m_LiveKickAngle = 20f;
    [SerializeField] float m_LiveKickBack = 0.03f;
    [SerializeField] float m_LiveKickDuration = 0.2f;
    [SerializeField] float m_BlankKickAngle = 6f;
    [SerializeField] float m_BlankKickBack = 0.008f;
    [SerializeField] float m_BlankKickDuration = 0.12f;

    [Header("Haptics (세기 0~1, 시간 초)")]
    [SerializeField] Vector2 m_HammerHaptic = new Vector2(0.25f, 0.04f);
    [SerializeField] Vector2 m_BlankHaptic = new Vector2(0.6f, 0.08f);
    [SerializeField] Vector2 m_LiveHaptic = new Vector2(1f, 0.25f);

    [Header("Sound (비워 두면 임시 소리)")]
    [SerializeField] AudioClip m_HammerClip;
    [SerializeField] AudioClip m_BlankClip;
    [SerializeField] AudioClip m_LiveClip;
    [SerializeField, Range(0f, 1f)] float m_Volume = 0.9f;

    Gun m_Gun;
    AudioSource m_Audio;
    Transform m_Marker;
    Renderer m_MarkerRenderer;
    MaterialPropertyBlock m_Block;
    float m_TracerHideTime;
    float m_FlashHideTime;
    float m_MarkerStartTime;
    float m_MarkerDuration;
    Vector2 m_MarkerSize;

    Vector3 m_ModelRestPosition;
    Quaternion m_ModelRestRotation;
    Vector3 m_KickAxis;
    Vector3 m_KickBackDirection;
    float m_KickStartTime;
    float m_KickDuration;
    float m_KickAngle;
    float m_KickBack;

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

        m_Audio = gameObject.AddComponent<AudioSource>();
        m_Audio.playOnAwake = false;
        m_Audio.spatialBlend = 1f;
        m_Audio.minDistance = 0.5f;
        m_Audio.maxDistance = 15f;
        if (m_HammerClip == null)
            m_HammerClip = TempSounds.Hammer();
        if (m_BlankClip == null)
            m_BlankClip = TempSounds.Blank();
        if (m_LiveClip == null)
            m_LiveClip = TempSounds.Gunshot();

        if (m_Model != null)
        {
            m_ModelRestPosition = m_Model.localPosition;
            m_ModelRestRotation = m_Model.localRotation;
        }
    }

    void OnDestroy()
    {
        if (m_Marker != null)
            Destroy(m_Marker.gameObject);
    }

    void OnEnable()
    {
        m_Gun.HammerCocked += OnHammerCocked;
        m_Gun.Fired += OnFired;
    }

    void OnDisable()
    {
        m_Gun.HammerCocked -= OnHammerCocked;
        m_Gun.Fired -= OnFired;
        if (m_Tracer != null)
            m_Tracer.enabled = false;
        if (m_MuzzleFlash != null)
            m_MuzzleFlash.enabled = false;
        if (m_Marker != null)
            m_Marker.gameObject.SetActive(false);
        m_KickDuration = 0f;
        if (m_Model != null)
            m_Model.SetLocalPositionAndRotation(m_ModelRestPosition, m_ModelRestRotation);
    }

    void OnHammerCocked()
    {
        m_Audio.PlayOneShot(m_HammerClip, m_Volume * 0.6f);
        Haptic(m_HammerHaptic);
    }

    void OnFired(Gun.ShotResult result)
    {
        if (!result.IsLive)
        {
            m_Audio.PlayOneShot(m_BlankClip, m_Volume);
            Haptic(m_BlankHaptic);
            Kick(m_BlankKickAngle, m_BlankKickBack, m_BlankKickDuration);
            ShowMarker(result.Origin, m_BlankSize, m_BlankColor, m_BlankDuration);
            return;
        }

        m_Audio.PlayOneShot(m_LiveClip, m_Volume);
        Haptic(m_LiveHaptic);
        Kick(m_LiveKickAngle, m_LiveKickBack, m_LiveKickDuration);

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
            ShowMarker(result.EndPoint, new Vector2(m_ImpactSize, m_ImpactSize), m_ImpactColor, m_ImpactDuration);
    }

    void Haptic(Vector2 amplitudeAndDuration)
    {
        if (m_Gun.HoldingHand is XRBaseInputInteractor hand)
            hand.SendHapticImpulse(amplitudeAndDuration.x, amplitudeAndDuration.y);
    }

    void Kick(float angle, float back, float duration)
    {
        if (m_Model == null || duration <= 0f)
            return;

        // 총구 방향(모델 기준)에서 위로 들리는 회전축과 뒤쪽을 구한다
        var forward = transform.InverseTransformDirection(m_Gun.Muzzle.forward);
        var up = transform.InverseTransformDirection(m_Gun.Muzzle.up);
        m_KickAxis = Vector3.Cross(forward, up).normalized;
        m_KickBackDirection = -forward;
        m_KickAngle = angle;
        m_KickBack = back;
        m_KickDuration = duration;
        m_KickStartTime = Time.time;
    }

    void ShowMarker(Vector3 position, Vector2 size, Color color, float duration)
    {
        m_Marker.position = position;
        m_Marker.localScale = Vector3.one * size.x;
        m_Block.SetColor(k_BaseColor, color);
        m_MarkerRenderer.SetPropertyBlock(m_Block);
        m_Marker.gameObject.SetActive(true);
        m_MarkerStartTime = Time.time;
        m_MarkerDuration = duration;
        m_MarkerSize = size;
    }

    void Update()
    {
        if (m_Tracer != null && m_Tracer.enabled && Time.time >= m_TracerHideTime)
            m_Tracer.enabled = false;
        if (m_MuzzleFlash != null && m_MuzzleFlash.enabled && Time.time >= m_FlashHideTime)
            m_MuzzleFlash.enabled = false;

        if (m_Marker.gameObject.activeSelf)
        {
            var t = m_MarkerDuration > 0f ? (Time.time - m_MarkerStartTime) / m_MarkerDuration : 1f;
            if (t >= 1f)
                m_Marker.gameObject.SetActive(false);
            else
                m_Marker.localScale = Vector3.one * Mathf.Lerp(m_MarkerSize.x, m_MarkerSize.y, t);
        }

        UpdateKick();
    }

    void UpdateKick()
    {
        if (m_Model == null || m_KickDuration <= 0f)
            return;

        // 순간 튀었다가 점점 원래 자리로 돌아온다
        var t = (Time.time - m_KickStartTime) / m_KickDuration;
        if (t >= 1f)
        {
            m_KickDuration = 0f;
            m_Model.SetLocalPositionAndRotation(m_ModelRestPosition, m_ModelRestRotation);
            return;
        }

        var weight = (1f - t) * (1f - t);
        var rotation = Quaternion.AngleAxis(m_KickAngle * weight, m_KickAxis);
        m_Model.SetLocalPositionAndRotation(
            m_ModelRestPosition + m_KickBackDirection * (m_KickBack * weight),
            rotation * m_ModelRestRotation);
    }

    /// <summary>효과음이 오기 전까지 쓰는 임시 소리. 코드로 파형을 만든다.</summary>
    static class TempSounds
    {
        const int k_Rate = 44100;

        public static AudioClip Hammer() => Make("TempHammer", 0.08f, 1, (t, rnd) =>
            Click(t, 0f, 0.012f, 3200f, rnd) * 0.7f + Click(t, 0.035f, 0.01f, 2600f, rnd) * 0.5f);

        // 딸깍: 날카로운 금속음 두 번
        public static AudioClip Blank() => Make("TempBlank", 0.15f, 2, (t, rnd) =>
            Click(t, 0f, 0.02f, 2400f, rnd) + Click(t, 0.05f, 0.015f, 1800f, rnd) * 0.6f);

        // 탕: 짧은 저음 충격 + 길게 사라지는 잡음
        public static AudioClip Gunshot() => Make("TempGunshot", 0.6f, 3, (t, rnd) =>
        {
            var noise = (float)(rnd.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t / 0.07f);
            var thump = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t / 0.05f);
            return noise * 0.8f + thump * 0.9f;
        });

        static float Click(float t, float start, float length, float frequency, System.Random rnd)
        {
            var local = t - start;
            if (local < 0f || local > length * 4f)
                return 0f;
            var envelope = Mathf.Exp(-local / length);
            var noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
            return (Mathf.Sin(2f * Mathf.PI * frequency * local) * 0.6f + noise * 0.4f) * envelope;
        }

        static AudioClip Make(string name, float seconds, int seed, System.Func<float, System.Random, float> wave)
        {
            var rnd = new System.Random(seed);
            var samples = new float[Mathf.CeilToInt(seconds * k_Rate)];
            var smoothed = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                // 아주 약한 저역 통과로 거친 소리를 조금 누그러뜨린다
                smoothed = Mathf.Lerp(smoothed, wave((float)i / k_Rate, rnd), 0.6f);
                samples[i] = Mathf.Clamp(smoothed, -1f, 1f);
            }

            var clip = AudioClip.Create(name, samples.Length, 1, k_Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
