using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;

/// <summary>
/// 보이스챗 (Vivox). NetworkManager와 같은 오브젝트에 붙인다.
/// 게임은 LAN으로 돌지만 Vivox는 Unity 서버를 거치므로 양쪽 기기 모두 인터넷이 있어야 한다.
/// 흐름: Play 시작 때 Unity 서비스 초기화·익명 로그인·Vivox 초기화 → LAN 접속이 되면 Vivox 로그인 후 방 채널 참가 → 나가면 퇴장·로그아웃.
/// 보이스챗이 안 돼도(인터넷 없음, 권한 없음) 게임은 그대로 진행된다. 실패는 상태 문구와 경고 로그로만 알린다.
/// 방이 하나뿐이라 채널 이름은 고정이다. 호스트가 바뀌어도 같은 채널에 모인다.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
public class LanVoiceChat : MonoBehaviour
{
    [SerializeField, Tooltip("두 사람이 함께 들어가는 채널 이름. 같은 Unity Cloud 프로젝트에서 같은 이름이면 같은 방이다")] string m_ChannelName = "bombdefuse-room";
    [SerializeField, Tooltip("좌석 번호가 정해지기를 기다리는 최대 시간(초). 표시 이름에 쓴다")] float m_SeatWaitTimeout = 3f;

    static LanVoiceChat s_Instance;

    NetworkManager m_NetworkManager;
    bool m_ServicesReady;
    bool m_WantConnected;
    Coroutine m_JoinRoutine;

    public static LanVoiceChat Instance => s_Instance;

    /// <summary>지금 상태를 사람이 읽을 수 있게 적은 문구.</summary>
    public string Status { get; private set; } = "Voice: off";

    public event Action<string> StatusChanged;

    /// <summary>채널에 들어가 있어서 말할 수 있는 상태.</summary>
    public bool IsInChannel => m_ServicesReady && VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn && VivoxService.Instance.ActiveChannels.Count > 0;

    public bool IsMuted => m_ServicesReady && VivoxService.Instance != null && VivoxService.Instance.IsInputDeviceMuted;

    void Awake()
    {
        s_Instance = this;
        m_NetworkManager = GetComponent<NetworkManager>();
    }

    void OnEnable()
    {
        m_NetworkManager.OnClientConnectedCallback += OnClientConnected;
        m_NetworkManager.OnClientStopped += OnClientStopped;
    }

    void OnDisable()
    {
        if (m_NetworkManager != null)
        {
            m_NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            m_NetworkManager.OnClientStopped -= OnClientStopped;
        }
    }

    void OnDestroy()
    {
        if (s_Instance == this)
            s_Instance = null;
        if (m_ServicesReady && VivoxService.Instance != null)
        {
            VivoxService.Instance.ParticipantAddedToChannel -= OnParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel -= OnParticipantRemoved;
        }
    }

    async void Start()
    {
        await InitializeServices();
    }

    // ── 준비: Unity 서비스 → 익명 로그인 → Vivox ──

