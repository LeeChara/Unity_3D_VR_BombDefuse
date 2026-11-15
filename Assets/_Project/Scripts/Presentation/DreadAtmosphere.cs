using System;
using UnityEngine;

/// <summary>
/// 방 분위기 연출. 공포 단계(docs/01_rules_essential.md 7절) 하나로 조명·환경광·진행자 조명·배경음을 함께 바꾸고,
/// 진행 상태에 따라 총 조명·긴장 레이어·정적을 더한다.
/// - 공포 단계: <c>dread_changed</c>(새 단계)를 받으면 조명이 잠깐 꺼졌다가 새 단계 조명으로 켜진다. 새 게임(Seating)과 로비에서 0으로 돌아간다.
/// - 총 조명: 총 지급·사수 턴 동안 위에서 총을 비추고 총을 따라간다.
/// - 긴장 레이어: 사수 턴 동안 배경음 위에 겹친다.
/// - 정적: 방아쇠를 당긴 순간(<c>hammer_cock</c>)과 사망(<c>player_died</c>) 때 배경음을 잠깐 죽인다.
/// 이벤트와 상태는 네트워크로 전달되므로 클라이언트에서도 같이 바뀐다. 제한시간·난이도는 진행 쪽(<see cref="GameFlowController"/>)이 바꾼다.
/// </summary>
public class DreadAtmosphere : MonoBehaviour
{
    [Serializable]
    public class Level
    {
        [Tooltip("천장 조명 색")] public Color LightColor = new Color(1f, 0.5f, 0.2f);
        [Tooltip("천장 조명 밝기")] public float LightIntensity = 3f;
        [Range(0f, 1f), Tooltip("깜빡임 세기. 0이면 안 깜빡인다")] public float Flicker;
        [Tooltip("환경광 색 (그림자 속 밝기)")] public Color Ambient = new Color(0.2f, 0.17f, 0.15f);
        [Tooltip("진행자만 비추는 조명 밝기")] public float HostLightIntensity;
        [Tooltip("이 단계의 배경음 (반복). 비우면 조용하다")] public AudioClip Music;
    }

    [SerializeField] GameFlowController m_Flow;
    [SerializeField, Tooltip("방 천장 조명")] Light m_MainLight;
    [SerializeField, Tooltip("진행자만 비추는 조명 (3단계에서 켠다)")] Light m_HostLight;
    [SerializeField, Tooltip("단계 0~3")] Level[] m_Levels =
    {
        new Level { LightColor = new Color(1f, 0.5f, 0.2f), LightIntensity = 1.8f, Flicker = 0f, Ambient = new Color(0.13f, 0.11f, 0.1f) },
        new Level { LightColor = new Color(1f, 0.45f, 0.18f), LightIntensity = 1.8f, Flicker = 0.5f, Ambient = new Color(0.11f, 0.09f, 0.08f) },
        new Level { LightColor = new Color(0.8f, 0.1f, 0.08f), LightIntensity = 2.4f, Flicker = 0.3f, Ambient = new Color(0.12f, 0.05f, 0.05f) },
        new Level { LightColor = new Color(0.8f, 0.08f, 0.06f), LightIntensity = 0.6f, Flicker = 0.4f, Ambient = new Color(0.04f, 0.025f, 0.025f), HostLightIntensity = 6f },
    };
    [SerializeField, Min(0.01f), Tooltip("단계가 바뀔 때 조명이 넘어가는 시간(초)")] float m_TransitionTime = 2f;
    [SerializeField, Min(0f), Tooltip("단계가 오를 때 조명이 완전히 꺼져 있는 시간(초)")] float m_BlackoutTime = 0.8f;

    [Header("Gun Light")]
    [SerializeField, Tooltip("총 지급·사수 턴 동안 총을 위에서 비추는 조명")] Light m_GunLight;
    [SerializeField] float m_GunLightIntensity = 4f;
    [SerializeField, Tooltip("총 위로 조명 높이(m)")] float m_GunLightHeight = 1.6f;
    [SerializeField, Min(0.01f), Tooltip("총 조명이 켜지고 꺼지는 시간(초)")] float m_GunLightFade = 0.6f;

    [Header("Music")]
    [SerializeField, Range(0f, 1f)] float m_MusicVolume = 0.6f;
    [SerializeField, Min(0.01f), Tooltip("배경음이 넘어가는 시간(초)")] float m_MusicFadeTime = 3f;
    [SerializeField, Tooltip("사수 턴 동안 겹치는 긴장 레이어 (반복). 비우면 없음")] AudioClip m_TensionLayer;
    [SerializeField, Range(0f, 1f)] float m_TensionVolume = 0.5f;
    [SerializeField, Min(0f), Tooltip("방아쇠를 당긴 뒤 배경음을 죽이는 시간(초). 공이치기 지연보다 조금 길게")] float m_TriggerSilence = 1.2f;
    [SerializeField, Min(0f), Tooltip("사망 뒤 배경음을 죽이는 시간(초)")] float m_DeathSilence = 4f;
    [SerializeField, Min(0.01f), Tooltip("정적이 끝나고 배경음이 돌아오는 시간(초)")] float m_SilenceRecover = 1.5f;

