using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 방 분위기 연출. 공포 단계(docs/01_rules_essential.md 7절) 하나로 조명·환경광·진행자 조명·배경음·벽 위치를 함께 바꾸고,
/// 진행 상태에 따라 대상 조명·사격 배경음·심장박동·정적을 더한다.
/// - 공포 단계: <c>dread_changed</c>(새 단계)를 받으면 쿵 하고 방이 흔들리며 조명이 꺼진다. 어둠이 이어지다가 새 단계 조명(흰색 또는 붉은색)이 천천히 켜지고,
///   그 뒤로 벽이 정해진 시간 동안 천천히 다가온다. 진행 쪽은 이 연출이 끝날 때까지 기다린다 (<see cref="GameFlowController"/>의 Penalty Duration).
///   새 게임(Seating)과 로비에서 0으로 돌아간다 (벽은 바로 제자리).
/// - 대상 조명: 총 지급·사수 턴 동안 사수의 상대 머리 위에서 비춘다 (<c>gun_handover</c>의 Target).
/// - 사격 배경음: 총 지급·사수 턴 동안 단계 배경음 대신 나오고, 시간이 갈수록 빨라진다. 심장박동도 점점 커지고 빨라진다 (표적 기기에서 크게, 사수 기기에서 작게). 끝나면 단계 배경음이 돌아온다.
/// - 정적: 방아쇠를 당긴 순간(<c>hammer_cock</c>)과 사망(<c>player_died</c>) 때 배경음을 잠깐 죽인다.
/// 이벤트와 상태는 네트워크로 전달되므로 클라이언트에서도 같이 바뀐다. 제한시간·난이도는 진행 쪽(<see cref="GameFlowController"/>)이 바꾼다.
/// </summary>
public class DreadAtmosphere : MonoBehaviour
{
    [Serializable]
    public class Level
    {
        [Tooltip("천장 조명 색 (흰색 또는 붉은색)")] public Color LightColor = Color.white;
        [Tooltip("천장 조명 밝기")] public float LightIntensity = 3f;
        [Range(0f, 1f), Tooltip("깜빡임 세기. 0이면 안 깜빡인다")] public float Flicker;
        [Tooltip("환경광 색 (그림자 속 밝기)")] public Color Ambient = new Color(0.12f, 0.12f, 0.12f);
        [Tooltip("진행자만 비추는 조명 밝기")] public float HostLightIntensity;
        [Tooltip("방 가운데에서 벽까지 거리(m). 단계가 오를수록 줄여 방을 좁힌다")] public float WallDistance = 3.95f;
        [Tooltip("이 단계의 배경음 (반복). 비우면 조용하다")] public AudioClip Music;
    }

    [SerializeField] GameFlowController m_Flow;
    [SerializeField, Tooltip("방 천장 조명")] Light m_MainLight;
    [SerializeField, Tooltip("진행자만 비추는 조명 (3단계에서 켠다)")] Light m_HostLight;
    [SerializeField, Tooltip("단계 0~3")] Level[] m_Levels =
    {
        new Level { LightColor = new Color(1f, 0.97f, 0.92f), LightIntensity = 1.8f, Flicker = 0f, Ambient = new Color(0.12f, 0.12f, 0.12f), WallDistance = 3.95f },
        new Level { LightColor = new Color(1f, 0.97f, 0.92f), LightIntensity = 1.1f, Flicker = 0.45f, Ambient = new Color(0.07f, 0.07f, 0.07f), WallDistance = 3.3f },
        new Level { LightColor = new Color(0.85f, 0.08f, 0.06f), LightIntensity = 2f, Flicker = 0.3f, Ambient = new Color(0.08f, 0.02f, 0.02f), WallDistance = 2.8f },
        new Level { LightColor = new Color(0.7f, 0.02f, 0.02f), LightIntensity = 0.7f, Flicker = 0.4f, Ambient = new Color(0.035f, 0.005f, 0.005f), HostLightIntensity = 6f, WallDistance = 2.35f },
    };
    [SerializeField, Min(0.01f), Tooltip("단계가 바뀔 때 조명이 넘어가는 시간(초)")] float m_TransitionTime = 2f;
    [SerializeField, Min(0f), Tooltip("단계가 오를 때 조명이 완전히 꺼져 있는 시간(초)")] float m_BlackoutTime = 2f;

