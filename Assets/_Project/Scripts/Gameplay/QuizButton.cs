using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
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
/// 단말기의 정답 버튼. 손(레이 포함)으로 가리키고 트리거를 누르거나, 손가락 끝(컨트롤러의 Poke Point)으로 위에서 직접 누르면 선택지(A~D)를 알린다.
/// 직접 누르기는 손가락이 버튼 위에서 내려와 윗면을 지나 들어갔을 때만 인정한다 (옆에서 스치면 눌리지 않는다).
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

    [Header("Direct Press")]
    [SerializeField, Tooltip("손가락 끝으로 직접 눌러도 된다")] bool m_AllowPoke = true;
    [SerializeField, Tooltip("손가락이 버튼 윗면보다 이만큼 들어가면 눌린 것(m)")] float m_PokePressDepth = 0.005f;
    [SerializeField, Tooltip("버튼 가장자리 밖으로 이만큼까지 손가락을 인정(m)")] float m_PokeMargin = 0.008f;
    [SerializeField, Tooltip("다시 누르려면 손가락이 윗면보다 이만큼 올라와야 한다(m)")] float m_PokeRearmHeight = 0.01f;
    [SerializeField, Range(0f, 1f)] float m_PokeHaptic = 0.5f;

    static XRPokeInteractor[] s_Pokes;
    static float s_NextPokeRefresh;

    string m_Prompt;
    Vector3 m_RestLocalPosition;
    Renderer[] m_Renderers;
    MaterialPropertyBlock m_Block;
    Coroutine m_PressRoutine;
    // 버튼 위에서 내려오는 중인 손가락들 (Poke Interactor의 인스턴스 ID)
    readonly HashSet<int> m_ArmedPokes = new HashSet<int>();
    float m_PokeRadius;
    float m_PokeTop;

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

        // 버튼 모양(메시)의 크기로 직접 누르는 범위를 잡는다. 반지름은 가로, 윗면은 중심에서 위로
        if (TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null)
        {
            var extents = meshFilter.sharedMesh.bounds.extents;
            var scale = transform.lossyScale;
            m_PokeRadius = Mathf.Max(extents.x * scale.x, extents.z * scale.z);
            m_PokeTop = extents.y * scale.y;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_Pokes = null;
        s_NextPokeRefresh = 0f;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        m_PressRoutine = null;
        transform.localPosition = m_RestLocalPosition;
        m_ArmedPokes.Clear();
    }

    protected override bool CanUse(XRBaseInputInteractor interactor, out string prompt)
    {
        prompt = m_Prompt;
        return true;
    }

    protected override void OnTriggerPressed(XRBaseInputInteractor interactor) => Press("트리거");

    protected override void Update()
    {
        base.Update();
        if (m_AllowPoke && m_PokeRadius > 0f)
            UpdatePoke();
    }

    void Press(string how)
    {
        Debug.Log($"[QuizButton] {m_Choice} ({how})", this);
        PlayPress();
        Pressed?.Invoke(this);
    }

    // 손가락 끝이 버튼 위에서 내려와 윗면 아래로 들어가면 누른다. 들어가는 움직임 중에도 원래 자리 기준으로 잰다
    void UpdatePoke()
    {
        if (s_Pokes == null || Time.unscaledTime >= s_NextPokeRefresh)
        {
            s_Pokes = FindObjectsByType<XRPokeInteractor>(FindObjectsSortMode.None);
            s_NextPokeRefresh = Time.unscaledTime + 2f;
        }

        var up = transform.up;
        var rest = transform.parent != null ? transform.parent.TransformPoint(m_RestLocalPosition) : m_RestLocalPosition;
        foreach (var poke in s_Pokes)
        {
            if (poke == null || !poke.isActiveAndEnabled)
                continue;

            var tip = poke.attachTransform != null ? poke.attachTransform.position : poke.transform.position;
            var offset = tip - rest;
            var height = Vector3.Dot(offset, up);
            var inside = (offset - up * height).magnitude <= m_PokeRadius + m_PokeMargin;
            var id = poke.GetInstanceID();

            if (!inside)
            {
                m_ArmedPokes.Remove(id);
                continue;
            }

            if (height > m_PokeTop + m_PokeRearmHeight)
            {
                m_ArmedPokes.Add(id);
                continue;
            }

            if (height < m_PokeTop - m_PokePressDepth && m_ArmedPokes.Remove(id))
            {
                if (m_PokeHaptic > 0f)
                    poke.GetComponentInParent<HapticImpulsePlayer>()?.SendHapticImpulse(m_PokeHaptic, 0.05f);
                Press("직접");
                return;
            }
        }
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
