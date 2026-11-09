using System;
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 그립을 쥐고 있는 동안 손에 들리고, 놓으면 떨어진다. 쥔 채로 트리거를 당기면
/// 공이치기 연출 시간(0.6초) 뒤에 약실(<see cref="Revolver"/>)을 한 칸 쏜다.
/// 실탄이면 총구에서 정면으로 히트스캔하고, 맞은 <see cref="Hitbox"/>에 피해를 준다. 공포탄이면 딸깍만 한다.
/// 반동·사운드·이펙트는 <see cref="HammerCocked"/>, <see cref="Fired"/>를 구독해서 붙인다 (<see cref="GunEffects"/>).
/// 게임 진행 컨트롤러가 <see cref="Present"/>로 지급 위치에 꺼내고 <see cref="Withdraw"/>로 치운다.
/// 꺼내 둔 동안 떨어뜨리면 잠시 뒤 지급 위치로 돌아온다.
/// 지금은 총이 약실을 직접 들고 있다. 멀티를 붙일 때 호스트 쪽으로 옮긴다.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class Gun : MonoBehaviour
{
    /// <summary>한 발의 히트스캔 결과.</summary>
    public struct ShotResult
    {
        public Vector3 Origin;
        public Vector3 Direction;
        /// <summary>실탄이면 true, 공포탄이면 false. 공포탄이면 히트스캔하지 않는다.</summary>
        public bool IsLive;
        public bool DidHit;
        public RaycastHit Hit;
        /// <summary>맞은 지점. 안 맞았으면 사거리 끝.</summary>
        public Vector3 EndPoint;
    }

    [SerializeField, Tooltip("총구. Z축(파란 화살표)이 발사 방향")] Transform m_Muzzle;
    [SerializeField, Tooltip("수치 (약실 수, 총알 수, 공이치기 연출 시간)")] TuningConfig m_Config;

    [Header("Hitscan")]
    [SerializeField] float m_Range = 50f;
    [SerializeField] LayerMask m_HitMask = ~0;
    [SerializeField, Tooltip("다음 발사까지 최소 간격(초)")] float m_FireCooldown = 0.25f;

    [Header("Prompt")]
    [SerializeField] string m_PickUpPrompt = "Press Grip to pick up";

    /// <summary>방아쇠를 당겨 공이치기가 젖혀질 때. 결과는 공이치기 연출 시간 뒤 <see cref="Fired"/>로 온다.</summary>
    public event Action HammerCocked;

    /// <summary>약실 한 칸을 쐈을 때 (실탄·공포탄 모두).</summary>
    public event Action<ShotResult> Fired;

    XRGrabInteractable m_Grab;
    IXRSelectInteractor m_Holder;
    float m_NextFireTime;
    bool m_IsCocking;
    Revolver m_Revolver;
    Rigidbody m_Rigidbody;
    Transform m_HomePoint;
    Coroutine m_ReturnRoutine;
    readonly List<XRBaseInputInteractor> m_HoveringInteractors = new List<XRBaseInputInteractor>();
    readonly RaycastHit[] m_Hits = new RaycastHit[16];

    public bool IsEquipped => m_Holder != null;
    public Transform Muzzle => m_Muzzle;

    /// <summary>true면 방아쇠를 당겨도 쏘지 않는다. 한 발 쏜 뒤 진행 컨트롤러가 잠근다.</summary>
    public bool IsTriggerLocked { get; set; }

    /// <summary>지금 꺼내져 있는지 (보이고 잡을 수 있는지).</summary>
    public bool IsPresented => gameObject.activeSelf;

    void Awake()
    {
        m_Grab = GetComponent<XRGrabInteractable>();
        m_Rigidbody = GetComponent<Rigidbody>();

        if (m_Config != null)
            m_Revolver = new Revolver(m_Config.ChamberCount, m_Config.StartingBullets);
        else
            Debug.LogWarning("[Gun] TuningConfig가 연결되지 않아 약실 없이 매번 실탄으로 쏜다", this);
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
        m_IsCocking = false;   // 꺼지면 공이치기·복귀 코루틴도 멈춘다
        m_ReturnRoutine = null;
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
        if (m_ReturnRoutine != null)
        {
            StopCoroutine(m_ReturnRoutine);
            m_ReturnRoutine = null;
        }
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (!ReferenceEquals(args.interactorObject, m_Holder))
            return;

        m_Holder = null;

        // 놓으면 떨어지고, 잠시 뒤 지급 위치로 돌아온다
        if (m_HomePoint != null && isActiveAndEnabled)
        {
            m_Rigidbody.isKinematic = false;
            m_ReturnRoutine = StartCoroutine(ReturnHomeAfterDelay());
        }
    }

    // 총을 쥔 손의 트리거
    void OnActivated(ActivateEventArgs args) => TryFire();

    void Update() => UpdatePrompt();

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

    /// <summary>지급 위치에 꺼낸다. 그 자리에 가만히 놓이고, 떨어뜨리면 이 위치로 돌아온다.</summary>
    public void Present(Transform homePoint)
    {
        m_HomePoint = homePoint;
        IsTriggerLocked = false;
        gameObject.SetActive(true);
        MoveHome();
    }

    /// <summary>손에서 놓고 치운다 (보이지 않고 잡을 수 없다).</summary>
    public void Withdraw()
    {
        Drop();
        m_HomePoint = null;
        gameObject.SetActive(false);
    }

    void MoveHome()
    {
        if (m_HomePoint == null)
            return;
        // 키네마틱으로 바꾸기 전에 멈춰야 경고가 나오지 않는다
        if (!m_Rigidbody.isKinematic)
        {
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
        }
        m_Rigidbody.isKinematic = true;
        transform.SetPositionAndRotation(m_HomePoint.position, m_HomePoint.rotation);
    }

    IEnumerator ReturnHomeAfterDelay()
    {
        yield return new WaitForSeconds(m_Config != null ? m_Config.GunReturnDelay : 2f);
        m_ReturnRoutine = null;
        if (!IsEquipped)
        {
            MoveHome();
            Debug.Log("[Gun] 떨어뜨린 총이 지급 위치로 돌아왔다", this);
        }
    }

    /// <summary>쥐고 있고 쿨다운이 지났으면 방아쇠를 당긴다. 결과는 공이치기 연출 시간 뒤에 나온다.</summary>
    public bool TryFire()
    {
        if (!IsEquipped || IsTriggerLocked || m_IsCocking || Time.time < m_NextFireTime)
            return false;

        StartCoroutine(FireAfterHammer());
        return true;
    }

    /// <summary>실린더를 무작위 위치로 돌린다. 사수 턴의 "실린더 돌리기"에서 부른다.</summary>
    [ContextMenu("Spin Cylinder")]
    public void SpinCylinder()
    {
        if (m_Revolver == null)
            return;
        m_Revolver.Spin();
        Debug.Log("[Gun] 실린더를 돌렸다", this);
    }

    IEnumerator FireAfterHammer()
    {
        // 방아쇠를 당기면 공이치기를 젖히는 시간이 먼저 지난다. 대상이 보고 피할 수 있는 시간이다
        m_IsCocking = true;
        HammerCocked?.Invoke();
        if (m_Config != null && m_Config.HammerDelay > 0f)
            yield return new WaitForSeconds(m_Config.HammerDelay);
        m_IsCocking = false;
        m_NextFireTime = Time.time + m_FireCooldown;

        var live = m_Revolver == null || m_Revolver.Pull();
        var result = live ? Hitscan() : new ShotResult { Origin = m_Muzzle.position, Direction = m_Muzzle.forward, EndPoint = m_Muzzle.position };
        result.IsLive = live;

        if (!live)
        {
            Debug.Log("[Gun] 딸깍 (공포탄)", this);
        }
        else
        {
            ApplyDamage(result);

            // 실탄을 쏘면 진행자가 다시 장전한다
            if (m_Revolver != null)
            {
                m_Revolver.ReloadAfterShot();
                Debug.Log($"[Gun] 다시 장전했다 (총알 {m_Revolver.BulletCount}발)", this);
            }
        }

        Fired?.Invoke(result);
    }

    void ApplyDamage(ShotResult result)
    {
        if (!result.DidHit)
        {
            Debug.Log("[Gun] 탕! 빗나감", this);
            return;
        }

        var hitbox = result.Hit.collider.GetComponent<Hitbox>();
        if (hitbox == null)
        {
            Debug.Log($"[Gun] 탕! {result.Hit.collider.name}에 맞음 ({result.Hit.distance:F2}m)", result.Hit.collider);
            return;
        }

        Debug.Log($"[Gun] 탕! {(hitbox.Owner != null ? hitbox.Owner.name : hitbox.name)}의 {hitbox.Part}에 맞음", hitbox);
        hitbox.ApplyHit();
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
}
