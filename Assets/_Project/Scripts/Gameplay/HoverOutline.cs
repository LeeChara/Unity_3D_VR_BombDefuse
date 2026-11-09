using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 손(레이 포함)이 가리키는 동안 외곽선을 그린다. 잡고 있는 동안에는 그리지 않는다.
/// 자식 렌더러까지 전부 외곽선 머티리얼을 덧붙인다.
/// </summary>
[RequireComponent(typeof(XRBaseInteractable))]
public class HoverOutline : MonoBehaviour
{
    [SerializeField, Tooltip("BombDefuse/Outline 셰이더를 쓰는 머티리얼")] Material m_OutlineMaterial;

    XRBaseInteractable m_Interactable;
    MeshRenderer[] m_Renderers;
    bool m_IsShown;

    void Awake()
    {
        m_Interactable = GetComponent<XRBaseInteractable>();
        m_Renderers = GetComponentsInChildren<MeshRenderer>(true);
    }

    void OnEnable()
    {
        m_Interactable.hoverEntered.AddListener(Refresh);
        m_Interactable.hoverExited.AddListener(Refresh);
        m_Interactable.selectEntered.AddListener(Refresh);
        m_Interactable.selectExited.AddListener(Refresh);
    }

    void OnDisable()
    {
        m_Interactable.hoverEntered.RemoveListener(Refresh);
        m_Interactable.hoverExited.RemoveListener(Refresh);
        m_Interactable.selectEntered.RemoveListener(Refresh);
        m_Interactable.selectExited.RemoveListener(Refresh);
        SetShown(false);
    }

    void Refresh(BaseInteractionEventArgs args) => SetShown(m_Interactable.isHovered && !m_Interactable.isSelected);

    void SetShown(bool shown)
    {
        if (shown == m_IsShown || m_OutlineMaterial == null)
            return;

        m_IsShown = shown;
        foreach (var meshRenderer in m_Renderers)
        {
            if (meshRenderer == null)
                continue;

            // 서브메시보다 머티리얼이 하나 더 많으면, 마지막 서브메시를 그 머티리얼로 한 번 더 그린다
            var materials = meshRenderer.sharedMaterials;
            if (shown)
            {
                System.Array.Resize(ref materials, materials.Length + 1);
                materials[materials.Length - 1] = m_OutlineMaterial;
            }
            else
            {
                materials = System.Array.FindAll(materials, m => m != m_OutlineMaterial);
            }
            meshRenderer.sharedMaterials = materials;
        }
    }
}
