using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 착석 단계를 두 기기에서 맞춘다. 착석 진행(안내 → 경고 → 강제 착석)은 호스트의 <see cref="GameFlowController"/>만 하고, 두 사람이 모두 앉아야 다음으로 넘어간다.
/// - 클라이언트 → 호스트: 자기 플레이어가 자기 의자에 앉았는지 바뀔 때마다 보낸다. 호스트는 <see cref="GameFlowController.SetRemoteSeated"/>로 받는다.
/// - 호스트 → 클라이언트: 강제 착석할 때(<see cref="GameFlowController.SeatForced"/>) 그 좌석 기기에 보내 <see cref="GameFlowController.ForceLocalSeat"/>를 부르게 한다.
/// 의자 뒤에 서기, 고정, 풀기는 클라이언트가 받은 상태(Seating, Intro, GameOver)를 보고 스스로 한다 (<see cref="GameFlowController"/>).
/// </summary>
public class LanSeatingSync : MonoBehaviour
{
    const string k_SeatedMessage = "BombDefuse.Seated";
    const string k_ForceSeatMessage = "BombDefuse.ForceSeat";

    [SerializeField] GameFlowController m_Flow;
    [SerializeField, Tooltip("이 기기의 플레이어 (XR Origin)")] SeatedPlayer m_Player;

    NetworkManager m_NetworkManager;
    bool m_LastSentSeated;
    bool m_HasSent;

    void Start()
    {
        m_NetworkManager = NetworkManager.Singleton;
        if (m_NetworkManager == null)
        {
            Debug.LogWarning("[LanSeatingSync] No NetworkManager in the scene", this);
            return;
        }

        m_NetworkManager.OnClientStarted += RegisterHandlers;
        m_NetworkManager.OnServerStarted += RegisterHandlers;
        m_Flow.SeatForced += OnSeatForced;
    }

    void OnDestroy()
    {
        if (m_NetworkManager != null)
        {
            m_NetworkManager.OnClientStarted -= RegisterHandlers;
            m_NetworkManager.OnServerStarted -= RegisterHandlers;
        }
        if (m_Flow != null)
            m_Flow.SeatForced -= OnSeatForced;
    }

    // 메시지 관리자는 접속할 때마다 새로 만들어지므로 매번 등록한다
    void RegisterHandlers()
    {
        m_HasSent = false;
        var messaging = m_NetworkManager.CustomMessagingManager;
        if (m_NetworkManager.IsServer)
            messaging.RegisterNamedMessageHandler(k_SeatedMessage, ReceiveSeated);
        else
            messaging.RegisterNamedMessageHandler(k_ForceSeatMessage, ReceiveForceSeat);
    }

    // 클라이언트: 자기 의자에 앉았는지가 바뀌면 호스트에 알린다
    void Update()
    {
        if (m_NetworkManager == null || m_NetworkManager.IsServer || !m_NetworkManager.IsConnectedClient || m_Player == null)
            return;

        var seated = m_Player.CurrentSeat != null && m_Player.CurrentSeat == m_Player.AssignedSeat;
        if (m_HasSent && seated == m_LastSentSeated)
            return;

        m_HasSent = true;
        m_LastSentSeated = seated;
        using (var writer = new FastBufferWriter(1, Allocator.Temp))
        {
            writer.WriteValueSafe(seated);
            m_NetworkManager.CustomMessagingManager.SendNamedMessage(k_SeatedMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    // 호스트: 보낸 기기의 좌석이 앉았는지 기록한다
    void ReceiveSeated(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out bool seated);
        var player = FindPlayer(senderClientId);
        if (player == null || player.SeatNumber < 1)
            return;

        Debug.Log($"[LanSeatingSync] Seat {player.SeatNumber} {(seated ? "seated" : "stood up")}", this);
        m_Flow.SetRemoteSeated(player.SeatNumber, seated);
    }

    // 호스트: 강제 착석할 좌석의 기기에 알린다
    void OnSeatForced(int seat)
    {
        var player = NetworkPlayer.AtSeat(seat);
        if (m_NetworkManager == null || !m_NetworkManager.IsServer || player == null || player.OwnerClientId == NetworkManager.ServerClientId)
            return;

        using (var writer = new FastBufferWriter(0, Allocator.Temp))
            m_NetworkManager.CustomMessagingManager.SendNamedMessage(k_ForceSeatMessage, player.OwnerClientId, writer, NetworkDelivery.ReliableSequenced);
    }

    // 클라이언트: 호스트가 강제로 앉혔다
    void ReceiveForceSeat(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
            return;

        Debug.Log("[LanSeatingSync] Forced to sit by the host", this);
        m_Flow.ForceLocalSeat();
    }

    static NetworkPlayer FindPlayer(ulong clientId)
    {
        foreach (var player in NetworkPlayer.Players)
            if (player.OwnerClientId == clientId)
                return player;
        return null;
    }
}
