using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 템포 조정용 기록. 한 바퀴(문제 하나가 나와서 다음 문제가 나오기까지)마다 상태별로 걸린 시간을 콘솔에 남기고,
/// 게임이 끝나면 한 판 전체 시간을 남긴다. 헤드셋 플레이테스트 뒤 콘솔(또는 Player.log)에서 <c>[Tempo]</c>로 찾는다.
/// 기다리는 시간 값은 GameFlowController의 Timing 칸, 대사 표시 시간은 HostDialogue에 있다.
/// </summary>
public class TempoLog : MonoBehaviour
{
    [SerializeField] GameFlowController m_Flow;

    readonly Dictionary<GameState, float> m_Round = new Dictionary<GameState, float>();
    readonly List<GameState> m_Order = new List<GameState>();
    GameState m_Current = GameState.Lobby;
    float m_EnteredAt;
    float m_GameStartedAt = -1f;
    int m_RoundNumber;

    void Start()
    {
        if (m_Flow != null)
            m_Flow.StateMachine.StateChanged += OnStateChanged;
    }

    void OnDestroy()
    {
        if (m_Flow != null)
            m_Flow.StateMachine.StateChanged -= OnStateChanged;
    }

    void OnStateChanged(GameState previous, GameState next)
    {
        var now = Time.time;
        Add(m_Current, now - m_EnteredAt);
        m_Current = next;
        m_EnteredAt = now;

        switch (next)
        {
            case GameState.Seating:
                m_GameStartedAt = now;
                m_RoundNumber = 0;
                Clear();
                break;
            case GameState.QuizActive:
                // 첫 문제 앞(착석·규칙 설명)은 따로 한 줄로 남긴다
                Flush(m_RoundNumber == 0 ? "시작~첫 문제" : $"{m_RoundNumber}번째 문제");
                m_RoundNumber++;
                break;
            case GameState.GameOver:
                Flush($"{m_RoundNumber}번째 문제 (마지막)");
                if (m_GameStartedAt >= 0f)
                    Debug.Log($"[Tempo] 한 판 {now - m_GameStartedAt:0.0}초, 문제 {m_RoundNumber}개, 공포 단계 {(m_Flow != null ? m_Flow.DreadLevel : 0)}", this);
                break;
        }
    }

    void Add(GameState state, float seconds)
    {
        if (!m_Round.ContainsKey(state))
        {
            m_Round[state] = 0f;
            m_Order.Add(state);
        }

        m_Round[state] += seconds;
    }

    void Flush(string label)
    {
        if (m_Order.Count == 0)
            return;

        var total = 0f;
        var parts = new StringBuilder();
        foreach (var state in m_Order)
        {
            total += m_Round[state];
            if (parts.Length > 0)
                parts.Append(", ");
            parts.Append(state).Append(' ').Append(m_Round[state].ToString("0.0"));
        }

        Debug.Log($"[Tempo] {label}: {total:0.0}초 ({parts})", this);
        Clear();
    }

    void Clear()
    {
        m_Round.Clear();
        m_Order.Clear();
    }
}
