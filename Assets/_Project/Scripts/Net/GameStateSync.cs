using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 호스트의 게임 상태를 클라이언트에 맞춘다 (docs/03_tech_review.md 3절). GameFlow 오브젝트에 NetworkObject와 함께 붙인다.
/// - 현재 상태는 <see cref="NetworkVariable{T}"/> 하나로 둔다. 늦게 들어온 클라이언트도 지금 상태를 바로 안다.
/// - 상태가 바뀔 때마다 RPC도 보낸다. 한 틱 안에 여러 번 바뀌면(Lobby → Seating → Intro → QuizActive) NetworkVariable은 마지막 값만 남지만,
///   RPC는 순서대로 다 오므로 클라이언트의 상태 머신도 같은 순서로 지나간다. 조명·사운드가 중간 상태를 놓치지 않는다.
/// 클라이언트의 <see cref="GameFlowController"/>는 <see cref="GameFlowController.DrivesGame"/>를 꺼서 상태만 비추고 진행은 하지 않는다.
/// </summary>
public class GameStateSync : NetworkBehaviour
{
    [SerializeField] GameFlowController m_Flow;

    readonly NetworkVariable<GameState> m_State = new NetworkVariable<GameState>(GameState.Lobby);

    /// <summary>호스트의 현재 상태. 접속 전에는 Lobby.</summary>
    public GameState Current => m_State.Value;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            m_State.Value = m_Flow.StateMachine.Current;
            m_Flow.StateMachine.StateChanged += OnHostStateChanged;
            return;
        }

        m_Flow.DrivesGame = false;
        m_Flow.StateMachine.Sync(m_State.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            m_Flow.StateMachine.StateChanged -= OnHostStateChanged;
            return;
        }

        // 나가면 다시 혼자 플레이할 수 있게
        m_Flow.DrivesGame = true;
    }

    void OnHostStateChanged(GameState previous, GameState next)
    {
        m_State.Value = next;
        StateChangedRpc(next);
    }

    [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Reliable)]
    void StateChangedRpc(GameState next) => m_Flow.StateMachine.Sync(next);
}
