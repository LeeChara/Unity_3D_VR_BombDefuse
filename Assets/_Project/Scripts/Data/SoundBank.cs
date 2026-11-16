using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>효과음이 들리는 곳.</summary>
public enum SoundLocation
{
    [Tooltip("듣는 사람 귀에서 바로 (공간감 없음)")] Listener,
    [Tooltip("이벤트 주인공 좌석의 의자")] Seat,
    [Tooltip("이벤트 주인공 좌석의 몸 (의자 위 가슴 높이)")] Body,
    [Tooltip("이벤트 주인공 좌석의 단말기")] Terminal,
    [Tooltip("퀴즈 화면")] QuizScreen,
    [Tooltip("총")] Gun,
    [Tooltip("진행자")] Host,
    [Tooltip("이벤트가 알려 준 지점 (맞은 곳 등). 없으면 몸")] EventPosition,
}

/// <summary>효과음을 누가 듣는지.</summary>
public enum SoundAudience
{
    [Tooltip("모든 기기")] Everyone,
    [Tooltip("이벤트 주인공 좌석의 기기에서만 (심장박동 등)")] EventPlayer,
    [Tooltip("이벤트 주인공이 아닌 기기에서만")] OtherPlayers,
}

/// <summary>
/// 게임 이벤트(docs/07_event_list.md)마다 재생할 효과음. 씬의 <see cref="EventSoundPlayer"/>가 이벤트를 받아 이 표에서 소리를 찾는다.
/// 메뉴 BombDefuse > 사운드 연결 창에서 폴더의 파일을 이름으로 자동 연결할 수 있다.
/// </summary>
[CreateAssetMenu(fileName = "SoundBank", menuName = "BombDefuse/Sound Bank")]
public class SoundBank : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        [Tooltip("이벤트 이름 (docs/07_event_list.md)")] public string EventName;
        [Tooltip("여러 개면 매번 무작위로 고른다 (같은 소리가 연속으로 나오지 않게)")] public List<AudioClip> Clips = new List<AudioClip>();
        public SoundLocation Location;
        public SoundAudience Audience;
        [Range(0f, 1f)] public float Volume = 1f;
        [Range(0f, 0.3f), Tooltip("재생할 때마다 음높이를 이만큼 무작위로 바꾼다")] public float PitchJitter = 0.05f;
        [Min(0f), Tooltip("이벤트가 나고 이 시간(초) 뒤에 재생한다")] public float Delay;
        [Tooltip("공간 음향일 때 이 거리(m)까지는 크게 들린다")] public float MinDistance = 0.5f;
        [Tooltip("공간 음향일 때 이 거리(m)부터는 들리지 않는다")] public float MaxDistance = 15f;

        public bool HasClip
        {
            get
            {
                foreach (var clip in Clips)
                    if (clip != null)
                        return true;
                return false;
            }
        }
    }

    [SerializeField] List<Entry> m_Entries = new List<Entry>();

    public List<Entry> Entries => m_Entries;

    /// <summary>이 이벤트의 효과음 칸. 없으면 null.</summary>
    public Entry Find(string eventName)
    {
        foreach (var entry in m_Entries)
            if (entry.EventName == eventName)
                return entry;
        return null;
    }
}
