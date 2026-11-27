using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 소품 묶음. 이 오브젝트 아래의 Rigidbody 소품들은 공포 단계가 오를 때 쿵 하고 흔들리고,
/// 다가오는 벽(<see cref="DreadAtmosphere"/>, 벽에 Is Kinematic Rigidbody)에 밀려 쓰러진다.
/// - 물리는 기기마다 따로 계산한다 (네트워크로 맞추지 않는다). 그래서 쓰러지는 모양은 기기마다 조금 다르고,
///   총알 판정(<see cref="Gun"/>)은 소품을 지나간다 (기기마다 결과가 달라지지 않게).
/// - 새 게임(Seating)과 로비에서 모든 소품이 처음 자리로 돌아간다.
/// - 이 오브젝트는 흔들리는 방(DreadAtmosphere의 Shake Root) 밖에 둔다. 방 아래에 두면 흔들 때 소품이 순간이동한다.
/// </summary>
public class RoomProps : MonoBehaviour
{
    [SerializeField] GameFlowController m_Flow;
    [SerializeField, Tooltip("소품이 부딪히지 않는 레이어 (플레이어 몸, 손)")] LayerMask m_IgnoreLayers;
    [SerializeField, Min(0f), Tooltip("공포 단계가 오를 때 소품을 흔드는 속도(m/s)")] float m_KickSpeed = 0.35f;
    [SerializeField, Min(0f), Tooltip("공포 단계가 오를 때 소품을 기울이는 회전 속도(rad/s)")] float m_KickSpin = 0.6f;
    [SerializeField, Min(0.1f), Tooltip("끼었을 때 빠져나오는 최대 속도(m/s). 크면 소품이 튕겨 날아간다")] float m_MaxDepenetration = 0.8f;
    [SerializeField, Tooltip("이 높이 아래로 떨어진 소품은 처음 자리로 돌려놓는다 (바닥을 뚫었을 때)")] float m_FallLimit = -2f;

    [Header("키 큰 소품 넘어뜨리기")]
    [SerializeField, Tooltip("다가오는 벽들 (DreadAtmosphere의 Walls와 같게). 벽이 판판하게 밀면 키 큰 소품은 쓰러지지 않고 미끄러지기만 해서, 벽에 닿으면 윗부분을 방 쪽으로 민다")] Collider[] m_Walls;
    [SerializeField, Min(0f), Tooltip("이 높이(m)보다 크고 폭보다 1.5배 이상 높은 소품만 넘어뜨린다")] float m_TallHeight = 1.2f;
    [SerializeField, Min(0f), Tooltip("바닥에서 이 높이(m) 위에 올라가 있는 소품도 넘어뜨린다 (쌓인 캐비닛 윗단 등)")] float m_RaisedHeight = 0.25f;
    [SerializeField, Min(0f), Tooltip("벽에 닿은 소품의 윗부분을 미는 가속도(m/s²)")] float m_TopplePush = 12f;
    [SerializeField, Min(0f), Tooltip("벽과 이 거리(m) 안이면 닿은 것으로 본다")] float m_ContactGap = 0.04f;
    [SerializeField, Min(0f), Tooltip("쓰러진 키 큰 소품의 회전 감쇠. 크게 하면 벽에 밀려도 데굴데굴 굴러 다시 서지 않고 바닥에서 밀린다")] float m_FallenAngularDamping = 8f;

    static readonly HashSet<Rigidbody> s_Bodies = new HashSet<Rigidbody>();

    Rigidbody[] m_Bodies;
    Vector3[] m_StartPositions;
    Quaternion[] m_StartRotations;
    bool[] m_Tall;
    float[] m_StartAngularDamping;
    Vector3[] m_LastWallPositions;
    bool[] m_WallMoving;
    readonly List<Collider> m_ColliderBuffer = new List<Collider>();

    /// <summary>이 콜라이더가 방 소품인가. 총알 판정이 소품을 건너뛸 때 쓴다.</summary>
    public static bool IsProp(Collider collider) => collider != null && collider.attachedRigidbody != null && s_Bodies.Contains(collider.attachedRigidbody);

    void Start()
    {
        m_Bodies = GetComponentsInChildren<Rigidbody>();
        m_StartPositions = new Vector3[m_Bodies.Length];
        m_StartRotations = new Quaternion[m_Bodies.Length];
        m_Tall = new bool[m_Bodies.Length];
        m_StartAngularDamping = new float[m_Bodies.Length];
        for (var i = 0; i < m_Bodies.Length; i++)
        {
            var body = m_Bodies[i];
            m_StartAngularDamping[i] = body.angularDamping;
            var size = BodyBounds(body).size;
            m_Tall[i] = size.y > m_TallHeight && size.y > 1.5f * Mathf.Min(size.x, size.z);
            m_StartPositions[i] = body.transform.position;
            m_StartRotations[i] = body.transform.rotation;
            body.excludeLayers = m_IgnoreLayers;
            body.maxDepenetrationVelocity = m_MaxDepenetration;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.Sleep();
            s_Bodies.Add(body);
        }

        if (m_Flow != null)
            m_Flow.StateMachine.StateChanged += OnStateChanged;
        GameEvents.Raised += OnEvent;
    }

    void OnDestroy()
    {
        if (m_Flow != null)
            m_Flow.StateMachine.StateChanged -= OnStateChanged;
        GameEvents.Raised -= OnEvent;
        if (m_Bodies != null)
            foreach (var body in m_Bodies)
                s_Bodies.Remove(body);
    }

