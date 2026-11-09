using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>버튼에 표시할 판정 결과.</summary>
public enum QuizButtonResult
{
    None,
    Correct,
    Wrong,
}

/// <summary>
/// 단말기의 정답 버튼. 손(레이 포함)으로 가리키고 트리거를 누르면 선택지(A~D)를 알린다.
/// 누르면 바로 들어갔다 나온다(판정을 기다리지 않는 즉시 반응). 판정 결과는 색으로 표시한다.
/// 퀴즈 시스템은 <see cref="Pressed"/>를 구독해서 판정한다.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class QuizButton : TriggerInteraction
{
    static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

    [SerializeField, Tooltip("이 버튼의 선택지")] string m_Choice = "A";
    [SerializeField, Tooltip("{0} 자리에 선택지가 들어간다")] string m_PromptFormat = "Press Trigger to choose {0}";

    [Header("Feedback")]
    [SerializeField, Tooltip("눌렀을 때 들어가는 깊이(m)")] float m_PressDepth = 0.008f;
    [SerializeField, Tooltip("들어갔다 나오는 데 걸리는 시간(초)")] float m_PressDuration = 0.15f;
    [SerializeField] Color m_CorrectColor = new Color(0.1f, 0.75f, 0.2f);
    [SerializeField] Color m_WrongColor = new Color(0.8f, 0.1f, 0.08f);

    string m_Prompt;
    Vector3 m_RestLocalPosition;
    Renderer[] m_Renderers;
    MaterialPropertyBlock m_Block;
    Coroutine m_PressRoutine;

    public string Choice => m_Choice;

    /// <summary>선택지 순서 (A = 0 ~ D = 3). 잘못 설정되어 있으면 -1.</summary>
    public int ChoiceIndex => string.IsNullOrEmpty(m_Choice) ? -1 : "ABCD".IndexOf(char.ToUpperInvariant(m_Choice[0]));

    public event Action<QuizButton> Pressed;

    protected override void Awake()
    {
        base.Awake();
        m_Prompt = string.Format(m_PromptFormat, m_Choice);
        m_RestLocalPosition = transform.localPosition;
        m_Renderers = GetComponentsInChildren<Renderer>();
        m_Block = new MaterialPropertyBlock();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        m_PressRoutine = null;
        transform.localPosition = m_RestLocalPosition;
    }

    protected override bool CanUse(XRBaseInputInteractor interactor, out string prompt)
    {
        prompt = m_Prompt;
        return true;
    }

    protected override void OnTriggerPressed(XRBaseInputInteractor interactor)
    {
        Debug.Log($"[QuizButton] {m_Choice}", this);
        PlayPress();
        Pressed?.Invoke(this);
    }

    /// <summary>들어갔다 나오는 움직임.</summary>
    public void PlayPress()
    {
        if (!isActiveAndEnabled)
            return;
        if (m_PressRoutine != null)
            StopCoroutine(m_PressRoutine);
        m_PressRoutine = StartCoroutine(PressRoutine());
    }

    /// <summary>판정 결과 색. None이면 원래 색으로 돌아간다.</summary>
    public void SetResult(QuizButtonResult result)
    {
        foreach (var r in m_Renderers)
        {
            if (result == QuizButtonResult.None)
            {
                r.SetPropertyBlock(null);
                continue;
            }

            m_Block.Clear();
            m_Block.SetColor(k_BaseColor, result == QuizButtonResult.Correct ? m_CorrectColor : m_WrongColor);
            r.SetPropertyBlock(m_Block);
        }
    }

    IEnumerator PressRoutine()
    {
        // 앞의 1/3 동안 들어가고, 나머지 동안 올라온다
        var down = m_RestLocalPosition - transform.parent.InverseTransformVector(transform.up * m_PressDepth);
        var downTime = m_PressDuration / 3f;
        for (var t = 0f; t < m_PressDuration; t += Time.deltaTime)
        {
            var k = t < downTime ? t / downTime : 1f - (t - downTime) / (m_PressDuration - downTime);
            transform.localPosition = Vector3.Lerp(m_RestLocalPosition, down, k);
            yield return null;
        }

        transform.localPosition = m_RestLocalPosition;
        m_PressRoutine = null;
    }
}
