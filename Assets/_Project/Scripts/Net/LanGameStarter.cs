using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 호스트가 START를 누르면(<see cref="LanSession.GameStarted"/>) 호스트의 <see cref="GameFlowController"/>를 시작한다.
/// 클라이언트는 진행을 돌리지 않고 호스트가 보내 주는 화면을 따른다(<see cref="LanQuizSync"/>).
/// GameFlowController의 Start On Play는 꺼 둔다.
/// </summary>
public class LanGameStarter : MonoBehaviour
{
    [SerializeField] GameFlowController m_Flow;

    LanSession m_Session;

    void Start()
    {
        m_Session = LanSession.Instance;
        if (m_Session == null)
        {
            Debug.LogWarning("[LanGameStarter] No LanSession in the scene", this);
            return;
        }

        m_Session.GameStarted += OnGameStarted;
    }

    void OnDestroy()
    {
        if (m_Session != null)
            m_Session.GameStarted -= OnGameStarted;
    }

    void OnGameStarted()
    {
        var networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsServer)
            m_Flow.StartGame();
    }
}
