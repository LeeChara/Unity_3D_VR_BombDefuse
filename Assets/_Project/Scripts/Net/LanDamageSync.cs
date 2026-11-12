using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 피해와 사망을 클라이언트에도 맞춘다. 맞았는지는 호스트의 <see cref="Gun"/>만 판정한다.
/// - 호스트: 좌석의 몸이 맞으면(<see cref="GameFlowController.SeatHit"/>) 좌석과 부위를 보내고, 새 게임에서 몸을 되돌리면(<see cref="GameFlowController.BodiesRevived"/>) 알린다.
/// - 클라이언트: 받은 좌석의 몸(자기 좌석이면 XR Origin의 몸, 상대면 아바타의 몸, <see cref="NetworkPlayer.BodyAtSeat"/>)에 똑같이 적용한다.
///   그래서 클라이언트에서도 <see cref="Damageable"/>의 중상·사망 상태와 이벤트가 호스트와 같다.
/// </summary>
public class LanDamageSync : MonoBehaviour
{
    const string k_HitMessage = "BombDefuse.SeatHit";
    const string k_ReviveMessage = "BombDefuse.BodiesRevived";

    [SerializeField] GameFlowController m_Flow;

    NetworkManager m_NetworkManager;

    void Start()
    {
        m_NetworkManager = NetworkManager.Singleton;
        if (m_NetworkManager == null)
        {
            Debug.LogWarning("[LanDamageSync] No NetworkManager in the scene", this);
            return;
        }

        m_NetworkManager.OnClientStarted += RegisterHandlers;
        m_Flow.SeatHit += OnSeatHit;
        m_Flow.BodiesRevived += OnBodiesRevived;
    }

    void OnDestroy()
    {
        if (m_NetworkManager != null)
            m_NetworkManager.OnClientStarted -= RegisterHandlers;
        if (m_Flow != null)
        {
            m_Flow.SeatHit -= OnSeatHit;
            m_Flow.BodiesRevived -= OnBodiesRevived;
        }
    }

    // 메시지 관리자는 접속할 때마다 새로 만들어지므로 매번 등록한다. 받는 쪽은 클라이언트뿐이다
    void RegisterHandlers()
    {
        if (m_NetworkManager.IsServer)
            return;

        var messaging = m_NetworkManager.CustomMessagingManager;
        messaging.RegisterNamedMessageHandler(k_HitMessage, ReceiveHit);
        messaging.RegisterNamedMessageHandler(k_ReviveMessage, ReceiveRevive);
    }

    // ── 호스트: 보내기 ──

    void OnSeatHit(int seat, BodyPart part)
    {
        if (!CanSend())
            return;

        using (var writer = new FastBufferWriter(8, Allocator.Temp))
        {
            writer.WriteValueSafe(seat);
            writer.WriteValueSafe((byte)part);
            Send(k_HitMessage, writer);
        }
    }

    void OnBodiesRevived()
    {
        if (!CanSend())
            return;

        using (var writer = new FastBufferWriter(0, Allocator.Temp))
            Send(k_ReviveMessage, writer);
    }

    bool CanSend() => m_NetworkManager != null && m_NetworkManager.IsServer && m_NetworkManager.ConnectedClientsIds.Count > 1;

    void Send(string messageName, FastBufferWriter writer)
    {
        var clients = new List<ulong>(m_NetworkManager.ConnectedClientsIds);
        clients.Remove(NetworkManager.ServerClientId);
        m_NetworkManager.CustomMessagingManager.SendNamedMessage(messageName, clients, writer, NetworkDelivery.ReliableSequenced);
    }

    // ── 클라이언트: 받기 ──

    void ReceiveHit(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
            return;

        reader.ReadValueSafe(out int seat);
        reader.ReadValueSafe(out byte part);
        var body = NetworkPlayer.BodyAtSeat(seat);
        if (body == null)
        {
            Debug.LogWarning($"[LanDamageSync] No body for seat {seat}", this);
            return;
        }

        body.ApplyHit((BodyPart)part);
    }

    void ReceiveRevive(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
            return;

        foreach (var player in NetworkPlayer.Players)
        {
            var body = NetworkPlayer.BodyAtSeat(player.SeatNumber);
            if (body != null && (body.IsDead || body.IsWounded))
                body.Revive();
        }
    }
}
