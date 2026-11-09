using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 호스트가 낸 퀴즈를 클라이언트 화면에 그대로 띄운다. 게임 진행(<see cref="GameFlowController"/>)은 호스트에서만 돈다.
/// 호스트: 진행 컨트롤러의 문제·종료·판정 색 이벤트를 Named Message로 보낸다.
/// 클라이언트: 받은 문제를 퀴즈 화면에 띄우고 남은 시간 줄을 자기 시계로 줄인다. 판정 색도 같이 표시한다.
/// 아직 없는 것: 클라이언트의 답 전송(지금은 클라이언트가 눌러도 아무 일도 없다).
/// </summary>
public class LanQuizSync : MonoBehaviour
{
    const string k_QuizShownMessage = "BombDefuse.QuizShown";
    const string k_QuizEndedMessage = "BombDefuse.QuizEnded";
    const string k_ButtonResultMessage = "BombDefuse.QuizButtonResult";

    [SerializeField] GameFlowController m_Flow;
    [SerializeField] QuizBoard m_Board;
    [SerializeField, Tooltip("좌석 1, 2 순서")] QuizTerminal[] m_Terminals;

    NetworkManager m_NetworkManager;
    bool m_IsTimerRunning;
    double m_QuizStartTime;
    float m_TimeLimit;

    void Start()
    {
        m_NetworkManager = NetworkManager.Singleton;
        if (m_NetworkManager == null)
        {
            Debug.LogWarning("[LanQuizSync] No NetworkManager in the scene", this);
            return;
        }

        m_NetworkManager.OnClientStarted += RegisterHandlers;
        m_Flow.QuizShown += OnQuizShown;
        m_Flow.QuizEnded += OnQuizEnded;
        m_Flow.ButtonResultShown += OnButtonResultShown;
    }

    void OnDestroy()
    {
        if (m_NetworkManager != null)
            m_NetworkManager.OnClientStarted -= RegisterHandlers;
        if (m_Flow != null)
        {
            m_Flow.QuizShown -= OnQuizShown;
            m_Flow.QuizEnded -= OnQuizEnded;
            m_Flow.ButtonResultShown -= OnButtonResultShown;
        }
    }

    void Update()
    {
        if (!m_IsTimerRunning)
            return;

        var remaining = m_TimeLimit > 0f ? (float)(1.0 - (Time.timeAsDouble - m_QuizStartTime) / m_TimeLimit) : 0f;
        m_Board.SetRemainingTime(Mathf.Clamp01(remaining));
    }

    // 메시지 관리자는 접속할 때마다 새로 만들어지므로 매번 등록한다
    void RegisterHandlers()
    {
        if (m_NetworkManager.IsServer)
            return;

        var messaging = m_NetworkManager.CustomMessagingManager;
        messaging.RegisterNamedMessageHandler(k_QuizShownMessage, ReceiveQuizShown);
        messaging.RegisterNamedMessageHandler(k_QuizEndedMessage, ReceiveQuizEnded);
        messaging.RegisterNamedMessageHandler(k_ButtonResultMessage, ReceiveButtonResult);
    }

    // ── 호스트: 보내기 ──

    void OnQuizShown(QuizQuestion question, float timeLimit)
    {
        if (!CanSend())
            return;

        // 문제 글이 길 수 있어 필요하면 늘어나게 한다
        using (var writer = new FastBufferWriter(256, Allocator.Temp, 8192))
        {
            writer.WriteValueSafe(question.Id ?? string.Empty);
            writer.WriteValueSafe(question.Difficulty);
            writer.WriteValueSafe(question.Category ?? string.Empty);
            writer.WriteValueSafe(question.Question ?? string.Empty);
            for (var i = 0; i < 4; i++)
            {
                var choice = question.Choices != null && i < question.Choices.Length ? question.Choices[i] : null;
                writer.WriteValueSafe(choice ?? string.Empty);
            }
            writer.WriteValueSafe(question.AnswerIndex);
            writer.WriteValueSafe(timeLimit);
            Send(k_QuizShownMessage, writer);
        }
    }

    void OnQuizEnded()
    {
        if (!CanSend())
            return;

        using (var writer = new FastBufferWriter(0, Allocator.Temp))
            Send(k_QuizEndedMessage, writer);
    }

    void OnButtonResultShown(int seat, int choiceIndex, bool correct)
    {
        if (!CanSend())
            return;

        using (var writer = new FastBufferWriter(16, Allocator.Temp))
        {
            writer.WriteValueSafe(seat);
            writer.WriteValueSafe(choiceIndex);
            writer.WriteValueSafe(correct);
            Send(k_ButtonResultMessage, writer);
        }
    }

    bool CanSend() => m_NetworkManager != null && m_NetworkManager.IsServer && m_NetworkManager.ConnectedClientsIds.Count > 1;

    void Send(string messageName, FastBufferWriter writer)
    {
        var clients = new List<ulong>(m_NetworkManager.ConnectedClientsIds);
        clients.Remove(NetworkManager.ServerClientId);
        m_NetworkManager.CustomMessagingManager.SendNamedMessage(messageName, clients, writer);
    }

    // ── 클라이언트: 받기 ──

    void ReceiveQuizShown(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
            return;

        var question = new QuizQuestion();
        reader.ReadValueSafe(out question.Id);
        reader.ReadValueSafe(out question.Difficulty);
        reader.ReadValueSafe(out question.Category);
        reader.ReadValueSafe(out question.Question);
        for (var i = 0; i < 4; i++)
            reader.ReadValueSafe(out question.Choices[i]);
        reader.ReadValueSafe(out question.AnswerIndex);
        reader.ReadValueSafe(out float timeLimit);

        foreach (var terminal in m_Terminals)
            if (terminal != null)
                terminal.ClearResults();
        m_Board.Show(question);

        // 같은 LAN이라 지연은 무시하고 받은 순간을 시작 시각으로 본다
        m_QuizStartTime = Time.timeAsDouble;
        m_TimeLimit = timeLimit;
        m_IsTimerRunning = true;
        Debug.Log($"[LanQuizSync] Host question {question.Id}: {question.Question} ({timeLimit}s)", this);
    }

    void ReceiveQuizEnded(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId == NetworkManager.ServerClientId)
            m_IsTimerRunning = false;
    }

    void ReceiveButtonResult(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
            return;

        reader.ReadValueSafe(out int seat);
        reader.ReadValueSafe(out int choiceIndex);
        reader.ReadValueSafe(out bool correct);
        if (seat >= 1 && seat <= m_Terminals.Length && m_Terminals[seat - 1] != null)
            m_Terminals[seat - 1].ShowResult(choiceIndex, correct);
    }
}