    int m_Level;
    // 지금 보이는 값 (목표 단계로 천천히 따라간다)
    Color m_LightColor;
    float m_LightIntensity;
    float m_Flicker;
    Color m_Ambient;
    float m_HostIntensity;
    float m_BlackoutUntil;
    float m_FlickerSeed;

    float m_GunLightLevel;
    bool m_GunLightOn;
    Vector3 m_GunLightTarget;

    AudioSource[] m_MusicSources;
    // 정적을 곱하기 전 음량 (페이드 진행 상태)
    readonly float[] m_MusicLevels = new float[2];
    int m_MusicIndex;
    AudioSource m_TensionSource;
    float m_TensionLevel;
    bool m_TensionOn;
    float m_SilenceUntil;
    float m_Duck = 1f;

    public int CurrentLevel => m_Level;

    void Start()
    {
        // 배경음 둘을 번갈아 써서 넘어갈 때 겹쳐 지나가게 한다
        m_MusicSources = new[] { CreateLoopSource(), CreateLoopSource() };
        m_TensionSource = CreateLoopSource();
        m_TensionSource.clip = m_TensionLayer;

        if (m_GunLight != null)
        {
            m_GunLight.enabled = false;
            m_GunLight.intensity = 0f;
        }

        m_FlickerSeed = UnityEngine.Random.value * 100f;
        SetLevel(0, true);

        if (m_Flow != null)
            m_Flow.StateMachine.StateChanged += OnStateChanged;
        GameEvents.Raised += OnEvent;
    }

    void OnDestroy()
    {
        if (m_Flow != null)
            m_Flow.StateMachine.StateChanged -= OnStateChanged;
        GameEvents.Raised -= OnEvent;
    }

