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
/// 실린더: 총을 쥔 채 손목을 왼쪽으로 빠르게 꺾으면(스냅) 실린더가 옆으로 열리고(쏠 수 없음), 오른쪽으로 꺾거나 총을 내려놓으면 닫힌다.
/// 열린 동안 반대 손으로 실린더를 잡고(그립) 손을 돌리면 실린더가 따라 돈다. 손으로 돌리기는 모습만 바뀌는 연출이고 약실(어느 칸에 총알이 있는지)은 바뀌지 않는다.
/// 총알 넣기: 실린더가 열려 있을 때 여분 총알(<see cref="SpareBullet"/>)을 쥔 손을 총을 쥔 손에 가까이 가져가거나, 총알이 실린더나 총을 쥔 손에 닿으면(던져도) 들어간다.
/// 넣은 총알이 있는 채로 실린더를 닫으면 약실이 무작위로 돈다(<see cref="CylinderClosed"/>로 넣은 수를 알린다. 진행 컨트롤러가 턴을 끝낸다).
/// 모습은 <see cref="RevolverModel"/>이 맡는다.
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
        /// <summary>이 발로 총알이 0발이 되어 진행자가 다시 장전했는지.</summary>
        public bool Reloaded;
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
    [SerializeField, Tooltip("움직이는 부품 (실린더, 공이치기, 탄피)")] RevolverModel m_Model;
    [SerializeField, Tooltip("테이블에 놓여 있을 때만 켜는 넉넉한 집기용 콜라이더. 멀리서 레이로 겨누기 쉽게 한다. 잡으면 끈다")] Collider m_PickupCollider;
    [SerializeField, Tooltip("총열 축으로 이 속도(도/초)보다 빠르게 손목을 꺾으면 스냅으로 본다. 왼쪽으로 꺾으면 열고 오른쪽으로 꺾으면 닫는다")]
    float m_FlickRollSpeed = 540f;
    [SerializeField, Tooltip("총을 잡거나 실린더를 열고 닫은 직후 이 시간(초) 동안은 스냅을 받지 않는다 (잡는 순간의 튐과 스냅 뒤 되돌아오는 움직임을 무시)")]
    float m_GestureGrace = 0.3f;
    [SerializeField, Tooltip("열린 실린더를 잡는 손잡이 (실린더 뼈의 자식). 잡은 손이 실린더 축을 도는 만큼 실린더가 돈다")] XRSimpleInteractable m_CylinderHandle;
    [SerializeField] string m_SpinPrompt = "Press Grip to hold the cylinder";

    [Header("Loading")]
    [SerializeField, Tooltip("총알을 쥔 손이 총을 쥔 손에 이 거리(m) 안으로 오면 넣는다")] float m_HandLoadDistance = 0.15f;
    [SerializeField, Tooltip("놓인 총알(던진 총알 포함)이 총을 쥔 손에 이 거리(m) 안으로 오면 넣는다")] float m_HandTouchDistance = 0.08f;
    [SerializeField, Tooltip("총알이 실린더 중심에서 이 거리(m) 안으로 오면 넣는다")] float m_CylinderLoadDistance = 0.06f;
    [SerializeField, Tooltip("던진 총알이 지나간 길을 이 간격(m)으로 나눠서 본다")] float m_LoadCheckStep = 0.02f;
    [SerializeField] string m_OpenCylinderPrompt = "Flick your wrist left to open the cylinder";

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

    /// <summary>총알 한 발이 들어갔을 때 (이 기기에 보이는 총알 수).</summary>
    public event Action<int> BulletInserted;

    /// <summary>실린더를 닫았을 때 (연 동안 넣은 총알 수). 넣은 총알이 있으면 약실이 돌았다(원격 판정이면 호스트가 돌린다).</summary>
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
    Quaternion m_LastRotation;
    float m_GestureEnableTime;
    int m_FlickFrames;
    int m_FlickSign;
    IXRSelectInteractor m_CylinderHolder;
    float m_CylinderHandAngle;
    float m_CylinderHandVelocity;
    // 원격 판정일 때 보여 줄 총알 수 (호스트가 알려 준 수)
    int m_RemoteBulletCount;
    int m_FollowedShellCount = -1;
    readonly List<XRBaseInputInteractor> m_HoveringInteractors = new List<XRBaseInputInteractor>();
    readonly RaycastHit[] m_Hits = new RaycastHit[16];
    bool m_IsFollowingRemote;
    Vector3 m_RemotePosition;
    Quaternion m_RemoteRotation;
    int m_InsertedSinceOpen;
    readonly Dictionary<SpareBullet, Vector3> m_LastBulletPositions = new Dictionary<SpareBullet, Vector3>();
    readonly List<SpareBullet> m_BulletBuffer = new List<SpareBullet>();

    public bool IsEquipped => m_Holder != null;

    /// <summary>총을 쥔 손 (진동용). 쥐고 있지 않으면 null.</summary>
    public XRBaseInputInteractor HoldingHand => m_Holder as XRBaseInputInteractor;
    public Transform Muzzle => m_Muzzle;
    public bool IsCylinderOpen { get; private set; }

    /// <summary>지금 들어 있는 총알 수 (공개 정보).</summary>
    public int BulletCount => m_Revolver != null ? m_Revolver.BulletCount : 0;

    /// <summary>실린더에 보여 줄 총알 수. 원격 판정이면 이 기기 약실 대신 호스트가 알려 준 수를 쓴다.</summary>
    public int DisplayedBulletCount => ResolvesShotsRemotely ? m_RemoteBulletCount : BulletCount;

    /// <summary>실린더 모습 (네트워크로 상대 화면에 보낼 때 쓴다). 모델이 없으면 0.</summary>
    public float CylinderSpinAngle => m_Model != null ? m_Model.SpinAngle : 0f;

    /// <summary>true면 방아쇠도 장전도 막힌다. 턴이 끝나면 진행 컨트롤러가 잠근다.</summary>
    public bool IsTriggerLocked { get; set; }

    /// <summary>지금 꺼내져 있는지 (보이고 잡을 수 있는지).</summary>
    public bool IsPresented => gameObject.activeSelf;

    /// <summary>true면 쏠 때 약실을 쓰지 않고 <see cref="ShotRequested"/>로 알리기만 한다 (멀티의 클라이언트).</summary>
    public bool ResolvesShotsRemotely { get; set; }

    void Awake()
    {
        m_Grab = GetComponent<XRGrabInteractable>();
        // 지급 위치에서는 키네마틱이라 놓을 때 던지기를 하면 경고가 난다. 총은 던질 일이 없다
        m_Grab.throwOnDetach = false;
        m_Rigidbody = GetComponent<Rigidbody>();

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
        if (m_CylinderHandle != null)
        {
            m_CylinderHandle.hoverEntered.AddListener(OnHandleHoverChanged);
            m_CylinderHandle.hoverExited.AddListener(OnHandleHoverChanged);
            m_CylinderHandle.selectEntered.AddListener(OnHandleSelectEntered);
            m_CylinderHandle.selectExited.AddListener(OnHandleSelectExited);
            m_CylinderHandle.enabled = false;
        }
    }

    void OnDisable()
    {
        m_Grab.hoverEntered.RemoveListener(OnHoverEntered);
        m_Grab.hoverExited.RemoveListener(OnHoverExited);
        m_Grab.selectEntered.RemoveListener(OnSelectEntered);
        m_Grab.selectExited.RemoveListener(OnSelectExited);
        m_Grab.activated.RemoveListener(OnActivated);
        if (m_CylinderHandle != null)
        {
            m_CylinderHandle.hoverEntered.RemoveListener(OnHandleHoverChanged);
            m_CylinderHandle.hoverExited.RemoveListener(OnHandleHoverChanged);
            m_CylinderHandle.selectEntered.RemoveListener(OnHandleSelectEntered);
            m_CylinderHandle.selectExited.RemoveListener(OnHandleSelectExited);
            InteractionPrompt.Hide(m_CylinderHandle);
        }
        m_CylinderHolder = null;
        m_HoveringInteractors.Clear();
        InteractionPrompt.Hide(this);
        InteractionPrompt.Hide(m_Muzzle);
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
        SetPickupCollider(false);
        if (m_ReturnRoutine != null)
        {
            StopCoroutine(m_ReturnRoutine);
            m_ReturnRoutine = null;
        }

        // 반대 손을 찾아 둔다 (실린더 돌리기용)
        m_OtherHands.Clear();
        var rig = m_Holder.transform.GetComponentInParent<XROrigin>();
        if (rig != null)
        {
            foreach (var hand in rig.GetComponentsInChildren<NearFarInteractor>())
                if (!ReferenceEquals(hand, m_Holder))
                    m_OtherHands.Add(hand);
        }

        m_LastRotation = transform.rotation;
        m_FlickFrames = 0;
        m_GestureEnableTime = Time.time + m_GestureGrace;
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (!ReferenceEquals(args.interactorObject, m_Holder))
            return;

        m_Holder = null;
        m_OtherHands.Clear();
        SetHandleEnabled(false);
        InteractionPrompt.Hide(m_Muzzle);

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

        if (IsEquipped)
        {
            CheckFlick();
            if (m_CylinderHolder != null)
                SpinByHandle();
            if (IsCylinderOpen)
                CheckBulletLoading();
            m_LastRotation = transform.rotation;
            UpdateOpenPrompt();
        }

        if (m_Model != null)
            m_Model.SetShellCount(m_IsFollowingRemote && m_FollowedShellCount >= 0 ? m_FollowedShellCount : DisplayedBulletCount);
    }

    void UpdatePrompt()
    {
        if (IsEquipped)
            return;

        // 다른 것을 쥐고 있지 않은 손이 가리키면 안내 문구를 띄운다
        var canPickUp = false;
        foreach (var interactor in m_HoveringInteractors)
        {
            if (!interactor.hasSelection)
                canPickUp = true;
        }

        // 이 기기 사람이 집어야 할 총이 테이블에 놓여 있으면, 가리키지 않아도 안내한다 (다른 안내가 떠 있으면 양보)
        var waitingForPickup = IsPresented && m_Grab.enabled && !m_IsFollowingRemote && m_HomePoint != null
            && !InteractionPrompt.IsOwnedByOther(this);

        if (canPickUp || waitingForPickup)
            InteractionPrompt.Show(this, m_PickUpPrompt);
        else
            InteractionPrompt.Hide(this);
    }

    void SetPickupCollider(bool enabled)
    {
        if (m_PickupCollider != null)
            m_PickupCollider.enabled = enabled;
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
            SetPickupCollider(false);
            m_IsFollowingRemote = true;
        }

        m_RemotePosition = position;
        m_RemoteRotation = rotation;
    }

    /// <summary>위치와 함께 실린더 모습(열림, 돈 각도, 보이는 총알 수)도 따라간다.</summary>
    public void FollowRemotePose(Vector3 position, Quaternion rotation, bool cylinderOpen, float spinAngle, int shellCount)
    {
        FollowRemotePose(position, rotation);
        if (!m_IsFollowingRemote)
            return;

        m_FollowedShellCount = shellCount;
        if (m_Model != null)
            m_Model.SetRemoteState(cylinderOpen, spinAngle);
    }

    /// <summary>원격 판정일 때 실린더에 보여 줄 총알 수 (호스트가 알려 준다).</summary>
    public void SetRemoteBulletCount(int count) => m_RemoteBulletCount = count;

    /// <summary>지급 위치에 꺼낸다. 그 자리에 가만히 놓이고, 떨어뜨리면 이 위치로 돌아온다.</summary>
    public void Present(Transform homePoint)
    {
        m_IsFollowingRemote = false;
        m_HomePoint = homePoint;
        IsTriggerLocked = false;
        gameObject.SetActive(true);
        SetCylinderOpen(false);
        m_InsertedSinceOpen = 0;
        m_LastBulletPositions.Clear();
        if (m_Model != null)
            m_Model.ResetPose();
        m_FollowedShellCount = -1;
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
        // 테이블에 놓인 동안만 넉넉한 콜라이더로 집기 쉽게 한다
        SetPickupCollider(true);
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

    /// <summary>실린더를 연다 (사수가 손목을 왼쪽으로 꺾었을 때). 열린 동안은 쏠 수 없다.</summary>
    public void OpenCylinder()
    {
        if (IsCylinderOpen || IsTriggerLocked || m_IsCocking || !IsPresented)
            return;

        SetCylinderOpen(true);
        SetHandleEnabled(true);
        m_InsertedSinceOpen = 0;
        m_LastBulletPositions.Clear();
        Debug.Log("[Gun] 실린더가 열렸다 (쏠 수 없음)", this);
        CylinderOpened?.Invoke();
    }

    // 여분 총알을 쥐고 있는데 실린더가 닫혀 있으면 여는 방법을 알려 준다
    void UpdateOpenPrompt()
    {
        var holdingBullet = false;
        if (!IsCylinderOpen && !IsTriggerLocked)
        {
            foreach (var bullet in SpareBullet.Active)
                if (bullet != null && !bullet.IsConsumed && bullet.Gun == this && bullet.IsHeld)
                    holdingBullet = true;
        }

        if (holdingBullet)
            InteractionPrompt.Show(m_Muzzle, m_OpenCylinderPrompt);
        else
            InteractionPrompt.Hide(m_Muzzle);
    }

    int ChamberCount => m_Config != null ? m_Config.ChamberCount : 6;

    /// <summary>실린더가 열려 있고 총을 쥐고 있을 때 총알을 넣는다. 손에 들린 총알이면 손에서 빼낸다. 넣었으면 true.</summary>
    public bool TryInsert(SpareBullet bullet)
    {
        if (bullet == null || bullet.IsConsumed || !IsEquipped || !IsCylinderOpen || IsTriggerLocked || m_IsCocking)
            return false;
        if (DisplayedBulletCount >= ChamberCount)
        {
            Debug.Log("[Gun] 약실이 꽉 차서 더 넣을 수 없다", this);
            return false;
        }

        // 원격 판정이면 약실은 호스트가 바꾼다. 여기서는 보이는 수만 늘리고, 닫을 때 넣은 수를 알린다
        if (ResolvesShotsRemotely)
            m_RemoteBulletCount++;
        else if (m_Revolver != null)
            m_Revolver.Insert();
        m_InsertedSinceOpen++;
        m_LastBulletPositions.Remove(bullet);
        bullet.Consume();
        Debug.Log($"[Gun] 총알을 넣었다 (총알 {DisplayedBulletCount}발)", this);
        BulletInserted?.Invoke(DisplayedBulletCount);
        return true;
    }

    // 이 총에 넣을 수 있는 총알 중 총을 쥔 손이나 실린더에 닿은 것을 넣는다
    void CheckBulletLoading()
    {
        if (IsTriggerLocked || m_IsCocking || m_Model == null || DisplayedBulletCount >= ChamberCount)
            return;

        var hand = m_Holder.transform.position;
        var cylinder = m_Model.CylinderCenter;
        m_BulletBuffer.Clear();
        m_BulletBuffer.AddRange(SpareBullet.Active);
        foreach (var bullet in m_BulletBuffer)
        {
            if (bullet == null || bullet.IsConsumed || bullet.Gun != this)
                continue;

            var position = bullet.transform.position;
            if (!m_LastBulletPositions.TryGetValue(bullet, out var lastPosition))
                lastPosition = position;
            m_LastBulletPositions[bullet] = position;

            if (ShouldLoad(bullet, lastPosition, position, hand, cylinder) && TryInsert(bullet) && DisplayedBulletCount >= ChamberCount)
                return;
        }
    }

    bool ShouldLoad(SpareBullet bullet, Vector3 from, Vector3 to, Vector3 gunHand, Vector3 cylinder)
    {
        // 총알을 쥔 손이 총을 쥔 손에 가까워졌다
        if (bullet.IsHeld && Vector3.Distance(bullet.HoldingHandPosition, gunHand) <= m_HandLoadDistance)
            return true;

        // 총알이 실린더나 총을 쥔 손에 닿았다. 던진 총알은 한 프레임에 멀리 가므로 지나간 길을 나눠서 본다
        var steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(from, to) / Mathf.Max(0.005f, m_LoadCheckStep)), 1, 50);
        for (var i = 0; i <= steps; i++)
        {
            var point = Vector3.Lerp(from, to, (float)i / steps);
            if (Vector3.Distance(point, gunHand) <= m_HandTouchDistance || Vector3.Distance(point, cylinder) <= m_CylinderLoadDistance)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 호스트: 상대 기기의 사수가 총알을 넣고 실린더를 닫았다. 같은 수를 약실에 넣고 돌린 뒤 <see cref="CylinderClosed"/>로 알린다.
    /// </summary>
    public void LoadRemote(int inserted)
    {
        if (m_Revolver == null || inserted <= 0)
            return;

        var loaded = 0;
        while (loaded < inserted && m_Revolver.Insert())
            loaded++;
        m_Revolver.Spin();
        Debug.Log($"[Gun] 상대 사수가 총알 {loaded}발을 넣고 돌렸다 (총알 {m_Revolver.BulletCount}발)", this);
        CylinderClosed?.Invoke(loaded);
    }

    // 손목 스냅: 총열 축으로 빠르게 도는 프레임이 두 번 이어지면 스냅으로 본다 (한 프레임 튐은 무시).
    // 총구 쪽을 보고 위가 왼쪽으로 넘어가면 열고, 오른쪽으로 넘어가면 닫는다
    void CheckFlick()
    {
        var dt = Time.deltaTime;
        if (dt <= 0f || Time.time < m_GestureEnableTime)
        {
            m_FlickFrames = 0;
            return;
        }

        (transform.rotation * Quaternion.Inverse(m_LastRotation)).ToAngleAxis(out var angle, out var axis);
        if (angle > 180f)
            angle -= 360f;
        var roll = Vector3.Dot(axis * (angle / dt), m_Muzzle.forward);

        var sign = roll > m_FlickRollSpeed ? 1 : roll < -m_FlickRollSpeed ? -1 : 0;
        m_FlickFrames = sign != 0 && sign == m_FlickSign ? m_FlickFrames + 1 : sign != 0 ? 1 : 0;
        m_FlickSign = sign;
        if (m_FlickFrames < 2)
            return;

        m_FlickFrames = 0;
        if (sign > 0 && !IsCylinderOpen)
            OpenCylinder();
        else if (sign < 0 && IsCylinderOpen)
            CloseCylinder("손목 스냅");
        else
            return;

        m_GestureEnableTime = Time.time + m_GestureGrace;
    }

    // 빈 반대 손이 열린 실린더 가까이에서 움직이면, 실린더 둘레 방향 속도만큼 실린더를 민다 (모습만 바뀐다)
    // ── 실린더 잡고 돌리기 ──

    // 손잡이는 실린더가 열려 있고 총을 쥐고 있을 때만 잡을 수 있다
    void SetHandleEnabled(bool enabled)
    {
        if (m_CylinderHandle == null)
            return;

        if (!enabled && m_CylinderHolder != null)
            m_CylinderHandle.interactionManager.SelectExit(m_CylinderHolder, m_CylinderHandle);
        m_CylinderHandle.enabled = enabled && IsEquipped;
        if (!m_CylinderHandle.enabled)
            InteractionPrompt.Hide(m_CylinderHandle);
    }

    void OnHandleHoverChanged(BaseInteractionEventArgs args)
    {
        if (m_CylinderHandle.isHovered && !m_CylinderHandle.isSelected)
            InteractionPrompt.Show(m_CylinderHandle, m_SpinPrompt);
        else
            InteractionPrompt.Hide(m_CylinderHandle);
    }

    void OnHandleSelectEntered(SelectEnterEventArgs args)
    {
        // 총을 쥔 손으로는 잡을 수 없다
        if (ReferenceEquals(args.interactorObject, m_Holder))
        {
            m_CylinderHandle.interactionManager.SelectExit(args.interactorObject, m_CylinderHandle);
            return;
        }

        m_CylinderHolder = args.interactorObject;
        m_CylinderHandAngle = HandAngle(m_CylinderHolder);
        m_CylinderHandVelocity = 0f;
        InteractionPrompt.Hide(m_CylinderHandle);
    }

    void OnHandleSelectExited(SelectExitEventArgs args)
    {
        if (!ReferenceEquals(args.interactorObject, m_CylinderHolder))
            return;

        m_CylinderHolder = null;
        // 놓는 순간의 속도로 계속 돌다가 마찰로 멈춘다
        if (m_Model != null)
            m_Model.Push(m_CylinderHandVelocity);
    }

    // 잡은 손이 실린더 축 둘레로 돈 각도만큼 실린더를 돌린다. 방향이 손 움직임과 같다
    void SpinByHandle()
    {
        if (m_Model == null)
            return;

        var angle = HandAngle(m_CylinderHolder);
        var delta = Mathf.DeltaAngle(m_CylinderHandAngle, angle);
        m_CylinderHandAngle = angle;
        m_Model.Rotate(delta);
        if (Time.deltaTime > 0f)
            m_CylinderHandVelocity = Mathf.Lerp(m_CylinderHandVelocity, delta / Time.deltaTime, 0.5f);
    }

    // 실린더 축을 기준으로 손이 어느 방향에 있는지 (도)
    float HandAngle(IXRSelectInteractor hand)
    {
        var axis = m_Model.CylinderAxis;
        var radial = Vector3.ProjectOnPlane(hand.transform.position - m_Model.CylinderCenter, axis);
        if (radial.sqrMagnitude < 0.000001f)
            return m_CylinderHandAngle;

        var reference = Vector3.ProjectOnPlane(transform.forward, axis);
        if (reference.sqrMagnitude < 0.000001f)
            reference = Vector3.ProjectOnPlane(transform.up, axis);
        return Vector3.SignedAngle(reference.normalized, radial.normalized, axis);
    }

    void CloseCylinder(string reason)
    {
        if (!IsCylinderOpen)
            return;

        SetCylinderOpen(false);
        SetHandleEnabled(false);
        var inserted = m_InsertedSinceOpen;
        m_InsertedSinceOpen = 0;
        if (inserted > 0 && !ResolvesShotsRemotely && m_Revolver != null)
        {
            m_Revolver.Spin();
            Debug.Log($"[Gun] 실린더를 닫고 돌렸다 ({reason}, 넣은 총알 {inserted}발, 총알 {m_Revolver.BulletCount}발)", this);
        }
        else
        {
            Debug.Log($"[Gun] 실린더를 닫았다 ({reason}, 넣은 총알 {inserted}발)", this);
        }

        CylinderClosed?.Invoke(inserted);
    }

    void SetCylinderOpen(bool open)
    {
        IsCylinderOpen = open;
        if (m_Model != null)
            m_Model.SetOpen(open);
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
        var hammerDelay = m_Config != null ? m_Config.HammerDelay : 0f;
        if (m_Model != null)
            m_Model.Cock(hammerDelay);
        if (hammerDelay > 0f)
            yield return new WaitForSeconds(hammerDelay);
        m_IsCocking = false;
        if (m_Model != null)
            m_Model.Release();
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
            {
                result.Reloaded = true;
                Debug.Log($"[Gun] 진행자가 다시 장전했다 (총알 {m_Revolver.BulletCount}발)", this);
            }
        }

        Fired?.Invoke(result);
        return result;
    }

    ShotResult Hitscan(Vector3 rayOrigin, Vector3 direction, Vector3 muzzlePosition, XROrigin shooterRig)
    {
        var result = new ShotResult { Origin = muzzlePosition, Direction = direction, EndPoint = muzzlePosition + direction * m_Range };

        // 총 자신, 쏜 사람 리그의 히트박스가 아닌 콜라이더(이동용 캡슐 등)는 건너뛰고 가장 가까운 것을 고른다.
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
