using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 단말기의 정답 버튼. 손(레이 포함)으로 가리키고 트리거를 누르면 선택지(A~D)를 알린다.
/// 퀴즈 시스템은 <see cref="Pressed"/>를 구독해서 판정한다.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class QuizButton : TriggerInteraction
{
    [SerializeField, Tooltip("이 버튼의 선택지")] string m_Choice = "A";
    [SerializeField, Tooltip("{0} 자리에 선택지가 들어간다")] string m_PromptFormat = "Press Trigger to choose {0}";

    string m_Prompt;

    public string Choice => m_Choice;

    public event Action<QuizButton> Pressed;

    protected override void Awake()
    {
        base.Awake();
        m_Prompt = string.Format(m_PromptFormat, m_Choice);
    }

    protected override bool CanUse(XRBaseInputInteractor interactor, out string prompt)
    {
        prompt = m_Prompt;
        return true;
    }

    protected override void OnTriggerPressed(XRBaseInputInteractor interactor)
    {
        Debug.Log($"[QuizButton] {m_Choice}", this);
        Pressed?.Invoke(this);
    }
}