    void OnStateChanged(GameState previous, GameState next)
    {
        if (next == GameState.Seating || next == GameState.Lobby)
            ResetAll();
    }

    void OnEvent(GameEvent gameEvent)
    {
        if (gameEvent.Name == "dread_changed" && gameEvent.Count > 0)
            Kick();
    }

    void FixedUpdate()
    {
        if (m_Bodies == null)
            return;
        // 다가오는 중인 벽만 민다 (가만히 있는 벽에 기대 둔 소품은 그대로 둔다)
        if (m_Walls != null)
        {
            if (m_LastWallPositions == null || m_LastWallPositions.Length != m_Walls.Length)
            {
                m_LastWallPositions = new Vector3[m_Walls.Length];
                m_WallMoving = new bool[m_Walls.Length];
                for (var w = 0; w < m_Walls.Length; w++)
                    if (m_Walls[w] != null)
                        m_LastWallPositions[w] = WallPosition(m_Walls[w]);
            }
            for (var w = 0; w < m_Walls.Length; w++)
            {
                if (m_Walls[w] == null)
                    continue;
                var position = WallPosition(m_Walls[w]);
                // 방 가운데 쪽으로 다가올 때만 (새 게임에서 제자리로 물러날 때는 아님)
                var before = new Vector2(m_LastWallPositions[w].x, m_LastWallPositions[w].z).magnitude;
                var now = new Vector2(position.x, position.z).magnitude;
                m_WallMoving[w] = before - now > 1e-5f;
                m_LastWallPositions[w] = position;
            }
        }

        for (var i = 0; i < m_Bodies.Length; i++)
        {
            var body = m_Bodies[i];
            if (body == null)
                continue;
            if (body.position.y < m_FallLimit)
                ResetBody(i);
            else
                PushOver(body, m_Tall[i]);
            // 쓰러진 키 큰 소품은 더 구르지 않게 한다
            if (m_Tall[i])
                body.angularDamping = Vector3.Angle(body.transform.up, Vector3.up) > 70f ? m_FallenAngularDamping : m_StartAngularDamping[i];
        }
    }

    // 키 큰 소품이나 다른 소품 위에 올라간 소품이 아직 서 있고 다가오는 벽에 닿아 있으면, 윗부분을 방 쪽으로 밀어 넘어뜨린다
    void PushOver(Rigidbody body, bool tall)
    {
        // 넘어가기 시작하면 더 밀지 않는다 (계속 밀면 쓰러진 뒤에도 굴러서 거꾸로 선다)
        if (m_Walls == null || Vector3.Angle(body.transform.up, Vector3.up) > 15f)
            return;

        var bounds = BodyBounds(body);
        if (!tall && bounds.min.y < m_RaisedHeight)
            return;
        for (var w = 0; w < m_Walls.Length; w++)
        {
            var wall = m_Walls[w];
            if (wall == null || !m_WallMoving[w])
                continue;
            // 벽에서 방 가운데 쪽 방향과 벽 안쪽 면
            var toCenter = -new Vector3(wall.bounds.center.x, 0f, wall.bounds.center.z).normalized;
            var face = Vector3.Dot(wall.bounds.center, toCenter) + Vector3.Dot(wall.bounds.extents, Abs(toCenter));
            var nearest = Vector3.Dot(bounds.center, toCenter) - Vector3.Dot(bounds.extents, Abs(toCenter));
            if (nearest - face > m_ContactGap)
                continue;
            var top = bounds.center + Vector3.up * bounds.extents.y * 0.8f;
            body.AddForceAtPosition(toCenter * (m_TopplePush * body.mass), top, ForceMode.Force);
        }
    }

    static Vector3 WallPosition(Collider wall) => wall.attachedRigidbody != null ? wall.attachedRigidbody.position : wall.transform.position;

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    Bounds BodyBounds(Rigidbody body)
    {
        body.GetComponentsInChildren(m_ColliderBuffer);
        var first = true;
        var bounds = new Bounds(body.position, Vector3.zero);
        foreach (var collider in m_ColliderBuffer)
        {
            if (collider.attachedRigidbody != body)
                continue;
            if (first) { bounds = collider.bounds; first = false; }
            else bounds.Encapsulate(collider.bounds);
        }
        return bounds;
    }

    /// <summary>모든 소품을 처음 자리로 돌린다.</summary>
    public void ResetAll()
    {
        if (m_Bodies == null)
            return;
        for (var i = 0; i < m_Bodies.Length; i++)
            ResetBody(i);
    }

    void ResetBody(int index)
    {
        var body = m_Bodies[index];
        if (body == null)
            return;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = m_StartPositions[index];
        body.rotation = m_StartRotations[index];
        body.transform.SetPositionAndRotation(m_StartPositions[index], m_StartRotations[index]);
        body.Sleep();
    }

    // 쿵 할 때 소품마다 조금씩 다른 방향으로 흔든다. 불안하게 쌓인 것은 이때 무너질 수 있다
    void Kick()
    {
        foreach (var body in m_Bodies)
        {
            if (body == null || body.isKinematic)
                continue;
            var push = Random.insideUnitCircle * m_KickSpeed;
            body.WakeUp();
            body.AddForce(new Vector3(push.x, Random.Range(0f, m_KickSpeed * 0.5f), push.y), ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * m_KickSpin, ForceMode.VelocityChange);
        }
    }
}