    [Header("흔들림·벽")]
    [SerializeField, Tooltip("단계가 오를 때 흔들 오브젝트 (방). 벽은 이 아래에 있어야 같이 흔들린다")] Transform m_ShakeRoot;
    [SerializeField, Min(0f), Tooltip("흔들림 세기(m)")] float m_ShakeAmplitude = 0.02f;
    [SerializeField, Min(0f), Tooltip("흔들림 시간(초)")] float m_ShakeDuration = 1.5f;
    [SerializeField, Tooltip("단계가 오르면 방 가운데로 다가오는 벽들 (방 오브젝트의 자식)")] Transform[] m_Walls;
    [SerializeField, Min(0.01f), Tooltip("조명이 다시 켜진 뒤 벽이 새 거리까지 다가오는 시간(초)")] float m_WallMoveTime = 4f;

    [Header("대상 조명")]
    [SerializeField, FormerlySerializedAs("m_GunLight"), Tooltip("총 지급·사수 턴 동안 사수의 상대 머리 위에서 비추는 조명")] Light m_TargetLight;
    [SerializeField, FormerlySerializedAs("m_GunLightIntensity")] float m_TargetLightIntensity = 6f;
    [SerializeField, FormerlySerializedAs("m_GunLightHeight"), Tooltip("머리 위로 조명 높이(m)")] float m_TargetLightHeight = 1.4f;
    [SerializeField, FormerlySerializedAs("m_GunLightFade"), Min(0.01f), Tooltip("조명이 켜지고 꺼지는 시간(초)")] float m_TargetLightFade = 0.4f;

    [Header("Music")]
    [SerializeField, Range(0f, 1f)] float m_MusicVolume = 0.6f;
    [SerializeField, Min(0.01f), Tooltip("배경음이 넘어가는 시간(초)")] float m_MusicFadeTime = 3f;
    [SerializeField, FormerlySerializedAs("m_TensionLayer"), Tooltip("총 지급·사수 턴 동안 단계 배경음 대신 나오는 곡 (반복). 비우면 단계 배경음이 그대로")] AudioClip m_ShooterMusic;
    [SerializeField, FormerlySerializedAs("m_TensionVolume"), Range(0f, 1f)] float m_ShooterMusicVolume = 0.6f;
    [SerializeField, Tooltip("사수 턴이 끝날 무렵 사격 배경음 속도 (1 = 그대로)")] float m_ShooterMaxPitch = 1.3f;
    [SerializeField, Tooltip("사수 턴 동안 점점 커지는 심장박동 (반복). 비우면 없음")] AudioClip m_Heartbeat;
    [SerializeField, Range(0f, 1f), Tooltip("심장박동 처음 음량")] float m_HeartbeatStartVolume = 0.15f;
    [SerializeField, Range(0f, 1f), Tooltip("심장박동 마지막 음량")] float m_HeartbeatMaxVolume = 0.9f;
    [SerializeField, Tooltip("사수 턴이 끝날 무렵 심장박동 속도")] float m_HeartbeatMaxPitch = 1.6f;
    [SerializeField, Range(0f, 1f), Tooltip("표적이 아닌 기기(사수)의 심장박동 음량 배율. 표적 기기는 그대로 크게 듣는다")] float m_ShooterHeartbeatScale = 0.4f;
    [SerializeField, Min(0.01f), Tooltip("사격 배경음이 들어오고 나가는 시간(초)")] float m_ShooterFade = 0.8f;
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

