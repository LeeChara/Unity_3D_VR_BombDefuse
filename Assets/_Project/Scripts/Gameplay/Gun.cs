using System;
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 리볼버. 그립을 쥐고 있는 동안 손에 들리고, 놓으면 떨어진다.
/// 쥔 채로 트리거를 당기면 공이치기 연출 시간(0.6초) 뒤에 약실(<see cref="Revolver"/>)을 한 칸 쏜다.
/// 실탄·공포탄 모두 총구 방향으로 레이캐스트해서 어느 <see cref="Hitbox"/>를 겨눴는지 알린다. 실탄이면 그 부위에 피해를 준다.
/// 총알 넣기: 총을 쥔 채, 손에서 놓인 <see cref="SpareBullet"/>이 장전 영역(<see cref="GunLoadZone"/>)에 닿으면 들어간다.
/// 총알을 집거나 넣으면 실린더가 열리고(쏠 수 없음), 총을 내려놓거나 흔들거나 반대 손을 대면 닫는다. 넣은 총알이 있으면 닫을 때 돌린다.
/// 게임 진행 컨트롤러가 <see cref="Present"/>로 지급 위치에 꺼내고 <see cref="Withdraw"/>로 치운다. 꺼내 둔 동안 떨어뜨리면 잠시 뒤 지급 위치로 돌아온다.
/// 멀티: 약실은 호스트의 총만 믿는다. 클라이언트의 총은 <see cref="ResolvesShotsRemotely"/>를 켜서 쏠 때 <see cref="ShotRequested"/>로 호스트에 맡기고,
/// 호스트가 <see cref="FireRemote"/>로 판정한 결과를 <see cref="ShowRemoteShot"/>으로 받는다. 사수가 아닌 쪽의 총은 잡을 수 없고(<see cref="SetGrabbable"/>)
/// 사수 쪽에서 보내 주는 위치를 따라간다(<see cref="FollowRemotePose"/>).
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class Gun : MonoBehaviour
{
    /// <summary>한 발의 결과.</summary>
    public struct ShotResult
    {
        /// <summary>총구 위치 (이펙트 시작점).</summary>
        public Vector3 Origin;
        public Vector3 Direction;
        /// <summary>실탄이면 true, 공포탄이면 false.</summary>
        public bool IsLive;
        public bool DidHit;
        public RaycastHit Hit;
        /// <summary>겨눈 몸의 부위. 몸이 아니면 null.</summary>
        public Hitbox HitBox;
        /// <summary>맞은 지점. 안 맞았으면 사거리 끝.</summary>
        public Vector3 EndPoint;
    }

    [SerializeField, Tooltip("총구. Z축(파란 화살표)이 발사 방향")] Transform m_Muzzle;
    [SerializeField, Tooltip("수치 (약실 수, 총알 수, 공이치기 연출 시간)")] TuningConfig m_Config;

    [Header("Hitscan")]
    [SerializeField] float m_Range = 50f;
    [SerializeField] LayerMask m_HitMask = ~0;
    [SerializeField, Tooltip("다음 발사까지 최소 간격(초)")] float m_FireCooldown = 0.25f;
    [SerializeField, Tooltip("레이를 총구보다 이만큼 뒤에서 쏜다(m). 총구를 머리에 댔을 때 총구가 머리 안에 있어도 맞게 하려고")]
    float m_RayBackOffset = 0.15f;

    [Header("Cylinder")]
    [SerializeField, Tooltip("열리고 닫히는 모습을 보여 줄 실린더 (임시 모델)")] Transform m_CylinderVisual;
    [SerializeField, Tooltip("열렸을 때 실린더가 옆으로 나오는 거리 (로컬)")] Vector3 m_CylinderOpenOffset = new Vector3(0f, 0f, 0.05f);
    [SerializeField, Tooltip("이 속도(도/초)보다 빠르게 돌리면 흔든 것으로 보고 실린더를 닫는다")] float m_ShakeAngularSpeed = 720f;
    [SerializeField, Tooltip("빈 반대 손이 이 거리(m) 안으로 들어오면 실린더를 닫는다")] float m_OtherHandCloseDistance = 0.1f;
    [SerializeField, Tooltip("총을 잡거나 실린더가 열린 직후 이 시간(초) 동안은 흔들기·반대 손으로 닫지 않는다 (잡는 순간 총이 손으로 튀는 움직임을 무시)")]
    float m_CloseGestureGrace = 0.3f;

    [Header("Prompt")]
    [SerializeField] string m_PickUpPrompt = "Press Grip to pick up";

    [Header("Network")]
    [SerializeField, Tooltip("상대가 든 총의 위치를 따라갈 때 부드럽게 하는 정도 (클수록 빨리 따라감)")] float m_RemoteFollowSharpness = 20f;

    /// <summary>방아쇠를 당겨 공이치기가 젖혀질 때. 결과는 공이치기 연출 시간 뒤 <see cref="Fired"/>로 온다.</summary>
    public event Action HammerCocked;

    /// <summary>약실 한 칸을 쐈을 때 (실탄·공포탄 모두).</summary>
    public event Action<ShotResult> Fired;

    /// <summary>실린더가 열렸을 때.</summary>
    public event Action CylinderOpened;

    /// <summary>총알이 한 발 들어갔을 때 (지금 총알 수).</summary>
    public event Action<int> BulletInserted;

    /// <summary>실린더를 닫았을 때 (이번에 넣은 총알 수). 넣은 총알이 있으면 돌린 뒤다.</summary>
    public event Action<int> CylinderClosed;

    /// <summary><see cref="ResolvesShotsRemotely"/>일 때 공이치기가 내려간 순간 (레이 시작점, 방향). 결과는 호스트가 정한다.</summary>
    public event Action<Vector3, Vector3> ShotRequested;

    XRGrabInteractable m_Grab;
    IXRSelectInteractor m_Holder;
    readonly List<XRBaseInputInteractor> m_OtherHands = new List<XRBaseInputInteractor>();
    float m_NextFireTime;
    bool m_IsCocking;
    Revolver m_Revolver;
    Rigidbody m_Rigidbody;
    Transform m_HomePoint;
    Coroutine m_ReturnRoutine;
    Vector3 m_CylinderRestLocalPosition;
    int m_InsertedSinceOpen;
    Quaternion m_LastRotation;
    bool m_OtherHandWasNear;
    float m_GestureEnableTime;
    int m_ShakeFrames;
    readonly List<XRBaseInputInteractor> m_HoveringInteractors = new List<XRBaseInputInteractor>();
    readonly RaycastHit[] m_Hits = new RaycastHit[16];
    bool m_IsFollowingRemote;
    Vector3 m_RemotePosition;
    Quaternion m_RemoteRotation;

    public bool IsEquipped => m_Holder != null;
    public Transform Muzzle => m_Muzzle;
    public bool IsCylinderOpen { get; private set; }

    /// <summary>지금 들어 있는 총알 수 (공개 정보).</summary>
    public int BulletCount => m_Revolver != null ? m_Revolver.BulletCount : 0;

    /// <summary>true면 방아쇠도 장전도 막힌다. 턴이 끝나면 진행 컨트롤러가 잠근다.</summary>
    public bool IsTriggerLocked { get; set; }

    /// <summary>지금 꺼내져 있는지 (보이고 잡을 수 있는지).</summary>
    public bool IsPresented => gameObject.activeSelf;

    /// <summary>true면 쏠 때 약실을 쓰지 않고 <see cref="ShotRequested"/>로 알리기만 한다 (멀티의 클라이언트).</summary>
    public bool ResolvesShotsRemotely { get; set; }

    void Awake()
    {
        m_Grab = GetComponent<XRGrabInteractable>();
        m_Rigidbody = GetComponent<Rigidbody>();
        if (m_CylinderVisual != null)
            m_CylinderRestLocalPosition = m_CylinderVisual.localPosition;

        if (m_Config == null)
            Debug.LogWarning("[Gun] TuningConfig가 연결되지 않아 약실 없이 매번 실탄으로 쏜다", this);
        ResetRevolver();
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

    /// <summary>새 게임용으로 약실을 처음 상태로 되돌린다.</summary>
    public void ResetRevolver()
    {
        m_Revolver = m_Config != null
            ? new Revolver(m_Config.ChamberCount, m_Config.StartingBullets, m_Config.ReloadIncrement)
            : null;
    }

    // ── 잡기 ──

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

        // 반대 손을 찾아 둔다 (실린더 닫기 판정용)
        m_OtherHands.Clear();
        var rig = m_Holder.transform.GetComponentInParent<XROrigin>();
        if (rig != null)
        {
            foreach (var hand in rig.GetComponentsInChildren<NearFarInteractor>())
                if (!ReferenceEquals(hand, m_Holder))
                    m_OtherHands.Add(hand);
        }

        m_LastRotation = transform.rotation;
        m_OtherHandWasNear = IsOtherHandNear();
        m_GestureEnableTime = Time.time + m_CloseGestureGrace;
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (!ReferenceEquals(args.interactorObject, m_Holder))
            return;

        m_Holder = null;
        m_OtherHands.Clear();

        // 내려놓으면 실린더를 닫는다
        if (IsCylinderOpen)
            CloseCylinder("총을 내려놓음");

        // 놓으면 떨어지고, 잠시 뒤 지급 위치로 돌아온다
        if (m_HomePoint != null && isActiveAndEnabled)
        {
            m_Rigidbody.isKinematic = false;
            m_ReturnRoutine = StartCoroutine(ReturnHomeAfterDelay());
        }
    }

    // 총을 쥔 손의 트리거
    void OnActivated(ActivateEventArgs args) => TryFire();

    void Update()
    {
        if (m_IsFollowingRemote)
        {
            var t = 1f - Mathf.Exp(-m_RemoteFollowSharpness * Time.deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, m_RemotePosition, t),
                Quaternion.Slerp(transform.rotation, m_RemoteRotation, t));
        }

        UpdatePrompt();

        if (IsEquipped && IsCylinderOpen)
            CheckCloseGestures();
        if (IsEquipped)
            m_LastRotation = transform.rotation;
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

    // ── 꺼내기·치우기 ──

    /// <summary>잡을 수 있는지. 멀티에서 사수가 아닌 기기의 총은 끈다.</summary>
    public void SetGrabbable(bool grabbable)
    {
        if (!grabbable)
            Drop();
        m_Grab.enabled = grabbable;
    }

    /// <summary>상대 기기에서 들고 있는 총의 위치를 따라간다. 잡을 수 없는 총에만 쓴다.</summary>
    public void FollowRemotePose(Vector3 position, Quaternion rotation)
    {
        if (!IsPresented || IsEquipped)
            return;

        if (!m_IsFollowingRemote)
        {
            if (m_ReturnRoutine != null)
            {
                StopCoroutine(m_ReturnRoutine);
                m_ReturnRoutine = null;
            }
            StopRigidbody();
            m_IsFollowingRemote = true;
        }

        m_RemotePosition = position;
        m_RemoteRotation = rotation;
    }

    /// <summary>지급 위치에 꺼낸다. 그 자리에 가만히 놓이고, 떨어뜨리면 이 위치로 돌아온다.</summary>
    public void Present(Transform homePoint)
    {
        m_IsFollowingRemote = false;
        m_HomePoint = homePoint;
        IsTriggerLocked = false;
        gameObject.SetActive(true);
        SetCylinderOpen(false);
        m_InsertedSinceOpen = 0;
        MoveHome();
    }

    /// <summary>손에서 놓고 치운다 (보이지 않고 잡을 수 없다).</summary>
    public void Withdraw()
    {
        IsTriggerLocked = true;
        Drop();
        if (IsCylinderOpen)
            CloseCylinder("치움");
        m_HomePoint = null;
        m_IsFollowingRemote = false;
        gameObject.SetActive(false);
    }

    void MoveHome()
    {
        if (m_HomePoint == null)
            return;
        StopRigidbody();
        transform.SetPositionAndRotation(m_HomePoint.position, m_HomePoint.rotation);
    }

    void StopRigidbody()
    {
        // 키네마틱으로 바꾸기 전에 멈춰야 경고가 나오지 않는다
        if (!m_Rigidbody.isKinematic)
        {
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
        }
        m_Rigidbody.isKinematic = true;
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

    // ── 실린더와 총알 넣기 ──

    /// <summary>실린더를 연다 (사수가 총알을 집었을 때). 열린 동안은 쏠 수 없다.</summary>
    public void OpenCylinder()
    {
        if (IsCylinderOpen || IsTriggerLocked || m_IsCocking || !IsPresented)
            return;

        SetCylinderOpen(true);
        m_InsertedSinceOpen = 0;
        m_OtherHandWasNear = IsOtherHandNear();
        m_GestureEnableTime = Mathf.Max(m_GestureEnableTime, Time.time + m_CloseGestureGrace);
        m_ShakeFrames = 0;
        Debug.Log("[Gun] 실린더가 열렸다 (쏠 수 없음)", this);
        CylinderOpened?.Invoke();
    }

    /// <summary>총을 쥐고 있을 때 손에서 놓인 총알이 닿으면 넣는다. 넣었으면 true.</summary>
    public bool TryInsert(SpareBullet bullet)
    {
        if (bullet == null || bullet.IsHeld || !IsEquipped || IsTriggerLocked || m_IsCocking || m_Revolver == null)
            return false;
        // 원격 판정이면 이 기기의 약실은 쓰지 않는다. 꽉 찼는지는 호스트가 넣을 때 본다
        if (!ResolvesShotsRemotely && m_Revolver.IsFull)
        {
            Debug.Log("[Gun] 약실이 꽉 차서 더 넣을 수 없다", this);
            return false;
        }

        OpenCylinder();
        if (!ResolvesShotsRemotely)
            m_Revolver.Insert();
        m_InsertedSinceOpen++;
        bullet.Consume();
        Debug.Log($"[Gun] 총알을 넣었다 (총알 {m_Revolver.BulletCount}발)", this);
        BulletInserted?.Invoke(m_Revolver.BulletCount);
        return true;
    }

    void CheckCloseGestures()
    {
        if (Time.time < m_GestureEnableTime)
        {
            m_OtherHandWasNear = IsOtherHandNear();
            return;
        }

        // 빠르게 도는 프레임이 두 번 이어지면 흔든 것으로 본다 (한 프레임 튐은 무시)
        var dt = Time.deltaTime;
        var fast = dt > 0f && Quaternion.Angle(m_LastRotation, transform.rotation) / dt > m_ShakeAngularSpeed;
        m_ShakeFrames = fast ? m_ShakeFrames + 1 : 0;
        if (m_ShakeFrames >= 2)
        {
            CloseCylinder("총을 흔듦");
            return;
        }

        // 반대 손이 빈손으로 새로 다가왔을 때만 닫는다.
        // 총알을 들고 다가와 놓은 손은 이미 가까이 있었으므로, 놓은 뒤 그대로 있어도 닫히지 않는다
        var near = IsOtherHandNear(out var emptyHand);
        if (near && !m_OtherHandWasNear && emptyHand)
            CloseCylinder("반대 손을 댐");
        m_OtherHandWasNear = near;
    }

    bool IsOtherHandNear() => IsOtherHandNear(out _);

    // 반대 손이 총 가까이 있는지 (손에 뭘 들고 있어도 거리만 본다). emptyHand는 가까이 있는 손이 빈손인지
    bool IsOtherHandNear(out bool emptyHand)
    {
        emptyHand = false;
        var near = false;
        foreach (var hand in m_OtherHands)
        {
            if (hand == null)
                continue;
            if (Vector3.Distance(hand.transform.position, transform.position) <= m_OtherHandCloseDistance)
            {
                near = true;
                if (!hand.hasSelection)
                    emptyHand = true;
            }
        }

        return near;
    }

    void CloseCylinder(string reason)
    {
        if (!IsCylinderOpen)
            return;

        SetCylinderOpen(false);
        var inserted = m_InsertedSinceOpen;
        m_InsertedSinceOpen = 0;
        if (inserted > 0 && m_Revolver != null)
        {
            m_Revolver.Spin();
            Debug.Log($"[Gun] 실린더를 닫고 돌렸다 ({reason}, 넣은 총알 {inserted}발, 총알 {m_Revolver.BulletCount}발)", this);
        }
        else
        {
            Debug.Log($"[Gun] 실린더를 닫았다 ({reason}, 넣은 총알 없음)", this);
        }

        CylinderClosed?.Invoke(inserted);
    }

    /// <summary>호스트: 상대 기기에서 사수가 총알을 넣고 실린더를 닫았다. 약실에 넣고 돌린 뒤 <see cref="CylinderClosed"/>를 알린다.</summary>
    public void InsertRemote(int count)
    {
        if (m_Revolver == null || IsTriggerLocked)
            return;

        var inserted = 0;
        for (var i = 0; i < count && !m_Revolver.IsFull; i++)
        {
            m_Revolver.Insert();
            inserted++;
        }
        if (inserted > 0)
            m_Revolver.Spin();

        Debug.Log($"[Gun] 상대가 총알 {inserted}발을 넣고 돌렸다 (총알 {m_Revolver.BulletCount}발)", this);
        CylinderClosed?.Invoke(inserted);
    }

    void SetCylinderOpen(bool open)
    {
        IsCylinderOpen = open;
        if (m_CylinderVisual != null)
            m_CylinderVisual.localPosition = m_CylinderRestLocalPosition + (open ? m_CylinderOpenOffset : Vector3.zero);
    }

    // ── 발사 ──

    /// <summary>쥐고 있고 쿨다운이 지났으면 방아쇠를 당긴다. 결과는 공이치기 연출 시간 뒤에 나온다.</summary>
    public bool TryFire()
    {
        if (!IsEquipped || IsTriggerLocked || m_IsCocking || Time.time < m_NextFireTime)
            return false;
        if (IsCylinderOpen)
        {
            Debug.Log("[Gun] 실린더가 열려 있어서 쏠 수 없다", this);
            return false;
        }

        StartCoroutine(FireAfterHammer());
        return true;
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

        if (ResolvesShotsRemotely)
        {
            // 약실은 호스트가 판정한다. 결과는 ShowRemoteShot으로 온다
            var direction = m_Muzzle.forward;
            ShotRequested?.Invoke(m_Muzzle.position - direction * m_RayBackOffset, direction);
            yield break;
        }

        var shooterRig = m_Holder != null ? m_Holder.transform.GetComponentInParent<XROrigin>() : null;
        Resolve(m_Muzzle.position - m_Muzzle.forward * m_RayBackOffset, m_Muzzle.forward, m_Muzzle.position, shooterRig);
    }

    /// <summary>호스트: 상대 기기의 사수가 쏜 한 발을 판정한다. 레이는 상대 기기의 총구 기준이다.</summary>
    public ShotResult FireRemote(Vector3 rayOrigin, Vector3 direction)
    {
        direction.Normalize();
        return Resolve(rayOrigin, direction, rayOrigin + direction * m_RayBackOffset, null);
    }

    /// <summary>클라이언트: 호스트가 판정한 한 발을 보여 준다 (<see cref="Fired"/>로 이펙트가 반응한다).</summary>
    public void ShowRemoteShot(ShotResult result) => Fired?.Invoke(result);

    ShotResult Resolve(Vector3 rayOrigin, Vector3 direction, Vector3 muzzlePosition, XROrigin shooterRig)
    {
        // 공포탄이어도 누구를 겨눴는지 알아야 해서 항상 레이캐스트한다
        var live = m_Revolver == null || m_Revolver.Pull();
        var result = Hitscan(rayOrigin, direction, muzzlePosition, shooterRig);
        result.IsLive = live;

        var target = result.HitBox != null ? $"{(result.HitBox.Owner != null ? result.HitBox.Owner.name : result.HitBox.name)}의 {result.HitBox.Part}" : result.DidHit ? result.Hit.collider.name : "허공";
        if (!live)
        {
            Debug.Log($"[Gun] 딸깍 (공포탄, 겨눈 곳: {target})", this);
        }
        else
        {
            Debug.Log($"[Gun] 탕! {target}", this);
            if (result.HitBox != null)
                result.HitBox.ApplyHit();

            // 총알이 0발이 되면 진행자가 장전한다 (넣는 수가 매번 늘어난다)
            if (m_Revolver != null && m_Revolver.ReloadIfEmpty())
                Debug.Log($"[Gun] 진행자가 다시 장전했다 (총알 {m_Revolver.BulletCount}발)", this);
        }

        Fired?.Invoke(result);
        return result;
    }

    ShotResult Hitscan(Vector3 rayOrigin, Vector3 direction, Vector3 muzzlePosition, XROrigin shooterRig)
    {
        var result = new ShotResult { Origin = muzzlePosition, Direction = direction, EndPoint = muzzlePosition + direction * m_Range };

        // 총 자신, 총알, 쏜 사람 리그의 히트박스가 아닌 콜라이더(이동용 캡슐 등)는 건너뛰고 가장 가까운 것을 고른다.
        // shooterRig가 null이면 상대 기기의 사수가 쏜 것이다
        var count = Physics.RaycastNonAlloc(rayOrigin, direction, m_Hits, m_Range + m_RayBackOffset, m_HitMask, QueryTriggerInteraction.Ignore);
        var closest = float.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var hit = m_Hits[i];
            var hitTransform = hit.collider.transform;
            if (hitTransform.IsChildOf(transform) || hit.collider.GetComponentInParent<SpareBullet>() != null)
                continue;
            var hitbox = hit.collider.GetComponent<Hitbox>();
            if (shooterRig != null && hitTransform.IsChildOf(shooterRig.transform) && hitbox == null)
                continue;
            // 상대 기기의 사수가 쏜 한 발은 이 기기 리그의 이동용 캡슐에 막히면 안 된다 (이 기기 사람은 히트박스로만 맞는다)
            if (shooterRig == null && hitbox == null && hit.collider.GetComponentInParent<XROrigin>() != null)
                continue;

            if (hit.distance < closest)
            {
                closest = hit.distance;
                result.DidHit = true;
                result.Hit = hit;
                result.HitBox = hitbox;
                result.EndPoint = hit.point;
            }
        }

        return result;
    }
}
