using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 손(레이 포함)으로 가리키고 트리거를 누르면 반응하는 물체의 공통 부분.
/// 가리키는 손을 추적하고, 트리거 입력과 안내 문구를 처리한다. 무엇을 할지는 상속한 클래스가 정한다.
/// </summary>
[RequireComponent(typeof(XRBaseInteractable))]
public abstract class TriggerInteraction : MonoBehaviour
{
    XRBaseInteractable m_Interactable;
    readonly List<XRBaseInputInteractor> m_HoveringInteractors = new List<XRBaseInputInteractor>();

    /// <summary>이 손이 지금 쓸 수 있으면 true와 함께 띄울 안내 문구를 돌려준다.</summary>
    protected abstract bool CanUse(XRBaseInputInteractor interactor, out string prompt);

    /// <summary>쓸 수 있는 손이 트리거를 눌렀을 때.</summary>
    protected abstract void OnTriggerPressed(XRBaseInputInteractor interactor);

    protected virtual void Awake() => m_Interactable = GetComponent<XRBaseInteractable>();

    protected virtual void OnEnable()
    {
        m_Interactable.hoverEntered.AddListener(OnHoverEntered);
        m_Interactable.hoverExited.AddListener(OnHoverExited);
    }

    protected virtual void OnDisable()
    {
        m_Interactable.hoverEntered.RemoveListener(OnHoverEntered);
        m_Interactable.hoverExited.RemoveListener(OnHoverExited);
        m_HoveringInteractors.Clear();
        InteractionPrompt.Hide(this);
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        // 트리거 입력을 읽을 수 있는 손(Near-Far Interactor)만 다룬다
        if (args.interactorObject is XRBaseInputInteractor interactor)
            m_HoveringInteractors.Add(interactor);
    }

    void OnHoverExited(HoverExitEventArgs args)
    {
        if (args.interactorObject is XRBaseInputInteractor interactor)
            m_HoveringInteractors.Remove(interactor);

        if (m_HoveringInteractors.Count == 0)
            InteractionPrompt.Hide(this);
    }

    protected virtual void Update()
    {
        if (m_HoveringInteractors.Count == 0)
            return;

        string prompt = null;
        for (var i = 0; i < m_HoveringInteractors.Count; i++)
        {
            var interactor = m_HoveringInteractors[i];
            // 총처럼 무언가를 쥐고 있는 손의 트리거는 그 물건이 쓴다
            if (interactor.hasSelection)
                continue;

            if (!CanUse(interactor, out var interactorPrompt))
                continue;

            if (interactor.activateInput.ReadWasPerformedThisFrame())
            {
                // 반응 중에 손 Interactor가 꺼졌다 켜지면서 목록이 바뀔 수 있어 바로 빠져나간다
                OnTriggerPressed(interactor);
                return;
            }

            prompt = interactorPrompt;
        }

        if (prompt != null)
            InteractionPrompt.Show(this, prompt);
        else
            InteractionPrompt.Hide(this);
    }
}
