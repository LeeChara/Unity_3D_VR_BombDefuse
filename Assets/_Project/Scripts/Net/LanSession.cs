using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// 같은 네트워크(LAN)에서 호스트를 열고 접속한다. NetworkManager와 같은 오브젝트에 붙인다.
/// 호스트는 게임 포트를 브로드캐스트로 알리고, 접속하는 쪽은 그 알림을 듣고 바로 붙는다.
/// 알림을 못 들으면 <see cref="m_FallbackAddress"/>로 붙는다 (에디터에서 둘을 띄울 때는 127.0.0.1).
/// 정원(2명)이 차면 다음 접속은 거절한다. 세션 서비스(Relay·Lobby)는 쓰지 않는다.
/// 정원이 차면 호스트가 <see cref="StartGame"/>으로 게임을 시작하고, 모두에게 <see cref="GameStarted"/>를 알린다.
/// </summary>
[RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
public class LanSession : MonoBehaviour
{
    [SerializeField, Tooltip("게임 포트")] ushort m_Port = 7777;
    [SerializeField, Tooltip("호스트 알림을 주고받는 UDP 포트")] ushort m_DiscoveryPort = 47777;
    [SerializeField, Min(0.1f), Tooltip("호스트 알림 간격(초)")] float m_AnnounceInterval = 1f;
    [SerializeField, Min(0f), Tooltip("호스트 알림을 기다리는 시간(초). 지나면 아래 주소로 붙는다")] float m_DiscoveryTimeout = 3f;
    [SerializeField, Tooltip("알림을 못 들었을 때 붙을 주소")] string m_FallbackAddress = "127.0.0.1";
    [SerializeField, Min(1), Tooltip("호스트 포함 최대 인원")] int m_MaxPlayers = 2;

    [Header("Debug")]
    [SerializeField, Tooltip("에디터·개발 빌드에서 화면 왼쪽 위에 호스트/접속 버튼을 띄운다 (Multiplayer Play Mode용)")] bool m_ShowDebugGui = true;

    const string k_StartGameMessage = "BombDefuse.StartGame";

    static LanSession s_Instance;

    NetworkManager m_NetworkManager;
    UnityTransport m_Transport;
    readonly LanDiscovery m_Discovery = new LanDiscovery();
    bool m_IsDiscovering;
    float m_DiscoveryEndTime;
    float m_NextAnnounceTime;

    public static LanSession Instance => s_Instance;

    /// <summary>지금 상태를 사람이 읽을 수 있게 적은 문구.</summary>
    public string Status { get; private set; } = "Offline";

    public event Action<string> StatusChanged;

    /// <summary>호스트를 열었거나, 접속했거나, 접속하는 중.</summary>
    public bool IsActive => m_IsDiscovering || m_NetworkManager.IsListening;

    /// <summary>이번 접속에서 게임을 시작했는지. 나가거나 끊기면 다시 false.</summary>
    public bool IsGameStarted { get; private set; }

    /// <summary>호스트이고, 정원이 찼고, 아직 시작하지 않았다.</summary>
    public bool CanStartGame => m_NetworkManager.IsServer && !IsGameStarted && m_NetworkManager.ConnectedClientsIds.Count >= m_MaxPlayers;

    /// <summary>호스트가 게임을 시작했다. 호스트와 클라이언트 모두에서 한 번씩 불린다.</summary>
    public event Action GameStarted;

    /// <summary>게임 중에 상대가 나갔다 (호스트: 클라이언트가 나감, 클라이언트: 호스트가 끊김). 남은 쪽이 이긴다.</summary>
    public event Action OpponentLeft;

    void Awake()
    {
        s_Instance = this;
        m_NetworkManager = GetComponent<NetworkManager>();
        m_Transport = GetComponent<UnityTransport>();
        m_NetworkManager.NetworkConfig.ConnectionApproval = true;
    }

    void OnEnable()
    {
        m_NetworkManager.ConnectionApprovalCallback += ApproveConnection;
        m_NetworkManager.OnClientConnectedCallback += OnClientConnected;
        m_NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        m_NetworkManager.OnTransportFailure += OnTransportFailure;
    }

    void OnDisable()
    {
        if (m_NetworkManager != null)
        {
            m_NetworkManager.ConnectionApprovalCallback -= ApproveConnection;
            m_NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            m_NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            m_NetworkManager.OnTransportFailure -= OnTransportFailure;
        }
        StopDiscovery();
    }

    void OnDestroy()
    {
        if (s_Instance == this)
            s_Instance = null;
        m_Discovery.Dispose();
    }

    public void StartHost()
    {
        if (IsActive)
            return;

        var address = LanDiscovery.GetLocalAddress();
        // 모든 어댑터에서 받는다. 표시용 주소는 LAN 주소
        m_Transport.SetConnectionData(address, m_Port, "0.0.0.0");
        if (!m_NetworkManager.StartHost())
        {
            SetStatus("Failed to host. Is the port already in use?");
            return;
        }

        m_Discovery.StartAnnouncing(m_DiscoveryPort);
        m_NextAnnounceTime = 0f;
        SetStatus($"Hosting at {address}:{m_Port}\nWaiting for player 2");
    }

    public void Join()
    {
        if (IsActive)
            return;

        try
        {
            m_Discovery.StartListening(m_DiscoveryPort);
        }
        catch (System.Net.Sockets.SocketException e)
        {
            Debug.LogWarning($"[LanSession] Can't listen for hosts: {e.Message}");
            Connect(m_FallbackAddress, m_Port);
            return;
        }

        m_IsDiscovering = true;
        m_DiscoveryEndTime = Time.unscaledTime + m_DiscoveryTimeout;
        SetStatus("Looking for host...");
    }

    public void Leave()
    {
        StopDiscovery();
        if (m_NetworkManager.IsListening)
            m_NetworkManager.Shutdown();
        IsGameStarted = false;
        SetStatus("Offline");
    }

    /// <summary>호스트만 쓴다. 클라이언트에게 시작을 알리고 자기도 시작한다.</summary>
    public void StartGame()
    {
        if (!CanStartGame)
            return;

        var clients = new List<ulong>(m_NetworkManager.ConnectedClientsIds);
        clients.Remove(NetworkManager.ServerClientId);
        using (var writer = new FastBufferWriter(0, Allocator.Temp))
            m_NetworkManager.CustomMessagingManager.SendNamedMessage(k_StartGameMessage, clients, writer);
        OnGameStarted();
    }

    void OnStartGameMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId == NetworkManager.ServerClientId)
            OnGameStarted();
    }

    void OnGameStarted()
    {
        if (IsGameStarted)
            return;

        IsGameStarted = true;
        SetStatus("Game started");
        GameStarted?.Invoke();
    }

    void Update()
    {
        if (m_IsDiscovering)
        {
            if (m_Discovery.TryReceive(out var address, out var port))
            {
                StopDiscovery();
                Connect(address, port);
            }
            else if (Time.unscaledTime >= m_DiscoveryEndTime)
            {
                StopDiscovery();
                Connect(m_FallbackAddress, m_Port);
            }
        }
        else if (m_NetworkManager.IsServer && Time.unscaledTime >= m_NextAnnounceTime)
        {
            m_NextAnnounceTime = Time.unscaledTime + m_AnnounceInterval;
            m_Discovery.Broadcast(m_Port);
        }
    }

    void Connect(string address, ushort port)
    {
        m_Transport.SetConnectionData(address, port);
        if (m_NetworkManager.StartClient())
        {
            // 메시지 관리자는 접속할 때마다 새로 만들어지므로 매번 등록한다
            m_NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(k_StartGameMessage, OnStartGameMessage);
            SetStatus($"Connecting to {address}:{port}...");
        }
        else
        {
            SetStatus($"Failed to connect to {address}:{port}");
        }
    }

    void StopDiscovery()
    {
        m_IsDiscovering = false;
        m_Discovery.Dispose();
    }

    void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        // 호스트 자신은 ConnectedClientsIds에 아직 없을 때 승인된다
        var isFull = m_NetworkManager.ConnectedClientsIds.Count >= m_MaxPlayers;
        response.Approved = !isFull;
        response.CreatePlayerObject = !isFull;
        response.Reason = isFull ? "Room is full" : null;
    }

    void OnClientConnected(ulong clientId)
    {
        if (m_NetworkManager.IsServer)
        {
            var count = m_NetworkManager.ConnectedClientsIds.Count;
            if (count >= m_MaxPlayers)
            {
                // 정원이 찼으니 더 알리지 않는다
                m_Discovery.Dispose();
                SetStatus($"Players {count}/{m_MaxPlayers}\nPress START");
            }
        }
        else if (clientId == m_NetworkManager.LocalClientId)
        {
            SetStatus("Connected\nWaiting for host to start");
        }
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (m_NetworkManager.IsServer)
        {
            if (clientId == NetworkManager.ServerClientId || m_NetworkManager.ShutdownInProgress)
                return;

            // 자리가 났으니 다시 알린다
            m_Discovery.StartAnnouncing(m_DiscoveryPort);
            if (IsGameStarted)
                OpponentLeft?.Invoke();
            IsGameStarted = false;
            SetStatus("Player 2 left\nWaiting for player 2");
            return;
        }

        // 클라이언트: 호스트가 끊었거나, 정원이 찼거나, 접속에 실패했다
        if (IsGameStarted)
            OpponentLeft?.Invoke();
        IsGameStarted = false;
        var reason = m_NetworkManager.DisconnectReason;
        SetStatus(string.IsNullOrEmpty(reason) ? "Disconnected" : $"Disconnected: {reason}");
    }

    void OnTransportFailure()
    {
        StopDiscovery();
        SetStatus("Network error");
    }

    void SetStatus(string status)
    {
        if (Status == status)
            return;

        Status = status;
        Debug.Log($"[LanSession] {status.Replace('\n', ' ')}");
        StatusChanged?.Invoke(status);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void OnGUI()
    {
        if (!m_ShowDebugGui)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 260, 190), GUI.skin.box);
        GUILayout.Label(Status);
        GUI.enabled = !IsActive;
        if (GUILayout.Button("Host"))
            StartHost();
        if (GUILayout.Button("Join"))
            Join();
        GUI.enabled = CanStartGame;
        if (GUILayout.Button("Start Game"))
            StartGame();
        GUI.enabled = IsActive;
        if (GUILayout.Button("Leave"))
            Leave();
        GUI.enabled = true;
        GUILayout.EndArea();
    }
#endif
}
