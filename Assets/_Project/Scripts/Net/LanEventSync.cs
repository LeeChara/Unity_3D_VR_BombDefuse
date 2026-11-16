using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 게임 이벤트(<see cref="GameEvents"/>)를 상대 기기에도 낸다. 효과음처럼 이벤트에 반응하는 쪽이 양쪽 기기에서 똑같이 반응하게 한다.
/// 호스트에서 난 이벤트는 모든 클라이언트에 보낸다. 클라이언트에서 난 이벤트는 총을 직접 만지는 것(<see cref="GameEventCatalog.Info.FromShooter"/>)만 호스트에 보내고,
/// 호스트는 받은 이벤트를 자기 기기에서 내고 다른 클라이언트에도 전달한다. <see cref="GameEvent.LocalOnly"/> 이벤트는 보내지 않는다.
/// </summary>
public class LanEventSync : MonoBehaviour
{
    const string k_EventMessage = "BombDefuse.GameEvent";

    NetworkManager m_NetworkManager;

    void Start()
    {
        m_NetworkManager = NetworkManager.Singleton;
        if (m_NetworkManager == null)
        {
            Debug.LogWarning("[LanEventSync] No NetworkManager in the scene", this);
            return;
        }

        m_NetworkManager.OnClientStarted += RegisterHandlers;
        GameEvents.Raised += OnRaised;
    }

    void OnDestroy()
    {
        if (m_NetworkManager != null)
            m_NetworkManager.OnClientStarted -= RegisterHandlers;
        GameEvents.Raised -= OnRaised;
    }

    // 메시지 관리자는 접속할 때마다 새로 만들어지므로 매번 등록한다
    void RegisterHandlers() =>
        m_NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(k_EventMessage, Receive);

    bool IsConnected => m_NetworkManager != null && m_NetworkManager.IsListening && m_NetworkManager.CustomMessagingManager != null;

    void OnRaised(GameEvent gameEvent)
    {
        if (gameEvent.LocalOnly || gameEvent.FromNetwork || !IsConnected)
            return;

        if (m_NetworkManager.IsServer)
        {
            SendToClients(gameEvent, NetworkManager.ServerClientId);
            return;
        }

        var info = GameEventCatalog.Find(gameEvent.Name);
        if (info != null && info.FromShooter)
            Send(gameEvent, new List<ulong> { NetworkManager.ServerClientId });
    }

    void Receive(ulong senderClientId, FastBufferReader reader)
    {
        var gameEvent = Read(reader);
        gameEvent.FromNetwork = true;

        if (m_NetworkManager.IsServer)
        {
            // 클라이언트는 총을 직접 만지는 이벤트만 보낼 수 있다
            var info = GameEventCatalog.Find(gameEvent.Name);
            if (info == null || !info.FromShooter)
            {
                Debug.LogWarning($"[LanEventSync] 클라이언트 {senderClientId}가 보낸 '{gameEvent.Name}'는 받지 않는다", this);
                return;
            }

            SendToClients(gameEvent, senderClientId);
        }
        else if (senderClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        GameEvents.Raise(gameEvent);
    }

    // 호스트: 보낸 사람(또는 호스트 자신)을 뺀 클라이언트에 보낸다
    void SendToClients(GameEvent gameEvent, ulong except)
    {
        var clients = new List<ulong>(m_NetworkManager.ConnectedClientsIds);
        clients.Remove(NetworkManager.ServerClientId);
        clients.Remove(except);
        if (clients.Count > 0)
            Send(gameEvent, clients);
    }

    void Send(GameEvent gameEvent, List<ulong> targets)
    {
        using (var writer = new FastBufferWriter(64, Allocator.Temp, 1024))
        {
            writer.WriteValueSafe(gameEvent.Name);
            writer.WriteValueSafe((byte)gameEvent.Player);
            writer.WriteValueSafe((byte)gameEvent.Target);
            writer.WriteValueSafe(gameEvent.Count);
            writer.WriteValueSafe(gameEvent.HasPart);
            writer.WriteValueSafe((byte)gameEvent.Part);
            writer.WriteValueSafe(gameEvent.HasPosition);
            writer.WriteValueSafe(gameEvent.Position);
            m_NetworkManager.CustomMessagingManager.SendNamedMessage(k_EventMessage, targets, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    static GameEvent Read(FastBufferReader reader)
    {
        reader.ReadValueSafe(out string name);
        reader.ReadValueSafe(out byte player);
        reader.ReadValueSafe(out byte target);
        reader.ReadValueSafe(out int count);
        reader.ReadValueSafe(out bool hasPart);
        reader.ReadValueSafe(out byte part);
        reader.ReadValueSafe(out bool hasPosition);
        reader.ReadValueSafe(out Vector3 position);

        return new GameEvent(name, player, target, count)
        {
            HasPart = hasPart,
            Part = (BodyPart)part,
            HasPosition = hasPosition,
            Position = position,
        };
    }
}
