using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 총을 두 기기에서 맞춘다. 약실과 판정은 호스트의 <see cref="Gun"/>만 믿는다.
/// - 꺼내기·치우기: 호스트의 진행 컨트롤러가 사수 앞에 꺼내거나 치우면 클라이언트도 같은 좌석 앞에 꺼내거나 치운다.
/// - 잡기: 사수 기기의 총만 잡을 수 있다(진행 컨트롤러가 <see cref="GameFlowController.IsLocalSeat"/>로 정한다).
/// - 위치: 사수 기기가 총 위치를 보내고, 다른 기기의 총은 그 위치를 따라간다.
/// - 발사: 클라이언트 사수가 쏘면 레이(총구 위치·방향)만 호스트에 보내고, 호스트가 약실을 당겨 판정한다.
///   호스트는 모든 발사 결과를 클라이언트에 보내 이펙트를 보여 준다.
/// - 장전: 클라이언트 사수가 총알을 넣고 실린더를 닫으면 넣은 수를 호스트에 보내고, 호스트 약실에 넣고 돌린다.
/// 아직 없는 것: 상대가 넣어서 사라진 여분 총알을 이쪽 화면에서 지우기, 사망·게임 종료 표시.
/// </summary>
public class LanGunSync : MonoBehaviour
{
    const string k_PresentedMessage = "BombDefuse.GunPresented";
    const string k_WithdrawnMessage = "BombDefuse.GunWithdrawn";
    const string k_PoseMessage = "BombDefuse.GunPose";
    const string k_ShotRequestMessage = "BombDefuse.GunShotRequest";
    const string k_ShotMessage = "BombDefuse.GunShot";
    const string k_ReloadMessage = "BombDefuse.GunReload";

    [SerializeField] GameFlowController m_Flow;
    [SerializeField] Gun m_Gun;
    [SerializeField, Min(1f), Tooltip("사수 기기가 총 위치를 보내는 횟수(초당)")] float m_PoseSendRate = 30f;

    NetworkManager m_NetworkManager;
    // 총이 꺼내져 있는 좌석. 0이면 치워져 있다
    int m_PresentedSeat;
    float m_NextPoseTime;

    bool IsConnected => m_NetworkManager != null && m_NetworkManager.IsListening && (m_NetworkManager.IsServer || m_NetworkManager.IsConnectedClient);
    bool IsLocalShooter => m_PresentedSeat != 0 && m_PresentedSeat == NetworkPlayer.LocalSeatNumber;

    void Start()
    {
        m_NetworkManager = NetworkManager.Singleton;
        if (m_NetworkManager == null)
        {
            Debug.LogWarning("[LanGunSync] No NetworkManager in the scene", this);
            return;
        }

        m_NetworkManager.OnClientStarted += RegisterHandlers;
        m_NetworkManager.OnServerStarted += RegisterHandlers;
        m_Flow.GunPresented += OnGunPresented;
        m_Flow.GunWithdrawn += OnGunWithdrawn;
        m_Gun.Fired += OnGunFired;
        m_Gun.ShotRequested += OnShotRequested;
        m_Gun.CylinderClosed += OnCylinderClosed;
    }

    void OnDestroy()
    {
        if (m_NetworkManager != null)
        {
            m_NetworkManager.OnClientStarted -= RegisterHandlers;
            m_NetworkManager.OnServerStarted -= RegisterHandlers;
        }
        if (m_Flow != null)
        {
            m_Flow.GunPresented -= OnGunPresented;
            m_Flow.GunWithdrawn -= OnGunWithdrawn;
        }
        if (m_Gun != null)
        {
            m_Gun.Fired -= OnGunFired;
            m_Gun.ShotRequested -= OnShotRequested;
            m_Gun.CylinderClosed -= OnCylinderClosed;
        }
    }

    // 메시지 관리자는 접속할 때마다 새로 만들어지므로 매번 등록한다
    void RegisterHandlers()
    {
        var messaging = m_NetworkManager.CustomMessagingManager;
        messaging.RegisterNamedMessageHandler(k_PoseMessage, ReceivePose);
        if (m_NetworkManager.IsServer)
        {
            messaging.RegisterNamedMessageHandler(k_ShotRequestMessage, ReceiveShotRequest);
            messaging.RegisterNamedMessageHandler(k_ReloadMessage, ReceiveReload);
            m_Gun.ResolvesShotsRemotely = false;
            return;
        }

        messaging.RegisterNamedMessageHandler(k_PresentedMessage, ReceivePresented);
        messaging.RegisterNamedMessageHandler(k_WithdrawnMessage, ReceiveWithdrawn);
        messaging.RegisterNamedMessageHandler(k_ShotMessage, ReceiveShot);
        // 클라이언트 약실은 쓰지 않는다
        m_Gun.ResolvesShotsRemotely = true;
    }

    void Update()
    {
        if (!IsConnected || !IsLocalShooter || !m_Gun.IsPresented || Time.unscaledTime < m_NextPoseTime)
            return;

        m_NextPoseTime = Time.unscaledTime + 1f / m_PoseSendRate;
        using (var writer = new FastBufferWriter(32, Allocator.Temp))
        {
            writer.WriteValueSafe(m_Gun.transform.position);
            writer.WriteValueSafe(m_Gun.transform.rotation);
            // 위치는 자주 보내고 최신 것만 쓰면 되므로 순서만 지키고 재전송은 하지 않는다
            SendToOthers(k_PoseMessage, writer, NetworkDelivery.UnreliableSequenced);
        }
    }

    // ── 꺼내기·치우기 (호스트 → 클라이언트) ──

