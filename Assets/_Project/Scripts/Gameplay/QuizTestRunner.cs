using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 임시 퀴즈 진행. 게임 진행 컨트롤러가 생기기 전까지 출제 → 판정 → 잠시 뒤 다음 문제를 반복한다.
/// 공포 단계는 아직 없어서 제한시간은 단계 0 값을 쓰고, 난이도 구분 없이 고른다.
/// 게임 진행 컨트롤러를 만들면 이 스크립트는 지운다.
/// </summary>
public class QuizTestRunner : MonoBehaviour
{
    [SerializeField, Tooltip("문제 CSV (Assets/_Project/Data/Quiz/questions.csv)")] TextAsset m_QuestionsCsv;
    [SerializeField] TuningConfig m_Config;
    [SerializeField] QuizBoard m_Board;
    [SerializeField, Tooltip("좌석 1, 2의 단말기")] QuizTerminal[] m_Terminals;
    [SerializeField, Min(0f), Tooltip("문제가 끝나고 다음 문제까지(초)")] float m_NextQuestionDelay = 2f;

    QuizDeck m_Deck;
    QuizRound m_Round;

    static double Now => Time.timeAsDouble;

    void Start()
    {
        if (m_QuestionsCsv == null || m_Config == null || m_Board == null)
        {
            Debug.LogWarning("[Quiz] CSV, TuningConfig, QuizBoard 중 연결되지 않은 것이 있다", this);
            enabled = false;
            return;
        }

        var warnings = new List<string>();
        var questions = QuizCsvParser.Parse(m_QuestionsCsv.text, warnings);
        foreach (var warning in warnings)
            Debug.LogWarning($"[Quiz] {m_QuestionsCsv.name}.csv: {warning}", m_QuestionsCsv);
        Debug.Log($"[Quiz] {m_QuestionsCsv.name}.csv에서 문제 {questions.Count}개를 불러왔다", this);

        m_Deck = new QuizDeck(questions);
        if (m_Deck.Count == 0)
        {
            enabled = false;
            return;
        }

        StartRound();
    }

    void OnEnable()
    {
        foreach (var terminal in m_Terminals)
            if (terminal != null)
                terminal.AnswerPressed += OnAnswerPressed;
    }

    void OnDisable()
    {
        foreach (var terminal in m_Terminals)
            if (terminal != null)
                terminal.AnswerPressed -= OnAnswerPressed;
    }

    void Update()
    {
        if (m_Round == null || m_Round.IsResolved)
            return;

        m_Round.Tick(Now);
        if (!m_Round.IsResolved)
            m_Board.SetRemainingTime(m_Round.GetRemainingRatio(Now));
    }

    void StartRound()
    {
        var question = m_Deck.Draw(out var reshuffled);
        if (reshuffled)
            Debug.Log("[Quiz] 문제를 다 써서 처음부터 다시 낸다", this);

        var timeLimit = m_Config.GetQuizTimeLimit(0);
        m_Round = new QuizRound(question, Now, timeLimit);
        m_Round.AnswerWrong += OnAnswerWrong;
        m_Round.Resolved += OnResolved;

        m_Board.Show(question);
        Debug.Log($"[Quiz] {question.Id} 출제: {question.Question} (정답 {Letter(question.AnswerIndex)}, {timeLimit}초)", this);
    }

    void OnAnswerPressed(int seat, int choiceIndex)
    {
        if (m_Round == null)
            return;

        if (m_Round.Submit(seat, choiceIndex, Now) == QuizSubmitResult.Ignored)
            Debug.Log($"[Quiz] 좌석 {seat}의 {Letter(choiceIndex)} 무시 (잠겼거나 이미 끝남)", this);
    }

    void OnAnswerWrong(int seat, int choiceIndex) =>
        Debug.Log($"[Quiz] 좌석 {seat} 오답 {Letter(choiceIndex)} → 이 문제에서 잠김", this);

    void OnResolved(QuizResult result)
    {
        switch (result.Outcome)
        {
            case QuizOutcome.Correct:
                Debug.Log($"[Quiz] 좌석 {result.WinnerSeat} 정답 → 총을 얻는다", this);
                break;
            case QuizOutcome.Timeout:
                Debug.Log("[Quiz] 시간 초과 → 정답자 없음, 다음 문제", this);
                break;
            case QuizOutcome.AllWrong:
                Debug.Log("[Quiz] 둘 다 오답 → 정답자 없음, 다음 문제", this);
                break;
        }

        StartCoroutine(NextRoundAfterDelay());
    }

    IEnumerator NextRoundAfterDelay()
    {
        yield return new WaitForSeconds(m_NextQuestionDelay);
        StartRound();
    }

    static char Letter(int choiceIndex) => choiceIndex >= 0 && choiceIndex < 4 ? "ABCD"[choiceIndex] : '?';
}
