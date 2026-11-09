using System;
using UnityEngine;

/// <summary>
/// 한 플레이어의 단말기. 자식 버튼 A~D가 눌리면 (좌석 번호, 선택지)로 알린다.
/// </summary>
public class QuizTerminal : MonoBehaviour
{
    [SerializeField, Range(1, 2), Tooltip("이 단말기를 쓰는 좌석 번호")] int m_Seat = 1;

    QuizButton[] m_Buttons;

    public int Seat => m_Seat;

    /// <summary>버튼이 눌렸을 때 (좌석 번호, 선택지 0 = A ~ 3 = D).</summary>
    public event Action<int, int> AnswerPressed;

    void Awake()
    {
        m_Buttons = GetComponentsInChildren<QuizButton>(true);
        foreach (var button in m_Buttons)
        {
            if (button.ChoiceIndex < 0)
                Debug.LogWarning($"[QuizTerminal] {button.name}의 선택지 '{button.Choice}'가 A~D가 아니다", button);
        }
    }

    void OnEnable()
    {
        foreach (var button in m_Buttons)
            button.Pressed += OnButtonPressed;
    }

    void OnDisable()
    {
        foreach (var button in m_Buttons)
            button.Pressed -= OnButtonPressed;
    }

    void OnButtonPressed(QuizButton button)
    {
        if (button.ChoiceIndex >= 0)
            AnswerPressed?.Invoke(m_Seat, button.ChoiceIndex);
    }
}
