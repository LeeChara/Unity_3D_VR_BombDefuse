using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 두 플레이어의 퀴즈 화면을 함께 다룬다. 같은 문제를 모든 화면에 동시에 띄운다.
/// </summary>
public class QuizBoard : MonoBehaviour
{
    [SerializeField] QuizScreen[] m_Screens;

    [Header("Test")]
    [SerializeField, Tooltip("문제 CSV (Assets/_Project/Data/Quiz/questions.csv). 퀴즈 진행 로직이 생기면 그쪽으로 옮긴다")]
    TextAsset m_QuestionsCsv;
    [SerializeField, Tooltip("켜 두면 시작할 때 CSV에서 무작위 문제를 띄운다")] bool m_ShowRandomOnStart = true;

    List<QuizQuestion> m_Questions = new List<QuizQuestion>();
    int m_LastIndex = -1;

    void Start()
    {
        LoadQuestions();

        if (m_ShowRandomOnStart)
            ShowRandomQuestion();
        else
            Clear();
    }

    public void Show(QuizQuestion question)
    {
        foreach (var screen in m_Screens)
            screen.Show(question);
    }

    public void Clear()
    {
        foreach (var screen in m_Screens)
            screen.Clear();
    }

    void LoadQuestions()
    {
        if (m_QuestionsCsv == null)
        {
            Debug.LogWarning("[QuizBoard] 문제 CSV가 연결되지 않았다", this);
            return;
        }

        var warnings = new List<string>();
        m_Questions = QuizCsvParser.Parse(m_QuestionsCsv.text, warnings);
        foreach (var warning in warnings)
            Debug.LogWarning($"[QuizBoard] {m_QuestionsCsv.name}.csv: {warning}", m_QuestionsCsv);
        Debug.Log($"[QuizBoard] {m_QuestionsCsv.name}.csv에서 문제 {m_Questions.Count}개를 불러왔다", this);
    }

    // 직전 문제와 다른 문제를 무작위로 띄운다
    [ContextMenu("Show Random Question")]
    void ShowRandomQuestion()
    {
        if (m_Questions.Count == 0)
            LoadQuestions();
        if (m_Questions.Count == 0)
            return;

        var index = Random.Range(0, m_Questions.Count);
        if (m_Questions.Count > 1 && index == m_LastIndex)
            index = (index + 1) % m_Questions.Count;
        m_LastIndex = index;

        var question = m_Questions[index];
        Show(question);
        Debug.Log($"[QuizBoard] {question.Id} 표시 (정답 {"ABCD"[question.AnswerIndex]})", this);
    }
}
