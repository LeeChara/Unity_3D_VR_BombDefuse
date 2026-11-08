using System.Collections.Generic;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

/// <summary>
/// 접속한 플레이어 한 명. NetworkManager의 Player Prefab이다.
/// 주인(그 플레이어 본인)은 자기 XR Origin의 머리·양손 위치를 Head/LeftHand/RightHand에 복사하고,
/// 자식의 NetworkTransform(주인 권한)이 그 위치를 다른 쪽에 보낸다. 주인 화면에서는 자기 아바타를 숨긴다.
/// 좌석은 호스트가 접속 순서대로 비어 있는 번호를 준다. 주인은 배정된 의자 뒤로 옮겨지고 그 의자에만 앉을 수 있다.
/// </summary>
public class NetworkPlayer : NetworkBehaviour
{
    [SerializeField] Transform m_Head;
    [SerializeField] Transform m_LeftHand;
    [SerializeField] Transform m_RightHand;

    static readonly List<NetworkPlayer> s_Players = new List<NetworkPlayer>();

    // 좌석 번호 (0 = 좌석 1). 호스트만 정한다. -1이면 아직 없음
    readonly NetworkVariable<int> m_SeatIndex = new NetworkVariable<int>(-1);

    XROrigin m_Origin;
    SeatedPlayer m_SeatedPlayer;
    XRInputModalityManager m_InputModality;

    /// <summary>이 기기의 플레이어. 접속 전에는 null.</summary>
    public static NetworkPlayer Local { get; private set; }

    public static IReadOnlyList<NetworkPlayer> Players => s_Players;

    public int SeatIndex => m_SeatIndex.Value;

    public override void OnNetworkSpawn()
    {
        s_Players.Add(this);

        if (IsServer)
            m_SeatIndex.Value = FindFreeSeatIndex();

        if (IsOwner)
            SetUpLocal();
    }

    public override void OnNetworkDespawn()
    {
        s_Players.Remove(this);

        if (IsOwner)
        {
            m_SeatIndex.OnValueChanged -= OnSeatIndexChanged;
            if (m_SeatedPlayer != null)
                m_SeatedPlayer.AssignedSeat = null;
            if (Local == this)
                Local = null;
        }
    }

    void SetUpLocal()
    {
        Local = this;

        // 자기 머리와 손은 XR 리그가 그린다
        foreach (var avatarRenderer in GetComponentsInChildren<Renderer>(true))
            avatarRenderer.enabled = false;

        m_Origin = FindAnyObjectByType<XROrigin>();
        if (m_Origin == null)
        {
            Debug.LogError("[NetworkPlayer] No XR Origin in the scene", this);
            return;
        }

        m_SeatedPlayer = m_Origin.GetComponent<SeatedPlayer>();
        m_InputModality = m_Origin.GetComponentInChildren<XRInputModalityManager>(true);

        m_SeatIndex.OnValueChanged += OnSeatIndexChanged;
        ApplySeat(m_SeatIndex.Value);
    }

    void OnSeatIndexChanged(int previous, int current) => ApplySeat(current);

    void ApplySeat(int index)
    {
        if (index < 0 || m_SeatedPlayer == null)
            return;

        var seat = SeatRegistry.Instance != null ? SeatRegistry.Instance.GetSeat(index) : null;
        if (seat == null)
        {
            Debug.LogError($"[NetworkPlayer] No seat {index + 1} in SeatRegistry", this);
            return;
        }

        m_SeatedPlayer.AssignedSeat = seat;
        m_SeatedPlayer.PlaceBehind(seat);
    }

    int FindFreeSeatIndex()
    {
        var count = SeatRegistry.Instance != null ? SeatRegistry.Instance.Count : 0;
        for (var index = 0; index < count; index++)
        {
            var taken = false;
            foreach (var player in s_Players)
            {
                if (player != this && player.m_SeatIndex.Value == index)
                    taken = true;
            }

            if (!taken)
                return index;
        }

        Debug.LogError("[NetworkPlayer] No free seat", this);
        return -1;
    }

    void LateUpdate()
    {
        if (!IsSpawned || !IsOwner || m_Origin == null)
            return;

        Copy(m_Origin.Camera.transform, m_Head);
        Copy(GetHandSource(true), m_LeftHand);
        Copy(GetHandSource(false), m_RightHand);
    }

    // 손 추적 중이면 손, 아니면 컨트롤러
    Transform GetHandSource(bool left)
    {
        if (m_InputModality == null)
            return null;

        var hand = left ? m_InputModality.leftHand : m_InputModality.rightHand;
        var controller = left ? m_InputModality.leftController : m_InputModality.rightController;
        if (hand != null && hand.activeInHierarchy)
            return hand.transform;
        return controller != null ? controller.transform : null;
    }

    static void Copy(Transform source, Transform target)
    {
        if (source != null && target != null)
            target.SetPositionAndRotation(source.position, source.rotation);
    }
}
