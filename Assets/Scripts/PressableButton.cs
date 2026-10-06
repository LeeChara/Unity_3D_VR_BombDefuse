using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 눌리는 물리 버튼(키패드 키, 정답 버튼, 기폭 버튼). 눌림 연출 후 Pressed 이벤트를 보낸다.
/// </summary>
public class PressableButton : MonoBehaviour, IInteractable
{
    [SerializeField] private string label;
    [SerializeField] private float travel = 0.006f;
    [SerializeField] private float pressTime = 0.08f;

    public string Label => label;
    public string Prompt => $"[{label}]";
    public bool CanInteract => isActiveAndEnabled;

    public event Action<PressableButton> Pressed;

    private Vector3 restPosition;
    private Coroutine animating;

    private void Awake() => restPosition = transform.localPosition;

    public void Interact()
    {
        if (animating != null) StopCoroutine(animating);
        animating = StartCoroutine(PressAnimation());
        Pressed?.Invoke(this);
    }

    private IEnumerator PressAnimation()
    {
        transform.localPosition = restPosition - transform.parent.InverseTransformDirection(transform.up) * travel;
        yield return new WaitForSeconds(pressTime);
        transform.localPosition = restPosition;
        animating = null;
    }
}
