using System;
using UnityEngine;

/// <summary>
/// 기폭장치의 전선 한 가닥. 진짜(조끼와 연결) 여부는 게임 로직이 보관하고, 이 컴포넌트는 표시/절단 상태만 가진다.
/// </summary>
public class DetonatorWire : MonoBehaviour, IInteractable
{
    [SerializeField] private int index;
    [SerializeField] private string colorName;
    [SerializeField] private Renderer wireRenderer;

    public int Index => index;
    public string ColorName => colorName;
    public bool IsCut { get; private set; }
    /// <summary>자기 차례가 아닐 때 등 게임 로직이 절단을 막을 때 true</summary>
    public bool Locked { get; set; }

    public string Prompt => $"Cut wire {index + 1} ({colorName})";
    public bool CanInteract => !IsCut && !Locked;

    public event Action<DetonatorWire> CutPerformed;

    public void Interact() => Cut();

    public void Cut()
    {
        if (IsCut || Locked) return;
        IsCut = true;
        if (wireRenderer != null) wireRenderer.enabled = false;
        // 절단된 전선은 더 이상 조준되지 않도록
        Collider c = GetComponent<Collider>();
        if (c != null) c.enabled = false;
        Debug.Log($"[DetonatorWire] {name} 절단");
        CutPerformed?.Invoke(this);
    }
}
