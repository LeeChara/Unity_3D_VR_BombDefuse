using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 이벤트(<see cref="GameEvents"/>)를 받아 <see cref="SoundBank"/>에 연결된 효과음을 재생한다.
/// 소리 위치(의자, 단말기, 총 등)는 진행 컨트롤러에 연결된 오브젝트에서 찾는다.
/// 멀티: 호스트가 낸 이벤트는 네트워크 쪽이 클라이언트에도 전달하므로, 양쪽 기기가 각자 이 컴포넌트로 재생한다.
/// </summary>
public class EventSoundPlayer : MonoBehaviour
{
    [SerializeField] SoundBank m_Bank;
    [SerializeField, Tooltip("소리 위치(의자, 단말기, 총, 퀴즈 화면, 진행자)를 여기서 찾는다")] GameFlowController m_Flow;
    [SerializeField, Min(1), Tooltip("동시에 낼 수 있는 소리 수")] int m_Voices = 12;
    [SerializeField, Tooltip("몸 위치: 의자에서 이만큼 위(m)")] float m_BodyHeight = 1f;

    readonly List<AudioSource> m_Sources = new List<AudioSource>();
    readonly Dictionary<SoundBank.Entry, AudioClip> m_LastClips = new Dictionary<SoundBank.Entry, AudioClip>();
    int m_NextSource;

    public SoundBank Bank => m_Bank;

    void Awake()
    {
        for (var i = 0; i < m_Voices; i++)
        {
            var voice = new GameObject($"Voice {i}");
            voice.transform.SetParent(transform, false);
            var source = voice.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            m_Sources.Add(source);
        }
    }

    void OnEnable() => GameEvents.Raised += OnEvent;

    void OnDisable() => GameEvents.Raised -= OnEvent;

    void OnEvent(GameEvent gameEvent)
    {
        if (m_Bank == null)
            return;

        var entry = m_Bank.Find(gameEvent.Name);
        if (entry == null || !entry.HasClip || !IsAudience(entry.Audience, gameEvent.Player))
            return;

        if (entry.Delay > 0f)
            StartCoroutine(PlayLater(entry, gameEvent));
        else
            Play(entry, gameEvent);
    }

    IEnumerator PlayLater(SoundBank.Entry entry, GameEvent gameEvent)
    {
        yield return new WaitForSeconds(entry.Delay);
        Play(entry, gameEvent);
    }

    bool IsAudience(SoundAudience audience, int player)
    {
        if (audience == SoundAudience.Everyone || player <= 0)
            return true;

        var mine = m_Flow == null || m_Flow.IsLocalSeat == null || m_Flow.IsLocalSeat(player);
        return audience == SoundAudience.EventPlayer ? mine : !mine;
    }

    void Play(SoundBank.Entry entry, GameEvent gameEvent)
    {
        var clip = PickClip(entry);
        if (clip == null)
            return;

        var source = m_Sources[m_NextSource];
        m_NextSource = (m_NextSource + 1) % m_Sources.Count;

        var spatial = entry.Location != SoundLocation.Listener;
        if (spatial)
            source.transform.position = FindPosition(entry.Location, gameEvent);
        source.spatialBlend = spatial ? 1f : 0f;
        source.minDistance = entry.MinDistance;
        source.maxDistance = Mathf.Max(entry.MinDistance + 0.1f, entry.MaxDistance);
        source.pitch = 1f + Random.Range(-entry.PitchJitter, entry.PitchJitter);
        source.clip = clip;
        source.volume = entry.Volume;
        source.Play();
    }

    // 여러 개면 무작위로 고르되, 바로 전 소리는 피한다
    AudioClip PickClip(SoundBank.Entry entry)
    {
        var candidates = new List<AudioClip>();
        foreach (var clip in entry.Clips)
            if (clip != null)
                candidates.Add(clip);
        if (candidates.Count == 0)
            return null;

        if (candidates.Count > 1 && m_LastClips.TryGetValue(entry, out var last))
            candidates.Remove(last);
        var picked = candidates[Random.Range(0, candidates.Count)];
        m_LastClips[entry] = picked;
        return picked;
    }

    Vector3 FindPosition(SoundLocation location, GameEvent gameEvent)
    {
        var fallback = transform.position;
        if (m_Flow == null)
            return fallback;

        switch (location)
        {
            case SoundLocation.Seat:
                return Position(m_Flow.SeatTransform(gameEvent.Player), fallback);
            case SoundLocation.EventPosition when gameEvent.HasPosition:
                return gameEvent.Position;
            case SoundLocation.Body:
            case SoundLocation.EventPosition:
                var seat = m_Flow.SeatTransform(gameEvent.Player);
                return seat != null ? seat.position + Vector3.up * m_BodyHeight : fallback;
            case SoundLocation.Terminal:
                return Position(m_Flow.TerminalTransform(gameEvent.Player), fallback);
            case SoundLocation.QuizScreen:
                return m_Flow.QuizScreenPosition ?? fallback;
            case SoundLocation.Gun:
                return Position(m_Flow.GunTransform, fallback);
            case SoundLocation.Host:
                return Position(m_Flow.HostTransform, fallback);
            default:
                return fallback;
        }
    }

    static Vector3 Position(Transform target, Vector3 fallback) => target != null ? target.position : fallback;
}
