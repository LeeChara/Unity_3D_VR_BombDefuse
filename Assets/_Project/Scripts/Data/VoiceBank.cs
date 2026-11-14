using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 진행자 대사의 녹음. 대사 id(host_lines.csv의 id칸)마다 음성 파일 하나.
/// {bullets}가 들어간 대사는 숫자마다 따로 녹음해 `id_숫자`로 넣는다 (예: gh01_3). 숫자별 녹음이 없으면 `id` 녹음을 쓴다.
/// 메뉴 BombDefuse > 사운드 연결 창에서 폴더의 파일을 이름으로 자동 연결할 수 있다.
/// </summary>
[CreateAssetMenu(fileName = "VoiceBank", menuName = "BombDefuse/Voice Bank")]
public class VoiceBank : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        [Tooltip("대사 id. 숫자별 녹음이면 id_숫자 (예: gh01_3)")] public string Key;
        public AudioClip Clip;
    }

    [SerializeField] List<Entry> m_Entries = new List<Entry>();

    public List<Entry> Entries => m_Entries;

    /// <summary>이 대사의 녹음. 총알 개수가 있으면 숫자별 녹음을 먼저 찾는다. 없으면 null.</summary>
    public AudioClip Find(string lineId, int bullets = -1)
    {
        if (string.IsNullOrEmpty(lineId))
            return null;
        if (bullets >= 0)
        {
            var numbered = FindKey($"{lineId}_{bullets}");
            if (numbered != null)
                return numbered;
        }

        return FindKey(lineId);
    }

    AudioClip FindKey(string key)
    {
        foreach (var entry in m_Entries)
            if (entry.Key == key && entry.Clip != null)
                return entry.Clip;
        return null;
    }
}
