using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 진행자 대사를 클라이언트 화면에도 띄운다. 대사는 호스트의 <see cref="HostDialogue"/>가 고르고(진행 컨트롤러는 호스트에서만 돈다),
/// 고른 대사(id, 글, 표시 시간)를 Named Message로 보낸다. 클라이언트는 받은 그대로 자막에 띄운다.
/// </summary>
public class LanDialogueSync : MonoBehaviour
{
    const string k_LineMessage = "BombDefuse.HostLine";

    [SerializeField] HostDialogue m_Host;

    NetworkManager m_NetworkManager;

    void Start()
    {
        m_NetworkManager = NetworkManager.Singleton;
        if (m_NetworkManager == null)
        {
            Debug.LogWarning("[LanDialogueSync] No NetworkManager in the scene", this);
            return;
        }

        m_NetworkManager.OnClientStarted += RegisterHandlers;
        m_Host.LineShown += OnLineShown;
    }

    void OnDestroy()
    {
        if (m_NetworkManager != null)
            m_NetworkManager.OnClientStarted -= RegisterHandlers;
        if (m_Host != null)
            m_Host.LineShown -= OnLineShown;
    }

    // 메시지 관리자는 접속할 때마다 새로 만들어지므로 매번 등록한다. 받는 쪽은 클라이언트뿐이다
    void RegisterHandlers()
    {
        if (!m_NetworkManager.IsServer)
            m_NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(k_LineMessage, ReceiveLine);
    }

    // 호스트: 고른 대사를 클라이언트에 보낸다
    void OnLineShown(string lineId, string text, float duration)
    {
        if (m_NetworkManager == null || !m_NetworkManager.IsServer || m_NetworkManager.ConnectedClientsIds.Count < 2)
            return;

        var clients = new List<ulong>(m_NetworkManager.ConnectedClientsIds);
        clients.Remove(NetworkManager.ServerClientId);

        // 대사 글이 길 수 있어 필요하면 늘어나게 한다
        using (var writer = new FastBufferWriter(128, Allocator.Temp, 4096))
        {
            writer.WriteValueSafe(lineId ?? string.Empty);
            writer.WriteValueSafe(text ?? string.Empty);
            writer.WriteValueSafe(duration);
            m_NetworkManager.CustomMessagingManager.SendNamedMessage(k_LineMessage, clients, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    // 클라이언트: 호스트가 고른 대사를 띄운다
    void ReceiveLine(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
            return;

        reader.ReadValueSafe(out string lineId);
        reader.ReadValueSafe(out string text);
        reader.ReadValueSafe(out float duration);
        Debug.Log($"[LanDialogueSync] {text} ({lineId})", this);
        m_Host.ShowLine(lineId, text, duration);
    }
}
