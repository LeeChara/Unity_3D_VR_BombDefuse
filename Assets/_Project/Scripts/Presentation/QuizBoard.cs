using UnityEngine;

/// <summary>
/// 두 플레이어의 퀴즈 화면을 함께 다룬다. 같은 문제를 모든 화면에 동시에 띄운다.
/// </summary>
public class QuizBoard : MonoBehaviour
{
    [SerializeField] QuizScreen[] m_Screens;

    [Header("Test")]
    [SerializeField, Tooltip("켜 두면 시작할 때 테스트 문제를 띄운다")] bool m_ShowTestOnStart = true;
    [SerializeField] QuizQuestion m_TestQuestion = new QuizQuestion
    {
        Question = "TEST: How many bones are in the adult human body?",
        Choices = new[] { "106", "206", "306", "406" },
    };

    void Start()
    {
        if (m_ShowTestOnStart)
            Show(m_TestQuestion);
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

    [ContextMenu("Show Test Question")]
    void ShowTestQuestion() => Show(m_TestQuestion);
}
