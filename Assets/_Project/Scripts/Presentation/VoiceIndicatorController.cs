using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상대가 말하는 동안 상대 아바타 머리 위에 스피커 표시(<see cref="VoiceIndicator"/>)를 띄운다. 내 것은 띄우지 않는다.
/// 말하는지는 Vivox의 음성 감지(<see cref="LanVoiceChat.IsSeatSpeaking"/>), 소리 줄 개수는 목소리 크기(<see cref="LanVoiceChat.SeatAudioEnergy"/>)로 정한다.
/// </summary>
public class VoiceIndicatorController : MonoBehaviour
{
    [SerializeField, Tooltip("상대 표시의 머리 기준 위치")] Vector3 m_HeadOffset = new Vector3(0f, 0.24f, 0f);
    [SerializeField, Tooltip("상대 표시 높이(m)")] float m_HeadSize = 0.12f;
    [SerializeField] Color m_Color = Color.white;

    readonly Dictionary<NetworkPlayer, VoiceIndicator> m_Remote = new Dictionary<NetworkPlayer, VoiceIndicator>();
    readonly List<NetworkPlayer> m_Gone = new List<NetworkPlayer>();

    void LateUpdate()
    {
        var voice = LanVoiceChat.Instance;
        if (voice == null)
            return;

        // 지금 있는 상대 아바타마다 표시 하나
        foreach (var player in NetworkPlayer.Players)
        {
            if (player == null || player == NetworkPlayer.Local || player.Head == null)
                continue;

            if (!m_Remote.TryGetValue(player, out var indicator) || indicator == null)
            {
                indicator = VoiceIndicator.Create("VoiceIndicator", player.Head, m_HeadOffset, m_HeadSize, m_Color);
                m_Remote[player] = indicator;
            }

            var seat = player.SeatNumber;
            indicator.SetVoice(voice.IsSeatSpeaking(seat), voice.SeatAudioEnergy(seat));
        }

        // 사라진 아바타의 표시는 아바타와 함께 없어졌으므로 목록에서만 뺀다
        m_Gone.Clear();
        foreach (var pair in m_Remote)
            if (pair.Key == null || pair.Value == null)
                m_Gone.Add(pair.Key);
        foreach (var player in m_Gone)
            m_Remote.Remove(player);
    }
}
