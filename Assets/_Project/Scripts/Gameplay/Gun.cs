using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 그립을 쥐고 있는 동안 손에 들리고, 놓으면 떨어진다. 쥔 채로 트리거를 당기면 발사한다.
/// 발사는 총구에서 정면으로 쏘는 히트스캔이다. 반동·사운드·피해는 <see cref="Fired"/>를 구독해서 붙인다.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class Gun : MonoBehaviour
{
    /// <summary>한 발의 히트스캔 결과.</summary>
    public struct ShotResult
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public bool DidHit;
        public RaycastHit Hit;
        /// <summary>맞은 지점. 안 맞았으면 사거리 끝.</summary>
        public Vector3 EndPoint;
    }

    [SerializeField, Tooltip("총구. Z축(파란 화살표)이 발사 방향")] Transform m_Muzzle;

    [Header("Hitscan")]
    [SerializeField] float m_Range = 50f;
    [SerializeField] LayerMask m_HitMask = ~0;
    [SerializeField, Tooltip("다음 발사까지 최소 간격(초)")] float m_FireCooldown = 0.25f;

    [Header("Prompt")]
    [SerializeField] string m_PickUpPrompt = "Press Grip to pick up";

    [Header("Debug")]
    [SerializeField, Tooltip("발사 궤적을 잠깐 보여 줄 LineRenderer. 비워 두면 표시하지 않는다")] LineRenderer m_DebugTracer;
    [SerializeField] float m_DebugTracerDuration = 0.08f;

    public event Action<ShotResult> Fired;

    XRGrabInteractable m_Grab;
    IXRSelectInteractor m_Holder;
    float m_NextFireTime;
    float m_TracerHideTime;
    readonly List<XRBaseInputInteractor> m_HoveringInteractors = new List<XRBaseInputInteractor>();
    readonly RaycastHit[] m_Hits = new RaycastHit[16];

    public bool IsEquipped => m_Holder != null;
    public Transform Muzzle => m_Muzzle;

    void Awake()
    {
        m_Grab = GetComponent<XRGrabInteractable>();

        if (m_DebugTracer != null)
            m_DebugTracer.enabled = false;
    }

    void OnEnable()
    {
        m_Grab.hoverEntered.AddListener(OnHoverEntered);
        m_Grab.hoverExited.AddListener(OnHoverExited);
        m_Grab.selectEntered.AddListener(OnSelectEntered);
        m_Grab.selectExited.AddListener(OnSelectExited);
        m_Grab.activated.AddListener(OnActivated);
    }

    void OnDisable()
    {
        m_Grab.hoverEntered.RemoveListener(OnHoverEntered);
        m_Grab.hoverExited.RemoveListener(OnHoverExited);
        m_Grab.selectEntered.RemoveListener(OnSelectEntered);
        m_Grab.selectExited.RemoveListener(OnSelectExited);
        m_Grab.activated.RemoveListener(OnActivated);
        m_HoveringInteractors.Clear();
        InteractionPrompt.Hide(this);
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
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

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        m_Holder = args.interactorObject;
        InteractionPrompt.Hide(this);
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (ReferenceEquals(args.interactorObject, m_Holder))
            m_Holder = null;
    }

    // 총을 쥔 손의 트리거
    void OnActivated(ActivateEventArgs args) => TryFire();

    void Update()
    {
        UpdatePrompt();

        if (m_DebugTracer != null && m_DebugTracer.enabled && Time.time >= m_TracerHideTime)
            m_DebugTracer.enabled = false;
    }

    void UpdatePrompt()
    {
        if (IsEquipped || m_HoveringInteractors.Count == 0)
            return;

        // 다른 것을 쥐고 있지 않은 손이 가리키면 안내 문구를 띄운다
        var canPickUp = false;
        foreach (var interactor in m_HoveringInteractors)
        {
            if (!interactor.hasSelection)
                canPickUp = true;
        }

        if (canPickUp)
            InteractionPrompt.Show(this, m_PickUpPrompt);
        else
            InteractionPrompt.Hide(this);
    }

    /// <summary>쥐고 있는 손에서 떨어뜨린다.</summary>
    public void Drop()
    {
        if (IsEquipped)
            m_Grab.interactionManager.SelectExit(m_Holder, m_Grab);
    }

    /// <summary>쥐고 있고 쿨다운이 지났으면 발사한다.</summary>
    public bool TryFire()
    {
        if (!IsEquipped || Time.time < m_NextFireTime)
            return false;

        m_NextFireTime = Time.time + m_FireCooldown;
        var result = Hitscan();

        if (result.DidHit)
            Debug.Log($"[Gun] Hit {result.Hit.collider.name} ({result.Hit.distance:F2}m)", result.Hit.collider);
        else
            Debug.Log("[Gun] Miss");

        ShowTracer(result);
        Fired?.Invoke(result);
        return true;
    }

    ShotResult Hitscan()
    {
        var origin = m_Muzzle.position;
        var direction = m_Muzzle.forward;
        var result = new ShotResult { Origin = origin, Direction = direction, EndPoint = origin + direction * m_Range };

        // 총 자신과 쏜 사람의 리그는 맞지 않게 건너뛰고, 그중 가장 가까운 것을 고른다
        var shooterRig = m_Holder != null ? m_Holder.transform.GetComponentInParent<XROrigin>() : null;
        var count = Physics.RaycastNonAlloc(origin, direction, m_Hits, m_Range, m_HitMask, QueryTriggerInteraction.Ignore);
        var closest = float.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var hit = m_Hits[i];
            var hitTransform = hit.collider.transform;
            if (hitTransform.IsChildOf(transform) || (shooterRig != null && hitTransform.IsChildOf(shooterRig.transform)))
                continue;

            if (hit.distance < closest)
            {
                closest = hit.distance;
                result.DidHit = true;
                result.Hit = hit;
                result.EndPoint = hit.point;
            }
        }

        return result;
    }

    void ShowTracer(ShotResult result)
    {
        if (m_DebugTracer == null)
            return;

        m_DebugTracer.positionCount = 2;
        m_DebugTracer.SetPosition(0, result.Origin);
        m_DebugTracer.SetPosition(1, result.EndPoint);
        m_DebugTracer.enabled = true;
        m_TracerHideTime = Time.time + m_DebugTracerDuration;
    }
}
