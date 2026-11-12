using System;
using System.Collections.Generic;

/// <summary>
/// 게임 진행 상태와 허용된 전이만 관리한다. 네트워크와 씬을 모르는 순수 C#이다. 타이머와 판정은 하지 않는다.
/// 조명·사운드·UI는 <see cref="StateChanged"/>를 구독해서 반응한다.
/// 나중에 Net 쪽에서 호스트의 상태를 NetworkVariable로 동기화한다.
/// </summary>
public sealed class GameStateMachine
{
    // 각 상태에서 갈 수 있는 다음 상태 (docs/03_tech_review.md 3절 상태 표)
    static readonly Dictionary<GameState, GameState[]> k_Transitions = new Dictionary<GameState, GameState[]>
    {
        { GameState.Lobby, new[] { GameState.Seating } },
        { GameState.Seating, new[] { GameState.Intro } },
        { GameState.Intro, new[] { GameState.QuizActive } },
        { GameState.QuizActive, new[] { GameState.QuizResolve } },
        // QuizActive는 임시 경로: 양측 패널티를 만들기 전까지 정답자가 없으면 바로 다음 문제로 간다
        { GameState.QuizResolve, new[] { GameState.GunHandover, GameState.BothFailPenalty, GameState.QuizActive } },
        { GameState.GunHandover, new[] { GameState.ShooterTurn } },
        { GameState.ShooterTurn, new[] { GameState.ShotResolve } },
        { GameState.ShotResolve, new[] { GameState.HostReaction } },
        { GameState.HostReaction, new[] { GameState.DeathCheck } },
        { GameState.BothFailPenalty, new[] { GameState.DeathCheck } },
        { GameState.DeathCheck, new[] { GameState.QuizActive, GameState.GameOver } },
        { GameState.GameOver, new[] { GameState.Lobby } },
    };

    readonly Queue<GameState> m_PendingStates = new Queue<GameState>();
    bool m_IsNotifying;
    // 대기 중인 전이가 모두 끝난 뒤의 상태. 새 요청은 이 상태를 기준으로 허용 여부를 정한다
    GameState m_LastRequestedState;

    /// <summary>상태가 바뀔 때 (이전 상태, 새 상태).</summary>
    public event Action<GameState, GameState> StateChanged;

    public GameState Current { get; private set; }

    public GameStateMachine(GameState initial = GameState.Lobby)
    {
        Current = initial;
        m_LastRequestedState = initial;
    }

    /// <summary>
    /// 지금 요청하면 허용되는지. 알림 처리 중에 이미 요청된 전이가 있으면 그 전이가 끝난 뒤의 상태를 기준으로 본다.
    /// </summary>
    public bool CanTransitionTo(GameState next) => IsAllowed(m_LastRequestedState, next);

    /// <summary>
    /// 허용된 전이면 true를 돌려주고 상태를 바꾼 뒤 <see cref="StateChanged"/>를 알린다.
    /// 허용되지 않으면 false를 돌려주고 아무것도 바꾸지 않는다.
    /// 알림을 처리하는 도중에 부르면, 지금 알림이 끝난 뒤에 순서대로 바뀐다.
    /// </summary>
    public bool TryTransitionTo(GameState next)
    {
        if (!IsAllowed(m_LastRequestedState, next))
            return false;

        m_LastRequestedState = next;
        m_PendingStates.Enqueue(next);

        if (!m_IsNotifying)
            ProcessPendingStates();

        return true;
    }

    /// <summary>
    /// 네트워크로 받은 호스트의 상태를 그대로 적용한다 (클라이언트용). 전이 규칙은 검사하지 않는다. 호스트가 이미 검사했기 때문이다.
    /// 같은 상태면 아무것도 하지 않는다. 알림은 <see cref="TryTransitionTo"/>와 같은 순서 규칙을 따른다.
    /// </summary>
    public void Sync(GameState next)
    {
        if (next == m_LastRequestedState)
            return;

        m_LastRequestedState = next;
        m_PendingStates.Enqueue(next);

        if (!m_IsNotifying)
            ProcessPendingStates();
    }

    void ProcessPendingStates()
    {
        m_IsNotifying = true;
        try
        {
            while (m_PendingStates.Count > 0)
            {
                var next = m_PendingStates.Dequeue();
                var previous = Current;
                Current = next;
                StateChanged?.Invoke(previous, next);
            }
        }
        finally
        {
            m_IsNotifying = false;
        }
    }

    static bool IsAllowed(GameState from, GameState next)
    {
        // 상대가 접속을 끊으면 어디서든 끝낸다 (아직 시작 전인 Lobby와 이미 끝난 GameOver는 제외)
        if (next == GameState.GameOver && from != GameState.Lobby && from != GameState.GameOver)
            return true;

        return k_Transitions.TryGetValue(from, out var allowed) && Array.IndexOf(allowed, next) >= 0;
    }

    /// <summary>어떤 상태에서 갈 수 있는 다음 상태 목록 (연결 끊김으로 가는 GameOver 제외).</summary>
    public static IReadOnlyList<GameState> GetNextStates(GameState state) =>
        k_Transitions.TryGetValue(state, out var allowed) ? allowed : Array.Empty<GameState>();
}