    async Task InitializeServices()
    {
        try
        {
            SetStatus("Voice: starting");
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            // 같은 컴퓨터에서 에디터 2인 테스트를 하면 두 창이 같은 익명 계정을 쓰게 되어 Vivox가 한쪽을 끊는다.
            // 가상 플레이어는 다른 프로필(= 다른 익명 계정)로 로그인한다
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                if (IsVirtualPlayer)
                    AuthenticationService.Instance.SwitchProfile("virtualplayer");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            await VivoxService.Instance.InitializeAsync();
            VivoxService.Instance.ParticipantAddedToChannel += OnParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantRemoved;
            m_ServicesReady = true;
            SetStatus("Voice: ready");

            // 서비스가 늦게 준비됐는데 이미 LAN에 붙어 있으면 바로 들어간다
            if (m_WantConnected)
                StartJoin();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LanVoiceChat] Voice unavailable: {e.Message}", this);
            SetStatus("Voice: unavailable");
        }
    }

    static bool IsVirtualPlayer => Application.isEditor && Application.dataPath.Replace('\\', '/').Contains("/Library/VP/");

    // ── LAN 접속에 맞춰 들어가고 나가기 ──

    void OnClientConnected(ulong clientId)
    {
        if (clientId != m_NetworkManager.LocalClientId)
            return;

        m_WantConnected = true;
        if (m_ServicesReady)
            StartJoin();
    }

    void OnClientStopped(bool wasHost)
    {
        m_WantConnected = false;
        if (m_JoinRoutine != null)
        {
            StopCoroutine(m_JoinRoutine);
            m_JoinRoutine = null;
        }
        if (m_ServicesReady)
            _ = LeaveChannel();
    }

    void StartJoin()
    {
        if (m_JoinRoutine != null)
            StopCoroutine(m_JoinRoutine);
        m_JoinRoutine = StartCoroutine(JoinWhenSeatKnown());
    }

    // 표시 이름에 좌석 번호를 쓰려고 좌석이 정해질 때까지 잠깐 기다린다
    IEnumerator JoinWhenSeatKnown()
    {
        RequestMicrophonePermission();

        var deadline = Time.unscaledTime + m_SeatWaitTimeout;
        while (NetworkPlayer.LocalSeatNumber <= 0 && Time.unscaledTime < deadline)
            yield return null;

        m_JoinRoutine = null;
        var seat = NetworkPlayer.LocalSeatNumber;
        _ = JoinChannel(seat > 0 ? $"Seat{seat}" : "Player");
    }

    async Task JoinChannel(string displayName)
    {
        try
        {
            var vivox = VivoxService.Instance;
            if (!vivox.IsLoggedIn)
            {
                SetStatus("Voice: logging in");
                await vivox.LoginAsync(new LoginOptions { DisplayName = displayName });
            }

            // 접속이 끊겼으면 들어가지 않는다
            if (!m_WantConnected)
                return;

            if (vivox.ActiveChannels.Count == 0)
            {
                SetStatus("Voice: joining");
                await vivox.JoinGroupChannelAsync(m_ChannelName, ChatCapability.AudioOnly);
            }

            SetStatus(vivox.IsInputDeviceMuted ? "Voice: on (muted)" : "Voice: on");
            Debug.Log($"[LanVoiceChat] Joined '{m_ChannelName}' as {displayName}", this);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LanVoiceChat] Could not join voice: {e.Message}", this);
            SetStatus("Voice: failed");
        }
    }

    async Task LeaveChannel()
    {
        try
        {
            var vivox = VivoxService.Instance;
            if (vivox.ActiveChannels.Count > 0)
                await vivox.LeaveAllChannelsAsync();
            if (vivox.IsLoggedIn)
                await vivox.LogoutAsync();
            SetStatus("Voice: ready");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LanVoiceChat] Could not leave voice: {e.Message}", this);
        }
    }

    // ── 음소거 ──

    /// <summary>내 마이크를 켜고 끈다. 채널에 없어도 설정은 남는다.</summary>
    public void ToggleMute()
    {
        if (!m_ServicesReady)
            return;

        var vivox = VivoxService.Instance;
        if (vivox.IsInputDeviceMuted)
            vivox.UnmuteInputDevice();
        else
            vivox.MuteInputDevice();

        if (IsInChannel)
            SetStatus(vivox.IsInputDeviceMuted ? "Voice: on (muted)" : "Voice: on");
        Debug.Log($"[LanVoiceChat] Microphone {(vivox.IsInputDeviceMuted ? "muted" : "unmuted")}", this);
    }

    // ── 참가자 ──

    void OnParticipantAdded(VivoxParticipant participant)
    {
        if (participant.PlayerId == AuthenticationService.Instance.PlayerId)
            return;
        Debug.Log($"[LanVoiceChat] {participant.DisplayName} joined voice", this);
    }

    void OnParticipantRemoved(VivoxParticipant participant)
    {
        if (participant.PlayerId == AuthenticationService.Instance.PlayerId)
            return;
        Debug.Log($"[LanVoiceChat] {participant.DisplayName} left voice", this);
    }

    // Quest 단독(Android) 빌드는 마이크 권한을 물어봐야 한다. PC에서는 바로 허용된다
    static void RequestMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
#endif
    }

    void SetStatus(string status)
    {
        if (Status == status)
            return;

        Status = status;
        StatusChanged?.Invoke(status);
    }
}
