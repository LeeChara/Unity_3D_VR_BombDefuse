using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 호스트가 START를 누르면(<see cref="LanSession.GameStarted"/>) 멀티용으로 게임을 준비하고 호스트의 <see cref="GameFlowController"/>를 시작한다.
/// 준비: 이 기기의 좌석만 직접 플레이하게 하고(<see cref="GameFlowController.IsLocalSeat"/>), 테스트용 마네킹을 숨기고,
/// 각 좌석의 몸을 실제 플레이어(이 기기 사람은 XR Origin의 몸, 상대는 아바타의 몸)로 바꾼다.
/// 클라이언트는 진행을 돌리지 않고 호스트의 상태(<see cref="GameStateSync"/>)와 화면(<see cref="LanQuizSync"/>, <see cref="LanGunSync"/>)을 따른다.
/// 게임 중에 상대가 나가면(<see cref="LanSession.OpponentLeft"/>) 남은 쪽의 승리로 끝내고, 다시 START를 받을 수 있게 혼자 플레이 상태로 돌린다.
/// GameFlowController의 Start On Play는 꺼 둔다.
/// </summary>
public class LanGameStarter : MonoBehaviour
{
    [SerializeField] GameFlowController m_Flow;

    LanSession m_Session;
    // 이번 게임에서 이 기기의 좌석. 끊긴 뒤에는 NetworkPlayer가 사라져서 미리 적어 둔다
    int m_LocalSeat;

    void Start()
    {
        m_Session = LanSession.Instance;
        if (m_Session == null)
        {
            Debug.LogWarning("[LanGameStarter] No LanSession in the scene", this);
            return;
        }

        m_Session.GameStarted += OnGameStarted;
        m_Session.OpponentLeft += OnOpponentLeft;
    }

    void OnDestroy()
    {
        if (m_Session != null)
        {
            m_Session.GameStarted -= OnGameStarted;
            m_Session.OpponentLeft -= OnOpponentLeft;
        }
    }

    void OnGameStarted()
    {
        m_LocalSeat = NetworkPlayer.LocalSeatNumber;
        m_Flow.IsLocalSeat = seat => seat == NetworkPlayer.LocalSeatNumber;

        // 상대 자리에는 이제 실제 플레이어가 앉는다
        foreach (var mannequin in FindObjectsByType<TestMannequin>(FindObjectsSortMode.None))
            mannequin.gameObject.SetActive(false);

        var networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer)
            return;

        var seatCount = SeatRegistry.Instance != null ? SeatRegistry.Instance.Count : 2;
        for (var seat = 1; seat <= seatCount; seat++)
        {
            var body = NetworkPlayer.BodyAtSeat(seat);
            if (body == null)
                Debug.LogWarning($"[LanGameStarter] No body for seat {seat}", this);
            m_Flow.SetBody(seat, body);
        }

        m_Flow.StartGame();
    }

    void OnOpponentLeft()
    {
        var seatCount = SeatRegistry.Instance != null ? SeatRegistry.Instance.Count : 2;
        var leftSeat = m_LocalSeat == 1 ? 2 : 1;
        Debug.Log($"[LanGameStarter] 상대(좌석 {leftSeat})가 나갔다 → 좌석 {m_LocalSeat} 승리", this);

        // 끊긴 뒤에는 이 기기가 혼자 진행한다. 이 기기 좌석만 직접 플레이하고, 나간 좌석의 몸은 비운다
        var localSeat = m_LocalSeat;
        m_Flow.IsLocalSeat = seat => seat == localSeat;
        m_Flow.DrivesGame = true;
        if (seatCount >= leftSeat)
            m_Flow.SetBody(leftSeat, null);
        m_Flow.EndByOpponentLeft(leftSeat);
    }
}