    void OnGunPresented(int seat)
    {
        m_PresentedSeat = seat;
        if (!IsServerWithClients())
            return;

        using (var writer = new FastBufferWriter(4, Allocator.Temp))
        {
            writer.WriteValueSafe(seat);
            SendToOthers(k_PresentedMessage, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    void OnGunWithdrawn()
    {
        m_PresentedSeat = 0;
        if (!IsServerWithClients())
            return;

        using (var writer = new FastBufferWriter(0, Allocator.Temp))
            SendToOthers(k_WithdrawnMessage, writer, NetworkDelivery.ReliableSequenced);
    }

    void ReceivePresented(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
            return;

        reader.ReadValueSafe(out int seat);
        m_PresentedSeat = seat;
        m_Flow.PresentGunForSeat(seat);
    }

    void ReceiveWithdrawn(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
            return;

        m_PresentedSeat = 0;
        m_Flow.WithdrawGun();
    }

    // ── 위치 (사수 기기 → 다른 기기) ──

    void ReceivePose(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Vector3 position);
        reader.ReadValueSafe(out Quaternion rotation);

        // 사수가 아닌 기기가 보낸 위치는 받지 않는다
        if (IsLocalShooter || !IsSenderShooter(senderClientId))
            return;

        m_Gun.FollowRemotePose(position, rotation);
    }

    bool IsSenderShooter(ulong senderClientId)
    {
        var sender = FindPlayer(senderClientId);
        return sender != null && sender.SeatNumber == m_PresentedSeat;
    }

    // ── 발사 ──

    // 클라이언트: 공이치기가 내려갔다. 레이를 호스트에 보낸다
    void OnShotRequested(Vector3 rayOrigin, Vector3 direction)
    {
        if (!IsConnected || m_NetworkManager.IsServer)
            return;

        using (var writer = new FastBufferWriter(32, Allocator.Temp))
        {
            writer.WriteValueSafe(rayOrigin);
            writer.WriteValueSafe(direction);
            m_NetworkManager.CustomMessagingManager.SendNamedMessage(k_ShotRequestMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    // 호스트: 클라이언트 사수의 한 발을 판정한다. 결과는 Fired → OnGunFired로 다시 보낸다
    void ReceiveShotRequest(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Vector3 rayOrigin);
        reader.ReadValueSafe(out Vector3 direction);

        if (!IsSenderShooter(senderClientId) || m_Flow.StateMachine.Current != GameState.ShooterTurn || m_Gun.IsTriggerLocked)
        {
            Debug.Log($"[LanGunSync] Ignored shot from client {senderClientId} (not its turn)", this);
            return;
        }

        m_Gun.FireRemote(rayOrigin, direction);
    }

    // 호스트: 모든 발사 결과를 클라이언트에 보내 이펙트를 보여 준다
    void OnGunFired(Gun.ShotResult result)
    {
        if (!IsServerWithClients())
            return;

        using (var writer = new FastBufferWriter(64, Allocator.Temp))
        {
            writer.WriteValueSafe(result.Origin);
            writer.WriteValueSafe(result.Direction);
            writer.WriteValueSafe(result.EndPoint);
            writer.WriteValueSafe(result.IsLive);
            writer.WriteValueSafe(result.DidHit);
            SendToOthers(k_ShotMessage, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    void ReceiveShot(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
            return;

        var result = new Gun.ShotResult();
        reader.ReadValueSafe(out result.Origin);
        reader.ReadValueSafe(out result.Direction);
        reader.ReadValueSafe(out result.EndPoint);
        reader.ReadValueSafe(out result.IsLive);
        reader.ReadValueSafe(out result.DidHit);
        m_Gun.ShowRemoteShot(result);
    }

    // ── 장전 (클라이언트 사수 → 호스트) ──

    void OnCylinderClosed(int inserted)
    {
        // 클라이언트 약실은 쓰지 않으므로 넣은 수만 호스트에 알린다
        if (!IsConnected || m_NetworkManager.IsServer || !IsLocalShooter || inserted == 0)
            return;

        using (var writer = new FastBufferWriter(4, Allocator.Temp))
        {
            writer.WriteValueSafe(inserted);
            m_NetworkManager.CustomMessagingManager.SendNamedMessage(k_ReloadMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    void ReceiveReload(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int inserted);
        if (!IsSenderShooter(senderClientId) || m_Flow.StateMachine.Current != GameState.ShooterTurn)
            return;

        m_Gun.InsertRemote(inserted);
    }

    // ── 공통 ──

    bool IsServerWithClients() => m_NetworkManager != null && m_NetworkManager.IsServer && m_NetworkManager.ConnectedClientsIds.Count > 1;

    // 호스트는 모든 클라이언트에, 클라이언트는 호스트에 보낸다
    void SendToOthers(string messageName, FastBufferWriter writer, NetworkDelivery delivery)
    {
        var messaging = m_NetworkManager.CustomMessagingManager;
        if (!m_NetworkManager.IsServer)
        {
            messaging.SendNamedMessage(messageName, NetworkManager.ServerClientId, writer, delivery);
            return;
        }

        var clients = new List<ulong>(m_NetworkManager.ConnectedClientsIds);
        clients.Remove(NetworkManager.ServerClientId);
        if (clients.Count > 0)
            messaging.SendNamedMessage(messageName, clients, writer, delivery);
    }

    static NetworkPlayer FindPlayer(ulong clientId)
    {
        foreach (var player in NetworkPlayer.Players)
            if (player.OwnerClientId == clientId)
                return player;
        return null;
    }
}
