using UnityEngine;

/// <summary>
/// 리볼버 모델(Reichsrevolver M1879)의 움직이는 부품. <see cref="Gun"/>이 상태를 알려 주면 모습만 바꾼다. 판정과는 관계없다.
/// - 실린더 열기: 총열과 나란한 축(크레인)을 중심으로 왼쪽으로 젖혀진다. 실제 M1879는 옆으로 열리지 않지만 게임 연출로 이렇게 한다.
/// - 실린더 돌리기: 손으로 밀면 실린더 축으로 돌고 마찰로 서서히 멈춘다. 방아쇠를 당기면 한 칸(60도) 돈다.
/// - 공이치기: 방아쇠를 당기면 젖혀지고 쏘면 내려간다.
/// - 총알: 실린더 안의 탄피 6개 중 총알 수만큼 보인다. 어느 칸에 보이는지는 연출일 뿐 실제 약실과 관계없다.
/// 뼈(Cylinder, Hammer)의 처음 모습을 기준으로 움직이므로, 모델의 Animator는 끈다.
/// </summary>
public class RevolverModel : MonoBehaviour
{
    [SerializeField, Tooltip("실린더 뼈. 로컬 Y축이 실린더 축이다")] Transform m_Cylinder;
    [SerializeField, Tooltip("공이치기 뼈")] Transform m_Hammer;
    [SerializeField, Tooltip("실린더 안의 탄피 (칸 순서)")] GameObject[] m_Shells;

    [Header("Swing Out")]
    [SerializeField, Tooltip("크레인 축이 지나는 점 (실린더 뼈의 부모 기준)")] Vector3 m_CranePivot;
    [SerializeField, Tooltip("크레인 축 방향 (실린더 뼈의 부모 기준, 총구 쪽). 이 축으로 양의 각도만큼 돌면 실린더가 왼쪽으로 나온다")] Vector3 m_CraneAxis = Vector3.forward;
    [SerializeField] float m_OpenAngle = 90f;
    [SerializeField, Tooltip("열고 닫는 데 걸리는 시간(초)")] float m_OpenDuration = 0.12f;

    [Header("Spin")]
    [SerializeField, Tooltip("손으로 돌린 실린더가 멈추는 정도 (클수록 빨리 멈춤)")] float m_SpinFriction = 1.2f;
    [SerializeField, Tooltip("손으로 돌릴 수 있는 최고 속도(도/초)")] float m_MaxSpinSpeed = 1440f;

    [Header("Hammer")]
    [SerializeField] float m_HammerCockAngle = -45f;

    Vector3 m_CylinderRestPosition;
    Quaternion m_CylinderRestRotation;
    Quaternion m_HammerRestRotation;
    float m_Open;
    float m_OpenTarget;
    float m_SpinAngle;
    float m_SpinVelocity;
    float m_AdvanceRemaining;
    float m_AdvanceSpeed;
    float m_Cock;
    float m_CockSpeed;

    /// <summary>실린더가 돈 각도(도). 네트워크로 상대 화면에 보낼 때 쓴다.</summary>
    public float SpinAngle => m_SpinAngle;

    /// <summary>지금 실린더 중심 (월드). 손으로 돌리기 판정에 쓴다.</summary>
    public Vector3 CylinderCenter => m_Cylinder.position;

    /// <summary>실린더 축 (월드).</summary>
    public Vector3 CylinderAxis => m_Cylinder.up;

    void Awake()
    {
        m_CylinderRestPosition = m_Cylinder.localPosition;
        m_CylinderRestRotation = m_Cylinder.localRotation;
        if (m_Hammer != null)
            m_HammerRestRotation = m_Hammer.localRotation;
    }

    /// <summary>처음 모습으로 바로 되돌린다 (닫힘, 공이치기 내려감).</summary>
    public void ResetPose()
    {
        m_Open = m_OpenTarget = 0f;
        m_SpinVelocity = 0f;
        m_AdvanceRemaining = 0f;
        m_Cock = 0f;
        m_CockSpeed = 0f;
        Apply();
    }

    public void SetOpen(bool open) => m_OpenTarget = open ? 1f : 0f;

    /// <summary>손으로 민 속도(도/초)를 더한다. 같은 방향이면 빨라지고, 반대 방향이면 느려진다.</summary>
    public void Push(float degreesPerSecond)
    {
        m_SpinVelocity = Mathf.Clamp(m_SpinVelocity + degreesPerSecond, -m_MaxSpinSpeed, m_MaxSpinSpeed);
    }

    /// <summary>방아쇠를 당겼다. 이 시간 동안 공이치기가 젖혀지고 실린더가 한 칸 돈다.</summary>
    public void Cock(float duration)
    {
        var time = Mathf.Max(0.01f, duration);
        m_CockSpeed = 1f / time;
        m_AdvanceRemaining = 60f;
        m_AdvanceSpeed = 60f / time;
    }

    /// <summary>쐈다. 공이치기가 내려간다.</summary>
    public void Release()
    {
        m_CockSpeed = 0f;
        m_Cock = 0f;
    }

    public void SetShellCount(int count)
    {
        for (var i = 0; i < m_Shells.Length; i++)
            if (m_Shells[i] != null)
                m_Shells[i].SetActive(i < count);
    }

    /// <summary>상대 기기에서 받은 모습을 그대로 보여 준다.</summary>
    public void SetRemoteState(bool open, float spinAngle)
    {
        m_OpenTarget = open ? 1f : 0f;
        m_SpinAngle = spinAngle;
        m_SpinVelocity = 0f;
        m_AdvanceRemaining = 0f;
    }

    void LateUpdate()
    {
        var dt = Time.deltaTime;
        m_Open = Mathf.MoveTowards(m_Open, m_OpenTarget, dt / Mathf.Max(0.01f, m_OpenDuration));

        if (m_SpinVelocity != 0f)
        {
            m_SpinAngle += m_SpinVelocity * dt;
            m_SpinVelocity *= Mathf.Exp(-m_SpinFriction * dt);
            if (Mathf.Abs(m_SpinVelocity) < 5f)
                m_SpinVelocity = 0f;
        }

        if (m_AdvanceRemaining > 0f)
        {
            var step = Mathf.Min(m_AdvanceRemaining, m_AdvanceSpeed * dt);
            m_SpinAngle += step;
            m_AdvanceRemaining -= step;
        }
        m_SpinAngle = Mathf.Repeat(m_SpinAngle, 360f);

        if (m_CockSpeed > 0f)
            m_Cock = Mathf.Min(1f, m_Cock + m_CockSpeed * dt);

        Apply();
    }

    void Apply()
    {
        // 처음과 끝에서 부드럽게
        var open = Mathf.SmoothStep(0f, 1f, m_Open);
        var crane = Quaternion.AngleAxis(m_OpenAngle * open, m_CraneAxis);
        m_Cylinder.localPosition = m_CranePivot + crane * (m_CylinderRestPosition - m_CranePivot);
        m_Cylinder.localRotation = crane * m_CylinderRestRotation * Quaternion.AngleAxis(m_SpinAngle, Vector3.up);

        if (m_Hammer != null)
            m_Hammer.localRotation = m_HammerRestRotation * Quaternion.Euler(m_HammerCockAngle * m_Cock, 0f, 0f);
    }
}