    Vector3 m_ShakeBase;
    float m_ShakeUntil;
    Vector3[] m_WallBase;
    // 벽에 Rigidbody(Is Kinematic)가 있으면 물리로 옮겨 소품(RoomProps)을 밀어낸다. 위치만 바꾸면 소품이 벽을 뚫거나 튕겨 나간다
    Rigidbody[] m_WallBodies;
    float m_WallDistance;
    float m_WallFrom;
    float m_WallMoveStart;

    float m_TargetLightLevel;
    bool m_ShooterPhase;
    float m_ShooterPhaseStart;
    int m_TargetSeat;
    Transform m_TargetHead;
    Vector3 m_TargetLightPosition;

    AudioSource[] m_MusicSources;
    // 정적을 곱하기 전 음량 (페이드 진행 상태)
    readonly float[] m_MusicLevels = new float[2];
    int m_MusicIndex;
    AudioSource m_ShooterSource;
    AudioSource m_HeartbeatSource;
    float m_ShooterLevel;
    float m_SilenceUntil;
    float m_Duck = 1f;

    public int CurrentLevel => m_Level;

    void Start()
    {
        // 배경음 둘을 번갈아 써서 넘어갈 때 겹쳐 지나가게 한다
        m_MusicSources = new[] { CreateLoopSource(), CreateLoopSource() };
        m_ShooterSource = CreateLoopSource();
        m_ShooterSource.clip = m_ShooterMusic;
        m_HeartbeatSource = CreateLoopSource();
        m_HeartbeatSource.clip = m_Heartbeat;

        if (m_TargetLight != null)
        {
            m_TargetLight.enabled = false;
            m_TargetLight.intensity = 0f;
        }

        if (m_ShakeRoot != null)
            m_ShakeBase = m_ShakeRoot.localPosition;
        if (m_Walls != null)
        {
            m_WallBase = new Vector3[m_Walls.Length];
            m_WallBodies = new Rigidbody[m_Walls.Length];
            for (var i = 0; i < m_Walls.Length; i++)
                if (m_Walls[i] != null)
                {
                    m_WallBase[i] = m_Walls[i].localPosition;
                    m_WallBodies[i] = m_Walls[i].GetComponent<Rigidbody>();
                }
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
            case "gun_handover":
                m_TargetSeat = gameEvent.Target;
                m_TargetHead = m_Flow != null ? m_Flow.HeadOf(m_TargetSeat) : null;
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
            SetLevel(0, true);

        var shooterPhase = next == GameState.GunHandover || next == GameState.ShooterTurn;
        if (shooterPhase && !m_ShooterPhase)
            m_ShooterPhaseStart = Time.time;
        m_ShooterPhase = shooterPhase;
    }

    /// <summary>배경음을 잠깐 죽인다. 이미 정적 중이면 더 긴 쪽으로.</summary>
    public void Silence(float duration) => m_SilenceUntil = Mathf.Max(m_SilenceUntil, Time.time + duration);

    /// <summary>단계를 바꾼다. immediately면 바로 (벽도 제자리로), 아니면 천천히 넘어간다 (단계가 오를 때는 방이 흔들리고 조명이 잠깐 꺼진다).</summary>
    public void SetLevel(int level, bool immediately)
    {
        if (m_Levels == null || m_Levels.Length == 0)
            return;

        level = Mathf.Clamp(level, 0, m_Levels.Length - 1);
        var changed = level != m_Level;
        if (!immediately && level > m_Level)
        {
            m_BlackoutUntil = Time.time + m_BlackoutTime;
            m_ShakeUntil = Time.time + m_ShakeDuration;
            // 벽은 조명이 다시 켜질 때부터 움직인다
            m_WallFrom = m_WallDistance;
            m_WallMoveStart = m_BlackoutUntil;
        }
        m_Level = level;

        var target = m_Levels[level];
        if (immediately)
        {
            m_LightColor = target.LightColor;
            m_LightIntensity = target.LightIntensity;
            m_Flicker = target.Flicker;
            m_Ambient = target.Ambient;
            m_HostIntensity = target.HostLightIntensity;
            m_WallDistance = target.WallDistance;
            m_WallFrom = target.WallDistance;
            m_BlackoutUntil = 0f;
            m_ShakeUntil = 0f;
        }

        if (changed || immediately)
            PlayMusic(target.Music, immediately);
        Apply();
        ApplyWalls(immediately);
    }

    void Update()
    {
        if (m_Levels == null || m_Levels.Length == 0)
            return;

        // 꺼져 있는 동안에는 새 단계로 넘어가지 않고 있다가, 다시 켜질 때 새 단계 쪽으로 넘어간다
        var target = m_Levels[m_Level];
        if (Time.time >= m_BlackoutUntil)
        {
            var t = Time.deltaTime / m_TransitionTime;
            m_LightColor = Color.Lerp(m_LightColor, target.LightColor, t * 3f);
            m_LightIntensity = Mathf.MoveTowards(m_LightIntensity, target.LightIntensity, Mathf.Max(0.5f, Mathf.Abs(target.LightIntensity - m_LightIntensity)) * t * 2f);
            m_Flicker = Mathf.MoveTowards(m_Flicker, target.Flicker, t);
            m_Ambient = Color.Lerp(m_Ambient, target.Ambient, t * 3f);
            m_HostIntensity = Mathf.MoveTowards(m_HostIntensity, target.HostLightIntensity, Mathf.Max(1f, target.HostLightIntensity) * t);
        }

        // 벽은 조명이 다시 켜지면 정해진 시간 동안 천천히 출발해 천천히 멈춘다
        var wallProgress = Mathf.Clamp01((Time.time - m_WallMoveStart) / m_WallMoveTime);
        m_WallDistance = Mathf.Lerp(m_WallFrom, target.WallDistance, Mathf.SmoothStep(0f, 1f, wallProgress));

        Apply();
        ApplyWalls(false);
        UpdateShake();
        UpdateTargetLight();
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

    // 벽을 방 가운데(방 오브젝트의 원점)에서 지금 거리만큼 떨어진 곳에 둔다. 높이와 벽 방향은 그대로.
    // snap이면 물리 이동 없이 바로 옮긴다 (새 게임에서 제자리로)
    void ApplyWalls(bool snap)
    {
        if (m_Walls == null || m_WallBase == null)
            return;

        for (var i = 0; i < m_Walls.Length; i++)
        {
            var wall = m_Walls[i];
            if (wall == null)
                continue;
            var start = m_WallBase[i];
            var flat = new Vector3(start.x, 0f, start.z);
            if (flat.sqrMagnitude < 0.0001f)
                continue;
            var position = flat.normalized * m_WallDistance;
            var local = new Vector3(position.x, start.y, position.z);
            var body = m_WallBodies != null ? m_WallBodies[i] : null;
            if (body == null || snap || !Application.isPlaying)
            {
                wall.localPosition = local;
                if (body != null)
                    body.position = wall.position;
            }
            else
            {
                var world = wall.parent != null ? wall.parent.TransformPoint(local) : local;
                body.MovePosition(world);
            }
        }
    }

    // 쿵 하고 짧게 흔들린다. 처음이 가장 세고 점점 잦아든다
    void UpdateShake()
    {
        if (m_ShakeRoot == null)
            return;

        var left = m_ShakeUntil - Time.time;
        if (left <= 0f)
        {
            m_ShakeRoot.localPosition = m_ShakeBase;
            return;
        }

        var strength = m_ShakeAmplitude * (left / m_ShakeDuration);
        var time = Time.time * 30f;
        var offset = new Vector3(Mathf.PerlinNoise(time, 1.3f) - 0.5f, (Mathf.PerlinNoise(time, 7.1f) - 0.5f) * 0.5f, Mathf.PerlinNoise(time, 4.7f) - 0.5f) * 2f;
        m_ShakeRoot.localPosition = m_ShakeBase + offset * strength;
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

    // 사수의 상대 머리 위에서 아래로 비춘다. 머리를 움직이면 따라간다
    void UpdateTargetLight()
    {
        if (m_TargetLight == null)
            return;

        if (m_TargetHead == null && m_ShooterPhase && m_Flow != null && m_TargetSeat > 0)
            m_TargetHead = m_Flow.HeadOf(m_TargetSeat);
        var on = m_ShooterPhase && m_TargetHead != null;
        m_TargetLightLevel = Mathf.MoveTowards(m_TargetLightLevel, on ? 1f : 0f, Time.deltaTime / m_TargetLightFade);

        if (on)
        {
            var head = m_TargetHead.position;
            m_TargetLightPosition = m_TargetLightLevel <= 0.01f ? head : Vector3.Lerp(m_TargetLightPosition, head, Time.deltaTime * 6f);
            var lightTransform = m_TargetLight.transform;
            lightTransform.position = m_TargetLightPosition + Vector3.up * m_TargetLightHeight;
            lightTransform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        }

        m_TargetLight.intensity = m_TargetLightIntensity * m_TargetLightLevel * (Time.time < m_BlackoutUntil ? 0f : 1f);
        m_TargetLight.enabled = m_TargetLight.intensity > 0.01f;
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

        // 사격 배경음이 있으면 사수 턴 동안 단계 배경음은 물러난다
        var shooterOn = m_ShooterPhase && m_ShooterSource.clip != null;
        m_ShooterLevel = Mathf.MoveTowards(m_ShooterLevel, m_ShooterPhase ? 1f : 0f, Time.deltaTime / m_ShooterFade);
        var baseVolume = m_ShooterSource.clip != null ? 1f - m_ShooterLevel : 1f;

        var step = Time.deltaTime / m_MusicFadeTime * m_MusicVolume;
        for (var i = 0; i < m_MusicSources.Length; i++)
        {
            var source = m_MusicSources[i];
            var target = i == m_MusicIndex && source.clip != null ? m_MusicVolume : 0f;
            // 정적은 페이드와 따로 곱해서, 넘어가는 중이어도 바로 조용해진다
            m_MusicLevels[i] = Mathf.MoveTowards(m_MusicLevels[i], target, step);
            source.volume = m_MusicLevels[i] * m_Duck * baseVolume;
            if (m_MusicLevels[i] <= 0f && i != m_MusicIndex && source.isPlaying)
                source.Stop();
        }

        // 사수 턴이 진행될수록 (0 → 1) 사격 배경음은 빨라지고 심장박동은 커지고 빨라진다
        var limit = m_Flow != null ? m_Flow.ShooterTurnTimeLimit : 20f;
        var progress = Mathf.Clamp01((Time.time - m_ShooterPhaseStart) / Mathf.Max(1f, limit));
        UpdateLoop(m_ShooterSource, m_ShooterLevel * m_ShooterMusicVolume * m_Duck, Mathf.Lerp(1f, m_ShooterMaxPitch, progress), shooterOn);
        var heartbeatVolume = Mathf.Lerp(m_HeartbeatStartVolume, m_HeartbeatMaxVolume, progress);
        // 심장박동은 총을 맞을 사람(표적)에게 크게, 사수에게는 작게 들린다
        var isTarget = m_Flow != null && m_TargetSeat > 0 && m_Flow.LocalSeat == m_TargetSeat;
        if (!isTarget)
            heartbeatVolume *= m_ShooterHeartbeatScale;
        UpdateLoop(m_HeartbeatSource, m_ShooterLevel * heartbeatVolume, Mathf.Lerp(1f, m_HeartbeatMaxPitch, progress), m_ShooterPhase);
    }

    static void UpdateLoop(AudioSource source, float volume, float pitch, bool wanted)
    {
        if (source.clip == null)
            return;

        if (volume > 0.001f && !source.isPlaying && wanted)
            source.Play();
        else if (volume <= 0.001f && source.isPlaying)
            source.Stop();
        source.volume = volume;
        // 빨라지는 것은 사수 턴 동안만. 물러나는 동안에는 그 속도를 유지한다
        if (wanted)
            source.pitch = pitch;
    }
}
