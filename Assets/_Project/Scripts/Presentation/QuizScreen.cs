using TMPro;
using UnityEngine;

/// <summary>
/// 퀴즈 화면 한 장. 문제와 선택지 A~D, 남은 시간 줄(위·아래)을 표시한다.
/// 화면마다 따로 부를 수 있어서, 나중에 한 사람 화면에서만 오답을 지우는 것도 여기서 한다.
/// </summary>
public class QuizScreen : MonoBehaviour
{
    static readonly string[] k_Labels = { "A", "B", "C", "D" };

    [SerializeField] TMP_Text m_QuestionText;
    [SerializeField, Tooltip("A B C D 순서")] TMP_Text[] m_ChoiceTexts = new TMP_Text[4];
    [SerializeField, Tooltip("남은 시간만큼 가로 길이가 줄어드는 줄. 피벗이 가운데라 양쪽 끝에서 가운데로 줄어든다")]
    RectTransform[] m_TimerBars;

    public void Show(QuizQuestion question)
    {
        m_QuestionText.text = question.Question;
        for (var i = 0; i < m_ChoiceTexts.Length; i++)
        {
            var choice = question.Choices != null && i < question.Choices.Length ? question.Choices[i] : null;
            m_ChoiceTexts[i].text = string.IsNullOrEmpty(choice) ? string.Empty : $"{k_Labels[i]}. {choice}";
        }

        SetRemainingTime(1f);
    }

    /// <summary>남은 시간 비율 (1 = 가득, 0 = 없음).</summary>
    public void SetRemainingTime(float ratio)
    {
        if (m_TimerBars == null)
            return;

        ratio = Mathf.Clamp01(ratio);
        foreach (var bar in m_TimerBars)
        {
            if (bar == null)
                continue;
            var scale = bar.localScale;
            scale.x = ratio;
            bar.localScale = scale;
        }
    }

    public void Clear()
    {
        m_QuestionText.text = string.Empty;
        foreach (var choiceText in m_ChoiceTexts)
            choiceText.text = string.Empty;
        SetRemainingTime(0f);
    }
}