    AudioSource CreateLoopSource()
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = 0f;
        return source;
    }

    void OnEvent(GameEvent gameEvent)
    {
        switch (gameEvent.Name)
        {
            case "dread_changed":
                SetLevel(gameEvent.Count, false);
                break;
            case "hammer_cock":
                Silence(m_TriggerSilence);
                break;
            case "player_died":
                Silence(m_DeathSilence);
                break;
        }
    }

    void OnStateChanged(GameState previous, GameState next)
    {
        if (next == GameState.Seating || next == GameState.Lobby)
            SetLevel(0, false);

        m_GunLightOn = next == GameState.GunHandover || next == GameState.ShooterTurn;
        m_TensionOn = next == GameState.ShooterTurn;
    }

    /// <summary>배경음을 잠깐 죽인다. 이미 정적 중이면 더 긴 쪽으로.</summary>
    public void Silence(float duration) => m_SilenceUntil = Mathf.Max(m_SilenceUntil, Time.time + duration);

    /// <summary>단계를 바꾼다. immediately면 바로, 아니면 천천히 넘어간다 (단계가 오를 때는 조명이 잠깐 꺼진다).</summary>
    public void SetLevel(int level, bool immediately)
    {
        if (m_Levels == null || m_Levels.Length == 0)
            return;

        level = Mathf.Clamp(level, 0, m_Levels.Length - 1);
        var changed = level != m_Level;
        if (!immediately && level > m_Level)
            m_BlackoutUntil = Time.time + m_BlackoutTime;
        m_Level = level;

        var target = m_Levels[level];
        if (immediately)
        {
            m_LightColor = target.LightColor;
            m_LightIntensity = target.LightIntensity;
            m_Flicker = target.Flicker;
            m_Ambient = target.Ambient;
            m_HostIntensity = target.HostLightIntensity;
        }

        if (changed || immediately)
            PlayMusic(target.Music, immediately);
        Apply();
    }

    void Update()
    {
        if (m_Levels == null || m_Levels.Length == 0)
            return;

        // 꺼져 있는 동안에는 새 단계로 넘어가지 않고 있다가, 다시 켜질 때 새 단계 쪽으로 넘어간다
        if (Time.time >= m_BlackoutUntil)
        {
            var target = m_Levels[m_Level];
            var t = Time.deltaTime / m_TransitionTime;
            m_LightColor = Color.Lerp(m_LightColor, target.LightColor, t * 3f);
            m_LightIntensity = Mathf.MoveTowards(m_LightIntensity, target.LightIntensity, Mathf.Max(0.5f, Mathf.Abs(target.LightIntensity - m_LightIntensity)) * t * 2f);
            m_Flicker = Mathf.MoveTowards(m_Flicker, target.Flicker, t);
            m_Ambient = Color.Lerp(m_Ambient, target.Ambient, t * 3f);
            m_HostIntensity = Mathf.MoveTowards(m_HostIntensity, target.HostLightIntensity, Mathf.Max(1f, target.HostLightIntensity) * t);
        }

        Apply();
        UpdateGunLight();
        UpdateMusic();
    }

    void Apply()
    {
        var blackout = Time.time < m_BlackoutUntil;
        if (m_MainLight != null)
        {
            m_MainLight.color = m_LightColor;
            m_MainLight.intensity = blackout ? 0f : m_LightIntensity * FlickerFactor();
        }

        RenderSettings.ambientLight = blackout ? m_Ambient * 0.15f : m_Ambient;

        if (m_HostLight != null)
        {
            var intensity = blackout ? 0f : m_HostIntensity;
            m_HostLight.intensity = intensity;
            m_HostLight.enabled = intensity > 0.01f;
        }
    }

    // 1 = 평소 밝기. 대부분은 조금씩 떨리고, 가끔 확 꺼졌다 돌아온다. 세기만 바꿔서 성능 부담이 없다 (03 문서 위험 표)
    float FlickerFactor()
    {
        if (m_Flicker <= 0.001f)
            return 1f;

        var time = Time.time;
        var hum = Mathf.PerlinNoise(m_FlickerSeed, time * 6f);
        var drop = Mathf.PerlinNoise(m_FlickerSeed + 37f, time * 2.5f);
        var dip = drop > 0.72f ? Mathf.Clamp01((drop - 0.72f) * 8f) : 0f;
        return Mathf.Clamp01(1f - m_Flicker * (0.25f * hum + 0.75f * dip));
    }

    // 총 위에서 아래로 비춘다. 총을 들면 천천히 따라간다
    void UpdateGunLight()
    {
        if (m_GunLight == null)
            return;

        var gun = m_Flow != null ? m_Flow.GunTransform : null;
        var on = m_GunLightOn && gun != null && gun.gameObject.activeInHierarchy;
        m_GunLightLevel = Mathf.MoveTowards(m_GunLightLevel, on ? 1f : 0f, Time.deltaTime / m_GunLightFade);

        if (on)
        {
            var target = gun.position;
            m_GunLightTarget = m_GunLightLevel <= 0.01f ? target : Vector3.Lerp(m_GunLightTarget, target, Time.deltaTime * 4f);
            var lightTransform = m_GunLight.transform;
            lightTransform.position = new Vector3(m_GunLightTarget.x, m_GunLightTarget.y + m_GunLightHeight, m_GunLightTarget.z);
            lightTransform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        }

        m_GunLight.intensity = m_GunLightIntensity * m_GunLightLevel * (Time.time < m_BlackoutUntil ? 0f : 1f);
        m_GunLight.enabled = m_GunLight.intensity > 0.01f;
    }

    void PlayMusic(AudioClip clip, bool immediately)
    {
        if (m_MusicSources == null)
            return;

        var current = m_MusicSources[m_MusicIndex];
        if (current.clip == clip && current.isPlaying)
            return;

        // 다른 쪽 소스로 새 곡을 시작하고, 지금 곡은 줄여 나간다
        m_MusicIndex = 1 - m_MusicIndex;
        var next = m_MusicSources[m_MusicIndex];
        next.clip = clip;
        m_MusicLevels[m_MusicIndex] = immediately ? m_MusicVolume : 0f;
        if (clip != null)
            next.Play();
        else
            next.Stop();
        if (immediately)
            m_MusicLevels[1 - m_MusicIndex] = 0f;
    }

    void UpdateMusic()
    {
        if (m_MusicSources == null)
            return;

        // 정적: 바로 죽이고, 끝나면 천천히 돌아온다
        var silent = Time.time < m_SilenceUntil;
        m_Duck = silent ? 0f : Mathf.MoveTowards(m_Duck, 1f, Time.deltaTime / m_SilenceRecover);

        var step = Time.deltaTime / m_MusicFadeTime * m_MusicVolume;
        for (var i = 0; i < m_MusicSources.Length; i++)
        {
            var source = m_MusicSources[i];
            var target = i == m_MusicIndex && source.clip != null ? m_MusicVolume : 0f;
            // 정적은 페이드와 따로 곱해서, 넘어가는 중이어도 바로 조용해진다
            m_MusicLevels[i] = Mathf.MoveTowards(m_MusicLevels[i], target, step);
            source.volume = m_MusicLevels[i] * m_Duck;
            if (m_MusicLevels[i] <= 0f && i != m_MusicIndex && source.isPlaying)
                source.Stop();
        }

        if (m_TensionSource.clip != null)
        {
            var tensionTarget = m_TensionOn ? m_TensionVolume : 0f;
            m_TensionLevel = Mathf.MoveTowards(m_TensionLevel, tensionTarget, Time.deltaTime / 1.5f * m_TensionVolume);
            if (m_TensionLevel > 0f && !m_TensionSource.isPlaying)
                m_TensionSource.Play();
            else if (m_TensionLevel <= 0f && m_TensionSource.isPlaying)
                m_TensionSource.Stop();
            m_TensionSource.volume = m_TensionLevel * m_Duck;
        }
    }
}
