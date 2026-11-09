using TMPro;
using UnityEngine;

/// <summary>
/// 퀴즈 화면 한 장. 문제와 선택지 A~D를 표시한다.
/// 화면마다 따로 부를 수 있어서, 나중에 한 사람 화면에서만 오답을 지우는 것도 여기서 한다.
/// </summary>
public class QuizScreen : MonoBehaviour
{
    static readonly string[] k_Labels = { "A", "B", "C", "D" };

    [SerializeField] TMP_Text m_QuestionText;
    [SerializeField, Tooltip("A B C D 순서")] TMP_Text[] m_ChoiceTexts = new TMP_Text[4];

    public void Show(QuizQuestion question)
    {
        m_QuestionText.text = question.Question;
        for (var i = 0; i < m_ChoiceTexts.Length; i++)
        {
            var choice = question.Choices != null && i < question.Choices.Length ? question.Choices[i] : null;
            m_ChoiceTexts[i].text = string.IsNullOrEmpty(choice) ? string.Empty : $"{k_Labels[i]}. {choice}";
        }
    }

    public void Clear()
    {
        m_QuestionText.text = string.Empty;
        foreach (var choiceText in m_ChoiceTexts)
            choiceText.text = string.Empty;
    }
}
