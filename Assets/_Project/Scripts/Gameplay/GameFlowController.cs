using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 한 판을 <see cref="GameStateMachine"/>으로 진행한다. 수치는 <see cref="TuningConfig"/>에서 읽어 각 로직에 넘긴다.
/// 흐름: 퀴즈 → (정답자 있음) 총 지급 → 사수 턴 → 사격 결과 → 결과 브리핑 → 사망 확인 → 다음 퀴즈 또는 게임 종료.
/// 사수 턴: 상대 쏘기(턴 끝), 자신 쏘기(공포탄이면 한 번 더), 여분 총알 넣고 돌리기(쏘지 않고 턴 끝). 상대·자신은 겨눈 몸으로 구분한다.
/// 총은 정답자 앞에 여분 총알과 함께 나타난다.
/// 착석 단계: 이 기기의 플레이어를 자기 의자 뒤에 세우고, 앉을 때까지 기다린다. 안내 → 경고 → 강제 착석. 앉으면 게임이 끝날 때까지 일어날 수 없다.
/// 멀티에서는 두 좌석이 모두 앉아야 넘어간다(상대 좌석은 <see cref="SetRemoteSeated"/>로 받는다). 클라이언트는 받은 상태로 자기 플레이어의 착석만 맞춘다.
/// 진행자 대사는 <see cref="HostDialogue"/>가 이벤트 이름(docs/07_event_list.md)으로 골라 자막으로 띄운다. 대사가 끝날 때까지 기다린 뒤 다음으로 넘어간다.
/// 게임 이벤트는 <see cref="GameEvents"/>로 낸다 (효과음 등이 구독). 진행 이벤트는 호스트만, 총을 직접 만지는 이벤트는 사수 기기에서 낸다.
/// 진행자 대사 사이에는 잠깐씩 쉬어 플레이어가 상황을 알아챌 시간을 둔다 (Timing 칸). 결과는 먼저 버튼 색·효과음으로 보여 주고, 진행자가 뒤이어 말한다.
/// 아무도 못 맞히면 양측 패널티로 공포 단계가 오른다.
/// 멀티: 호스트에서만 돈다. 네트워크 쪽(Net/)이 이벤트를 받아 클라이언트에 보내고, <see cref="IsLocalSeat"/>, <see cref="SubmitAnswer"/>, <see cref="SetBody"/>로 상대 입력과 몸을 넣는다.
/// </summary>
public class GameFlowController : MonoBehaviour
{
    [SerializeField, Tooltip("문제 CSV (Assets/_Project/Data/Quiz/questions.csv)")] TextAsset m_QuestionsCsv;
    [SerializeField] TuningConfig m_Config;
    [SerializeField] QuizBoard m_Board;
    [SerializeField, Tooltip("좌석 1, 2 순서")] QuizTerminal[] m_Terminals;
    [SerializeField] Gun m_Gun;
    [SerializeField, Tooltip("좌석 1, 2 순서. 정답자 앞에 총이 나타나는 위치")] Transform[] m_GunSpawnPoints;
    [SerializeField, Tooltip("총과 함께 사수 앞에 놓을 여분 총알")] SpareBullet m_SpareBulletPrefab;
    [SerializeField, Tooltip("좌석 1, 2 순서. 여분 총알을 놓는 위치 (앉은 사람 기준 단말기 왼쪽). 첫 총알이 여기, 나머지는 Transform의 -X(앉은 사람의 왼쪽)로 한 줄")] Transform[] m_BulletSpawnPoints;
    [SerializeField, Tooltip("여분 총알 사이 간격(m)")] float m_SpareBulletSpacing = 0.05f;
    [SerializeField, Tooltip("좌석 1, 2 순서. 비워 두면 그 좌석은 죽지 않는다 (지금은 좌석 1이 플레이어 몸, 좌석 2가 마네킹)")]
    Damageable[] m_Bodies;
    [SerializeField, Tooltip("진행자 대사 (비워 두면 대사 없이 진행한다)")] HostDialogue m_Host;

    [Header("Seating")]
    [SerializeField, Tooltip("이 기기의 플레이어 (XR Origin)")] SeatedPlayer m_Player;
    [SerializeField, Tooltip("좌석 1, 2 순서의 의자")] Seat[] m_Seats;
    [SerializeField, Range(1, 2), Tooltip("혼자 테스트할 때 이 기기의 플레이어 좌석. 멀티에서는 IsLocalSeat로 정해진 좌석을 쓴다")]
    int m_PlayerSeat = 1;

    [Header("Timing (대사가 더 길면 대사가 끝날 때까지 기다린다)")]
    [SerializeField, Min(0f), Tooltip("진행자 대사를 이어 말할 때 사이에 쉬는 시간(초)")] float m_LineGap = 0.4f;
    [SerializeField, Min(0f), Tooltip("모두 앉은 뒤 규칙 설명을 시작하기까지(초)")] float m_IntroLeadIn = 1f;
    [SerializeField, Min(0f), Tooltip("규칙 설명이 끝나고 첫 문제 안내까지(초)")] float m_IntroLeadOut = 1f;
    [SerializeField, Min(0f), Tooltip("\"N번째 문제다\" 대사가 끝나고 문제가 나오기까지(초)")] float m_QuestionDelay = 1f;
    [SerializeField, Min(0f), Tooltip("퀴즈가 끝나고(정답·모두 오답·시간 초과) 진행자가 말하기까지(초). 그동안 버튼 색과 효과음으로 결과를 보여 준다")] float m_AnswerLineDelay = 1f;
    [SerializeField, Min(0f), Tooltip("\"기회를 주지\" 대사가 끝나고 총이 나오기까지(초)")] float m_GunDelay = 0.5f;
    [SerializeField, Min(0f), Tooltip("총이 나오고 쏘지 않으면 진행자가 재촉하기까지(초)")] float m_UrgeDelay = 5f;
    [SerializeField, Min(1f), Tooltip("그 뒤로 재촉하는 간격(초)")] float m_UrgeInterval = 7f;
    [SerializeField, Min(0f), Tooltip("쏜 뒤 총을 치우기까지(초)")] float m_ShotResultDelay = 1f;
    [SerializeField, Min(0f), Tooltip("결과 브리핑 최소 시간(초)")] float m_HostReactionDuration = 1.5f;
    [SerializeField, Min(0f), Tooltip("양측 패널티(공포 단계가 오르는 연출) 시간(초). 쿵·암전 → 조명 → 벽이 다가옴이 끝나고 한숨 돌릴 때까지")] float m_PenaltyDuration = 8f;
    [SerializeField, Min(0f), Tooltip("게임이 끝나고 처음부터 다시 시작하기까지 최소 시간(초)")] float m_RestartDelay = 3f;

    [Header("Start")]
    [SerializeField, Tooltip("켜면 Play하자마자 시작한다 (혼자 테스트할 때). 끄면 StartGame()을 부를 때까지 기다린다 (RoomScene은 LAN 패널의 START 버튼)")]
    bool m_StartOnPlay = true;

    readonly GameStateMachine m_StateMachine = new GameStateMachine();
    QuizDeck m_Deck;
    QuizRound m_Round;
    int m_ShooterSeat;
    // 총이 지금 놓인 좌석 (클라이언트도 안다). 총을 직접 만지는 이벤트를 낼 기기를 정한다
    int m_PresentedSeat;
    Coroutine m_ShooterTimer;
    Coroutine m_SeatingRoutine;
    bool m_PressureSaid;
    // 이번 사수 턴에 실탄이 맞은 부위 (결과 브리핑에서 부위 대사를 고른다). 없으면 null
    BodyPart? m_HitPart;
    int m_HitSeat;
    // 양팔을 다 못 쓰는 사수: 총을 꺼내지 않고 바로 결과 브리핑으로 넘긴다
    bool m_SkipShooterTurn;
    // 사수 턴이 어떻게 끝났는지. 결과 브리핑에서 이 이벤트의 대사를 말한다
    string m_ShotReaction;
    bool m_ReloadedThisTurn;
    // 공포 단계 (0 ~ 최대). 아무도 못 맞히면 오르고, 새 게임에서 0으로 돌아간다
    int m_DreadLevel;
    // 이번 게임에서 몇 번째 문제인지 ("N번째 문제다" 대사)
    int m_QuestionNumber;
    readonly List<SpareBullet> m_SpareBullets = new List<SpareBullet>();
    // 호스트: 상대 기기가 앉았다고 알려 온 좌석
    readonly HashSet<int> m_RemoteSeated = new HashSet<int>();

    /// <summary>상태 변경을 구독하려는 쪽(조명·사운드 등)이 쓴다.</summary>
    public GameStateMachine StateMachine => m_StateMachine;

    /// <summary>지금 공포 단계 (진행하는 기기 기준). 바뀌면 <c>dread_changed</c> 이벤트가 난다.</summary>
    public int DreadLevel => m_DreadLevel;

    /// <summary>첫 게임을 시작했는지. 시작한 뒤에는 게임이 끝나도 자동으로 다시 시작한다.</summary>
    public bool HasStarted { get; private set; }

    /// <summary>문제를 띄웠을 때 (문제, 제한시간 초). 네트워크 쪽이 클라이언트 화면에 같은 문제를 띄울 때 쓴다.</summary>
    public event Action<QuizQuestion, float> QuizShown;

    /// <summary>퀴즈가 끝나서 남은 시간 줄이 멈췄을 때.</summary>
    public event Action QuizEnded;

    /// <summary>단말기 버튼에 판정 색을 표시했을 때 (좌석, 선택지, 정답 여부).</summary>
    public event Action<int, int, bool> ButtonResultShown;

    /// <summary>사수 앞에 총을 꺼냈을 때 (사수 좌석).</summary>
    public event Action<int> GunPresented;

    /// <summary>총을 치웠을 때.</summary>
    public event Action GunWithdrawn;

    /// <summary>호스트: 상대 기기 좌석을 강제로 앉혀야 할 때 (좌석). 네트워크 쪽이 그 기기에 보내 <see cref="ForceLocalSeat"/>를 부르게 한다.</summary>
    public event Action<int> SeatForced;

    /// <summary>좌석의 몸이 맞았을 때 (좌석, 부위). 네트워크 쪽이 클라이언트의 같은 몸에 똑같이 적용한다.</summary>
    public event Action<int, BodyPart> SeatHit;

    /// <summary>새 게임을 위해 모든 몸을 처음 상태로 되돌렸을 때.</summary>
    public event Action BodiesRevived;

    /// <summary>
    /// 이 기기에서 직접 플레이하는 좌석인지. 비워 두면(null) 모든 좌석을 이 기기에서 쓴다 (혼자 테스트).
    /// 멀티에서는 네트워크 쪽이 정한다: 이 기기의 단말기 입력은 자기 좌석만 받고, 총과 여분 총알은 사수가 이 기기 사람일 때만 잡을 수 있다.
    /// </summary>
    public Func<int, bool> IsLocalSeat { get; set; }

    /// <summary>지금 사수 좌석. 사수 턴이 아니면 의미 없다.</summary>
    public int ShooterSeat => m_ShooterSeat;

    /// <summary>
    /// true면 이 기기가 게임을 진행한다(혼자 플레이, 멀티의 호스트). false면 상태 머신은 네트워크로 받은 호스트 상태를 비추기만 하고
    /// 출제·총 지급 같은 진행은 하지 않는다(멀티의 클라이언트). 상태 변경 이벤트는 양쪽 모두에서 난다.
    /// </summary>
    public bool DrivesGame { get; set; } = true;

    // ── 소리 위치 (EventSoundPlayer가 쓴다) ──

    /// <summary>좌석의 의자. 없으면 null.</summary>
    public Transform SeatTransform(int seat) => SeatAt(seat) != null ? SeatAt(seat).transform : null;

    /// <summary>좌석의 단말기. 없으면 null.</summary>
    public Transform TerminalTransform(int seat) =>
        m_Terminals != null && seat >= 1 && seat <= m_Terminals.Length && m_Terminals[seat - 1] != null ? m_Terminals[seat - 1].transform : null;

    /// <summary>퀴즈 화면들의 가운데. 화면이 없으면 null.</summary>
    public Vector3? QuizScreenPosition => m_Board != null ? m_Board.Center : (Vector3?)null;

    public Transform GunTransform => m_Gun != null ? m_Gun.transform : null;
    public Transform HostTransform => m_Host != null ? m_Host.Speaker : null;

    /// <summary>좌석의 몸에서 머리 히트박스. 없으면 null (사수 턴의 대상 조명이 비춘다).</summary>
    public Transform HeadOf(int seat)
    {
        var body = BodyOf(seat);
        if (body == null)
            return null;
        foreach (var hitbox in body.GetComponentsInChildren<Hitbox>(true))
            if (hitbox.Part == BodyPart.Head)
                return hitbox.transform;
        return null;
    }

    /// <summary>사수 턴 제한시간(초). 배경음이 고조되는 속도에 쓴다.</summary>
    public float ShooterTurnTimeLimit => m_Config != null ? m_Config.ShooterTurnTimeLimit : 20f;

    static double Now => Time.timeAsDouble;

    void Awake() => m_StateMachine.StateChanged += OnStateChanged;

    void Start()
    {
        if (m_QuestionsCsv == null || m_Config == null || m_Board == null || m_Gun == null)
        {
            Debug.LogWarning("[Flow] CSV, TuningConfig, QuizBoard, Gun 중 연결되지 않은 것이 있다", this);
            enabled = false;
            return;
        }

        var warnings = new List<string>();
        var questions = QuizCsvParser.Parse(m_QuestionsCsv.text, warnings);
        foreach (var warning in warnings)
            Debug.LogWarning($"[Flow] {m_QuestionsCsv.name}.csv: {warning}", m_QuestionsCsv);
        Debug.Log($"[Flow] {m_QuestionsCsv.name}.csv에서 문제 {questions.Count}개를 불러왔다", this);

        m_Deck = new QuizDeck(questions);
        if (m_Deck.Count == 0)
        {
            enabled = false;
            return;
        }

        m_Gun.Withdraw();
        // 생존자에게만 하는 말 등은 그 좌석의 기기에서만 띄운다
        if (m_Host != null)
            m_Host.IsLocalSeat = seat => seat == LocalSeat;
        if (m_StartOnPlay)
            StartGame();
    }

    /// <summary>첫 게임을 시작한다. 이미 시작했거나 준비가 안 됐으면 무시한다.</summary>
    public void StartGame()
    {
        if (HasStarted || !enabled || m_Deck == null)
            return;

        HasStarted = true;
        BeginGame();
    }

    void OnEnable()
    {
        foreach (var terminal in m_Terminals)
            if (terminal != null)
                terminal.AnswerPressed += OnAnswerPressed;
        if (m_Gun != null)
        {
            m_Gun.Fired += OnGunFired;
            m_Gun.CylinderClosed += OnCylinderClosed;
            m_Gun.Grabbed += OnGunGrabbed;
            m_Gun.Dropped += OnGunDropped;
            m_Gun.ReturnedHome += OnGunReturned;
            m_Gun.CylinderOpened += OnCylinderOpened;
            m_Gun.BulletInserted += OnBulletInserted;
            m_Gun.HammerCocked += OnHammerCocked;
        }
        if (m_Bodies != null)
            foreach (var body in m_Bodies)
                Watch(body);
    }

    void OnDisable()
    {
        foreach (var terminal in m_Terminals)
            if (terminal != null)
                terminal.AnswerPressed -= OnAnswerPressed;
        if (m_Gun != null)
        {
            m_Gun.Fired -= OnGunFired;
            m_Gun.CylinderClosed -= OnCylinderClosed;
            m_Gun.Grabbed -= OnGunGrabbed;
            m_Gun.Dropped -= OnGunDropped;
            m_Gun.ReturnedHome -= OnGunReturned;
            m_Gun.CylinderOpened -= OnCylinderOpened;
            m_Gun.BulletInserted -= OnBulletInserted;
            m_Gun.HammerCocked -= OnHammerCocked;
        }
        foreach (var body in new List<Damageable>(m_BodyHandlers.Keys))
            Unwatch(body);
    }

    void Update()
    {
        if (m_StateMachine.Current != GameState.QuizActive || m_Round == null || m_Round.IsResolved)
            return;

        m_Round.Tick(Now);
        if (m_Round.IsResolved)
            return;

        var ratio = m_Round.GetRemainingRatio(Now);
        m_Board.SetRemainingTime(ratio, ratio * m_Round.TimeLimit);
        // 남은 시간이 정한 초가 되면 한 번만 압박한다 (째깍 소리가 빨라지는 때와 같다)
        var secondsLeft = ratio * m_Round.TimeLimit;
        if (!m_PressureSaid && m_Config.PressureLineInterval > 0f && secondsLeft <= m_Config.PressureLineInterval)
        {
            m_PressureSaid = true;
            GameEvents.Raise("quiz_pressure", count: Mathf.CeilToInt(secondsLeft));
            Say("quiz_pressure");
        }
    }

    // ── 상태 이동 ──

    void GoTo(GameState next)
    {
        if (!m_StateMachine.TryTransitionTo(next))
            Debug.LogError($"[Flow] {m_StateMachine.Current} → {next} 이동이 허용되지 않는다", this);
    }

    void OnStateChanged(GameState previous, GameState next)
    {
        Debug.Log($"[Flow] {previous} → {next}", this);

        // 새 문제 안내 동안에는 지난 문제를 지운다 (클라이언트도 상태를 받아 같이 지운다)
        if (next == GameState.QuizActive)
        {
            m_Board.Clear();
            foreach (var terminal in m_Terminals)
                if (terminal != null)
                    terminal.ClearResults();
        }

        // 클라이언트는 호스트의 상태를 따라갈 뿐 진행을 돌리지 않는다. 자기 플레이어의 착석만 상태에 맞춘다
        if (!DrivesGame)
        {
            MirrorSeating(next);
            return;
        }

        switch (next)
        {
            case GameState.QuizActive:
                StartCoroutine(StartQuiz());
                break;
            case GameState.GunHandover:
                HandOverGun();
                break;
            case GameState.ShooterTurn:
                if (m_SkipShooterTurn)
                {
                    m_SkipShooterTurn = false;
                    StartCoroutine(ResolveShot());
                }
                else
                {
                    m_ShooterTimer = StartCoroutine(ShooterTimeout());
                }
                break;
        }
    }

    // 클라이언트: 착석 단계면 자기 의자 뒤에 서고, 규칙 설명부터는 의자에 고정되고, 게임이 끝나면 풀린다
    void MirrorSeating(GameState next)
    {
        if (m_Player == null)
            return;

        switch (next)
        {
            case GameState.Seating:
                m_Player.UnlockSeat();
                Debug.Log($"[Flow] 클라이언트 착석 단계: 좌석 {LocalSeatNumber}, {(IsLocalPlayerOnOwnSeat ? "이미 앉아 있어서 그대로 둔다" : "의자 뒤에 세운다")}", this);
                if (IsLocalPlayerOnOwnSeat)
                    RestrictToOwnSeat();
                else
                    PlaceLocalPlayerBehindSeat();
                break;
            case GameState.Intro:
                if (m_Player.CurrentSeat != null)
                    m_Player.LockSeat();
                break;
            case GameState.GameOver:
                m_Player.UnlockSeat();
                break;
        }
    }

    void BeginGame()
    {
        // 중상만 입은 몸도 처음 상태로 되돌린다
        foreach (var body in m_Bodies)
            if (body != null && body.HasInjury)
                body.Revive();
        BodiesRevived?.Invoke();
        m_Gun.ResetRevolver();
        m_DreadLevel = 0;
        m_QuestionNumber = 0;

        GoTo(GameState.Seating);
        m_SeatingRoutine = StartCoroutine(Seating());
    }

    // ── 착석 ──

    /// <summary>이 기기의 플레이어 좌석. 멀티면 네트워크 쪽이 정한 좌석, 혼자면 Player Seat 칸의 좌석.</summary>
    public int LocalSeat => LocalSeatNumber;

    int LocalSeatNumber
    {
        get
        {
            if (IsLocalSeat != null && m_Seats != null)
                for (var seat = 1; seat <= m_Seats.Length; seat++)
                    if (IsLocalSeat(seat))
                        return seat;

            // 멀티의 클라이언트는 게임 시작 알림보다 상태(Seating)를 먼저 받을 수 있다. 접속할 때 배정된 의자로 좌석을 안다
            if (m_Player != null && m_Player.AssignedSeat != null && m_Seats != null)
            {
                var index = Array.IndexOf(m_Seats, m_Player.AssignedSeat);
                if (index >= 0)
                    return index + 1;
            }
            return m_PlayerSeat;
        }
    }

    Seat SeatAt(int seatNumber) => m_Seats != null && seatNumber >= 1 && seatNumber <= m_Seats.Length ? m_Seats[seatNumber - 1] : null;

    // 착석을 기다릴 좌석. 혼자면 이 기기의 좌석만, 멀티면 모든 좌석 (상대 좌석은 상대 기기가 알려 준다)
    List<int> SeatsToWaitFor()
    {
        var seats = new List<int>();
        if (IsLocalSeat == null)
        {
            seats.Add(LocalSeatNumber);
            return seats;
        }

        for (var seat = 1; seat <= m_Seats.Length; seat++)
            seats.Add(seat);
        return seats;
    }

    bool IsSeated(int seatNumber)
    {
        if (IsLocalSeat == null || IsLocalSeat(seatNumber))
            return m_Player.CurrentSeat != null && m_Player.CurrentSeat == SeatAt(seatNumber);
        return m_RemoteSeated.Contains(seatNumber);
    }

    /// <summary>호스트: 상대 기기의 플레이어가 자기 의자에 앉았는지 (네트워크 쪽이 알려 준다).</summary>
    public void SetRemoteSeated(int seatNumber, bool seated)
    {
        if (seated)
            m_RemoteSeated.Add(seatNumber);
        else
            m_RemoteSeated.Remove(seatNumber);
    }

    // 이 기기의 플레이어를 자기 의자 뒤에 세운다. 다른 좌석의 의자는 앉을 수 없다
    void PlaceLocalPlayerBehindSeat()
    {
        var seat = SeatAt(LocalSeatNumber);
        if (m_Player == null || seat == null)
            return;

        RestrictToOwnSeat();
        m_Player.PlaceBehind(seat);
    }

    // 자기 의자만 앉을 수 있게 한다
    void RestrictToOwnSeat()
    {
        var seat = SeatAt(LocalSeatNumber);
        for (var i = 0; i < m_Seats.Length; i++)
            if (m_Seats[i] != null)
                m_Seats[i].enabled = m_Seats[i] == seat;
    }

    // 게임을 시작할 때 이미 자기 의자에 앉아 있으면 일으켜 세우지 않고 그대로 착석으로 친다
    bool IsLocalPlayerOnOwnSeat => m_Player != null && m_Player.CurrentSeat != null && m_Player.CurrentSeat == SeatAt(LocalSeatNumber);

    /// <summary>이 기기의 플레이어를 자기 의자에 강제로 앉히고 고정한다. 멀티의 클라이언트는 호스트가 시킬 때 부른다.</summary>
    public void ForceLocalSeat()
    {
        var seat = SeatAt(LocalSeatNumber);
        if (m_Player == null || seat == null)
            return;

        if (m_Player.CurrentSeat != seat)
            m_Player.SitOn(seat);
        m_Player.LockSeat();
    }

    IEnumerator Seating()
    {
        // 상대 좌석의 착석 여부는 지우지 않는다. 상대 기기는 바뀔 때만 알려 주므로, 이미 앉아 있던 상대는 그대로 앉은 것으로 둔다
        if (m_Player == null || SeatAt(LocalSeatNumber) == null)
        {
            Debug.LogWarning("[Flow] 플레이어나 의자가 연결되지 않아 착석 단계를 건너뛴다", this);
        }
        else
        {
            // 이미 자기 의자에 앉아 있으면(로비에서 앉았거나 지난 게임에서 앉은 채) 그대로 둔다
            if (IsLocalPlayerOnOwnSeat)
                RestrictToOwnSeat();
            else
                PlaceLocalPlayerBehindSeat();

            var waiting = SeatsToWaitFor();
            if (waiting.Exists(seat => !IsSeated(seat)))
            {
                GameEvents.Raise("seat_prompt");
                Say("seat_prompt");
            }

            // 모두 앉을 때까지 기다린다. 안내 → 경고 → 강제 착석
            var warned = false;
            var start = Time.time;
            while (waiting.Count > 0)
            {
                for (var i = waiting.Count - 1; i >= 0; i--)
                {
                    if (!IsSeated(waiting[i]))
                        continue;
                    GameEvents.Raise("player_seated", waiting[i]);
                    if (waiting[i] == LocalSeatNumber)
                        m_Player.LockSeat();
                    waiting.RemoveAt(i);
                }
                if (waiting.Count == 0)
                    break;

                var elapsed = Time.time - start;
                if (!warned && elapsed >= m_Config.SeatingWarningDelay)
                {
                    warned = true;
                    GameEvents.Raise("seat_warning", waiting[0], count: waiting.Count);
                    Say("seat_warning");
                }
                else if (warned && elapsed >= m_Config.SeatingWarningDelay + m_Config.ForceSeatDelay)
                {
                    foreach (var seatNumber in waiting)
                    {
                        GameEvents.Raise("seat_forced", seatNumber);
                        // 강제로 앉혀도 앉은 것이다. 앉는 소리도 같다
                        GameEvents.Raise("player_seated", seatNumber);
                        if (IsLocalSeat == null || IsLocalSeat(seatNumber))
                            ForceLocalSeat();
                        else
                            SeatForced?.Invoke(seatNumber);
                    }
                    waiting.Clear();
                    yield return new WaitForSeconds(Say("seat_forced"));
                    break;
                }

                yield return null;
            }

            // 앉으면 게임이 끝날 때까지 일어날 수 없다 (상대 기기는 Intro 상태를 받으면 스스로 고정한다)
            m_Player.LockSeat();
        }

        m_SeatingRoutine = null;
        m_Board.Clear();

        // 규칙 설명: 잠깐 쉬고, 진행자가 규칙 대사를 CSV 순서대로 하나씩 말한 뒤 첫 문제로 넘어간다
        GoTo(GameState.Intro);
        yield return new WaitForSeconds(m_IntroLeadIn);
        GameEvents.Raise("rules_intro");
        var ruleLines = m_Host != null ? m_Host.LineCount("rules_intro") : 0;
        for (var i = 0; i < ruleLines; i++)
            yield return Wait(m_Host.SayAt("rules_intro", i));
        yield return new WaitForSeconds(m_IntroLeadOut);
        GoTo(GameState.QuizActive);
    }

    /// <summary>진행자가 이 이벤트의 대사를 말한다. 다 읽는 데 걸리는 시간(초)을 돌려준다 (대사가 없으면 0).</summary>
    float Say(string eventName, int bullets = -1, string order = null, int audienceSeat = 0) =>
        m_Host != null ? m_Host.Say(eventName, bullets, order, audienceSeat) : 0f;

    // 대사가 끝나고 다음 대사 전에 조금 쉰다
    WaitForSeconds Wait(float lineDuration) => new WaitForSeconds(lineDuration > 0f ? lineDuration + m_LineGap : 0f);

    // 대사를 말하고 끝날 때까지 기다린다
    IEnumerator SayAndWait(string eventName, int bullets = -1, string order = null, int audienceSeat = 0)
    {
        yield return Wait(Say(eventName, bullets, order, audienceSeat));
    }

    static readonly string[] k_Ordinals =
    {
        "첫", "두", "세", "네", "다섯", "여섯", "일곱", "여덟", "아홉", "열",
        "열한", "열두", "열세", "열네", "열다섯", "열여섯", "열일곱", "열여덟", "열아홉", "스무",
    };

    // 1 → "첫 번째", 2 → "두 번째" ... 20을 넘으면 "21번째"
    static string Ordinal(int number) => number >= 1 && number <= k_Ordinals.Length ? $"{k_Ordinals[number - 1]} 번째" : $"{number}번째";

    // ── 퀴즈 ──

    // 진행자가 몇 번째 문제인지 말하고, 잠깐 뒤 문제를 낸다
    IEnumerator StartQuiz()
    {
        m_QuestionNumber++;
        yield return new WaitForSeconds(Say("quiz_intro", order: Ordinal(m_QuestionNumber)));
        yield return new WaitForSeconds(m_QuestionDelay);
        if (m_StateMachine.Current != GameState.QuizActive)
            yield break;

        // 공포 단계의 비율대로 난이도를 뽑아 그 난이도에서 고르고, 제한시간도 공포 단계를 따른다
        var question = m_Deck.DrawWeighted(m_Config.GetQuizDifficultyWeights(m_DreadLevel), out var reshuffled);
        if (reshuffled && question != null)
            Debug.Log($"[Flow] 난이도 {question.Difficulty} 문제를 다 써서 그 난이도를 처음부터 다시 낸다", this);

        var timeLimit = m_Config.GetQuizTimeLimit(m_DreadLevel);
        m_Round = new QuizRound(question, Now, timeLimit);
        m_Round.AnswerWrong += OnAnswerWrong;
        m_Round.Resolved += OnQuizResolved;

        m_Board.Show(question);
        QuizShown?.Invoke(question, timeLimit);
        GameEvents.Raise("quiz_start", count: Mathf.RoundToInt(timeLimit));
        m_PressureSaid = false;
        Debug.Log($"[Flow] {question.Id} 출제: {question.Question} (정답 {Letter(question.AnswerIndex)}, {timeLimit}초, 난이도 {question.Difficulty}, 공포 단계 {m_DreadLevel})", this);
    }

    // 이 기기의 단말기 입력
    void OnAnswerPressed(int seat, int choiceIndex)
    {
        if (IsLocalSeat != null && !IsLocalSeat(seat))
            return;

        GameEvents.Raise(new GameEvent("button_pressed", seat, count: choiceIndex) { LocalOnly = true });

        SubmitAnswer(seat, choiceIndex);
    }

    /// <summary>답을 낸다. 이 기기의 단말기 입력과, 네트워크로 받은 상대의 입력이 모두 여기로 온다.</summary>
    public void SubmitAnswer(int seat, int choiceIndex) => SubmitAnswer(seat, choiceIndex, Now);

    /// <param name="pressTime">누른 시각 (이 기기의 <see cref="Time.timeAsDouble"/> 기준). 네트워크로 받은 입력은 네트워크 시간을 이 기준으로 바꿔서 넘긴다</param>
    public void SubmitAnswer(int seat, int choiceIndex, double pressTime)
    {
        if (m_StateMachine.Current != GameState.QuizActive || m_Round == null)
            return;

        if (m_Round.Submit(seat, choiceIndex, pressTime) == QuizSubmitResult.Ignored)
            Debug.Log($"[Flow] 좌석 {seat}의 {Letter(choiceIndex)} 무시 (이 문제에서 잠김이거나 시간 초과)", this);
    }

    void OnAnswerWrong(int seat, int choiceIndex)
    {
        Debug.Log($"[Flow] 좌석 {seat} 오답 {Letter(choiceIndex)} → 이 문제에서 잠김", this);
        ShowButtonResult(seat, choiceIndex, false);
        // 오답은 대사 없이 버튼 색과 효과음으로 알린다
        GameEvents.Raise("answer_wrong", seat, count: choiceIndex);
    }

    // 결과는 바로 버튼 색과 효과음으로 보여 주고, 진행자는 잠깐 뒤에 말한다
    void OnQuizResolved(QuizResult result)
    {
        QuizEnded?.Invoke();
        switch (result.Outcome)
        {
            case QuizOutcome.Correct:
                Debug.Log($"[Flow] 좌석 {result.WinnerSeat} 정답", this);
                ShowButtonResult(result.WinnerSeat, m_Round.Question.AnswerIndex, true);
                GameEvents.Raise("answer_correct", result.WinnerSeat);
                break;
            case QuizOutcome.Timeout:
                Debug.Log("[Flow] 시간 초과 → 정답자 없음", this);
                GameEvents.Raise("quiz_timeout");
                break;
            case QuizOutcome.AllWrong:
                Debug.Log("[Flow] 둘 다 오답 → 정답자 없음", this);
                break;
        }

        StartCoroutine(AfterQuiz(result));
    }

    IEnumerator AfterQuiz(QuizResult result)
    {
        GoTo(GameState.QuizResolve);
        yield return new WaitForSeconds(m_AnswerLineDelay);

        if (result.HasWinner)
        {
            // "정답이다." → "너에게 기회를 주지." → 잠깐 뒤 총. 양팔을 다 못 쓰면 총을 쥘 수 없어서 기회가 넘어간다
            m_ShooterSeat = result.WinnerSeat;
            m_SkipShooterTurn = HasNoHands(m_ShooterSeat);
            yield return SayAndWait("answer_correct");
            if (!m_SkipShooterTurn)
            {
                yield return SayAndWait("gun_offer");
                yield return new WaitForSeconds(m_GunDelay);
            }
            GoTo(GameState.GunHandover);
        }
        else
        {
            // "모두 오답이다." 또는 "답을 고르지 못했군." → "자, 벌을 내릴 시간이다." → 공포 단계 상승
            yield return SayAndWait(result.Outcome == QuizOutcome.AllWrong ? "quiz_all_wrong" : "quiz_timeout");
            yield return SayAndWait("penalty_announce");
            yield return BothFailPenalty();
        }
    }

    // 양측 패널티: 아무도 못 맞히면 공포 단계가 하나 오른다(최대에서는 그대로).
    // 조명·벽·배경음(DreadAtmosphere)과 제한시간·난이도가 이 단계를 따른다
    IEnumerator BothFailPenalty()
    {
        GoTo(GameState.BothFailPenalty);
        if (m_DreadLevel < m_Config.MaxDreadLevel)
        {
            m_DreadLevel++;
            Debug.Log($"[Flow] 아무도 못 맞힘 → 공포 단계 {m_DreadLevel}", this);
            GameEvents.Raise("dread_changed", count: m_DreadLevel);
            yield return new WaitForSeconds(m_PenaltyDuration);
        }

        GoTo(GameState.DeathCheck);
        GoTo(GameState.QuizActive);
    }

    // ── 총 ──

    void HandOverGun()
    {
        m_HitPart = null;
        m_ReloadedThisTurn = false;
        if (m_SkipShooterTurn)
        {
            Debug.Log($"[Flow] 좌석 {m_ShooterSeat}: 양팔을 못 써서 총을 꺼내지 않는다", this);
            m_ShotReaction = "shooter_no_hands";
            GoTo(GameState.ShooterTurn);
            return;
        }

        if (!PresentGunForSeat(m_ShooterSeat))
            return;

        m_ShotReaction = null;
        GameEvents.Raise("gun_handover", m_ShooterSeat, 3 - m_ShooterSeat, m_Gun.BulletCount);
        Say("gun_handover", m_Gun.BulletCount);
        Debug.Log($"[Flow] 좌석 {m_ShooterSeat}에게 총 지급 (총알 {m_Gun.BulletCount}발, 여분 {m_SpareBullets.Count}발)", this);
        GunPresented?.Invoke(m_ShooterSeat);
        GoTo(GameState.ShooterTurn);
    }

    /// <summary>
    /// 사수 앞에 총과 여분 총알을 꺼낸다. 사수 좌석이 이 기기의 좌석이 아니면(<see cref="IsLocalSeat"/>) 잡을 수 없게 둔다.
    /// 진행을 돌리지 않는 네트워크 클라이언트는 호스트의 알림을 받아 이것만 부른다.
    /// </summary>
    public bool PresentGunForSeat(int seat)
    {
        var spawn = m_GunSpawnPoints != null && seat >= 1 && seat <= m_GunSpawnPoints.Length ? m_GunSpawnPoints[seat - 1] : null;
        if (spawn == null)
        {
            Debug.LogError($"[Flow] 좌석 {seat}의 총 지급 위치가 없다", this);
            return false;
        }

        var grabbable = IsLocalSeat == null || IsLocalSeat(seat);
        m_PresentedSeat = seat;
        m_Gun.Present(spawn);
        m_Gun.SetGrabbable(grabbable);
        var bulletSpawn = m_BulletSpawnPoints != null && seat <= m_BulletSpawnPoints.Length ? m_BulletSpawnPoints[seat - 1] : null;
        if (bulletSpawn == null)
            Debug.LogError($"[Flow] 좌석 {seat}의 여분 총알 위치가 없다", this);
        else
            SpawnSpareBullets(bulletSpawn, grabbable);
        return true;
    }

    /// <summary>총과 남은 여분 총알을 치운다.</summary>
    public void WithdrawGun()
    {
        m_Gun.Withdraw();
        ClearSpareBullets();
    }

    /// <summary>테이블에 남아 있는 여분 총알 수.</summary>
    public int SpareBulletsLeft
    {
        get
        {
            var count = 0;
            foreach (var bullet in m_SpareBullets)
                if (bullet != null && !bullet.IsConsumed)
                    count++;
            return count;
        }
    }

    /// <summary>사수가 아닌 기기: 사수 기기에 남은 여분 총알 수에 맞춰 넣은 만큼 치운다.</summary>
    public void SyncSpareBulletsLeft(int left)
    {
        var excess = SpareBulletsLeft - left;
        for (var i = m_SpareBullets.Count - 1; i >= 0 && excess > 0; i--)
        {
            var bullet = m_SpareBullets[i];
            if (bullet == null || bullet.IsConsumed)
                continue;
            bullet.Consume();
            excess--;
        }
    }

    void SpawnSpareBullets(Transform spawn, bool grabbable)
    {
        ClearSpareBullets();
        if (m_SpareBulletPrefab == null)
            return;

        // 여분 총알 위치에서 테이블 바깥쪽(위치의 -X, 앉은 사람의 왼쪽)으로 한 줄로 세워 둔다
        var outward = -spawn.right;
        for (var i = 0; i < m_Config.SpareBullets; i++)
        {
            var position = spawn.position + outward * (i * m_SpareBulletSpacing) + Vector3.up * 0.03f;
            var bullet = Instantiate(m_SpareBulletPrefab, position, Quaternion.identity);
            bullet.SetGun(m_Gun);
            bullet.SetGrabbable(grabbable);
            m_SpareBullets.Add(bullet);
        }
    }

    void ClearSpareBullets()
    {
        foreach (var bullet in m_SpareBullets)
            if (bullet != null && !bullet.IsConsumed)
                Destroy(bullet.gameObject);
        m_SpareBullets.Clear();
    }

    // 넣은 총알이 있는 채로 실린더를 닫으면 쏘지 않고 턴이 끝난다
    void OnCylinderClosed(int inserted)
    {
        RaiseFromShooter("cylinder_spun", inserted);
        if (!DrivesGame || m_StateMachine.Current != GameState.ShooterTurn || inserted == 0)
            return;

        Debug.Log($"[Flow] 좌석 {m_ShooterSeat}: 총알 {inserted}발을 넣고 돌렸다 → 턴 종료 (총알 {m_Gun.BulletCount}발)", this);
        m_ShotReaction = "cylinder_spun";
        EndShooterTurn();
    }

    // 사수 턴 시간을 잰다. 쏘지 않고 있으면 진행자가 재촉하고, 시간이 다 되면 총을 거둔다
    IEnumerator ShooterTimeout()
    {
        var end = Time.time + m_Config.ShooterTurnTimeLimit;
        var nextUrge = Time.time + m_UrgeDelay;
        while (Time.time < end)
        {
            if (Time.time >= nextUrge && end - Time.time > 2f)
            {
                Say("shooter_urge");
                nextUrge = Time.time + m_UrgeInterval;
            }
            yield return null;
        }

        m_ShooterTimer = null;
        Debug.Log($"[Flow] 사수 턴 {m_Config.ShooterTurnTimeLimit}초가 지나 총을 반납한다", this);
        m_ShotReaction = "shooter_timeout";
        GameEvents.Raise("shooter_timeout", m_ShooterSeat);
        StartCoroutine(ResolveShot());
    }

    void OnGunFired(Gun.ShotResult shot)
    {
        // 클라이언트는 호스트가 보낸 결과로 이펙트만 보여 준다. 턴 진행은 호스트가 한다
        if (!DrivesGame || m_StateMachine.Current != GameState.ShooterTurn)
            return;

        // 자기 몸을 겨눴으면 자신 쏘기, 상대 몸이나 아무 데도 아니면 상대 쏘기
        var shooterBody = BodyOf(m_ShooterSeat);
        var self = shot.HitBox != null && shooterBody != null && shot.HitBox.Owner == shooterBody;
        Debug.Log($"[Flow] 좌석 {m_ShooterSeat}: {(self ? "자신 쏘기" : "상대 쏘기")}, {(shot.IsLive ? "실탄" : "공포탄")}", this);

        if (self && !shot.IsLive)
        {
            // 한 번 더 쏘기 때문에 결과 브리핑을 기다리지 않고 바로 말한다
            Debug.Log("[Flow] 자신을 쐈는데 공포탄 → 한 번 더 쏜다", this);
            GameEvents.Raise("shot_blank_self", m_ShooterSeat);
            GameEvents.Raise("shoot_again", m_ShooterSeat);
            Say("shot_blank_self");
            return;
        }

        if (shot.IsLive && shot.HitBox != null)
        {
            m_HitPart = shot.HitBox.Part;
            m_HitSeat = Array.IndexOf(m_Bodies, shot.HitBox.Owner) + 1;
        }
        if (shot.IsLive)
            m_ShotReaction = self ? "shot_hit_self" : shot.HitBox != null ? "shot_hit_opponent" : "shot_miss";
        else
            m_ShotReaction = "shot_blank_opponent";
        RaiseShot(m_ShotReaction, shot);
        m_ReloadedThisTurn = shot.Reloaded;
        EndShooterTurn();
    }

    void EndShooterTurn()
    {
        m_Gun.IsTriggerLocked = true;
        if (m_ShooterTimer != null)
        {
            StopCoroutine(m_ShooterTimer);
            m_ShooterTimer = null;
        }

        StartCoroutine(ResolveShot());
    }

    IEnumerator ResolveShot()
    {
        m_Gun.IsTriggerLocked = true;
        GoTo(GameState.ShotResolve);
        yield return new WaitForSeconds(m_ShotResultDelay);
        WithdrawGun();
        GunWithdrawn?.Invoke();

        // 결과 브리핑: 사격 결과(또는 총알 넣기·시간 초과)에 반응하고, 진행자가 장전했으면 바뀐 총알 수를 알려 준다.
        // 누가 죽었으면 장전 안내는 건너뛰고 바로 사망으로 넘어간다 (게임이 끝나므로 총알 수는 의미가 없다)
        GoTo(GameState.HostReaction);
        var reaction = m_ShotReaction;
        var bullets = m_Gun.BulletCount;
        var lineDuration = reaction != null ? Say(reaction, bullets) : 0f;
        yield return new WaitForSeconds(Mathf.Max(m_HostReactionDuration, lineDuration + m_LineGap));
        var deadSeat = FindDeadSeat();

        // 맞고 살아 있으면 어디를 맞았고 무엇이 바뀌었는지 알려 준다
        var hitBody = BodyOf(m_HitSeat);
        if (m_HitPart.HasValue && hitBody != null && !hitBody.IsDead)
            yield return Wait(Say(HitPartLine(m_HitPart.Value, hitBody)));
        if (m_ReloadedThisTurn && deadSeat == 0)
        {
            GameEvents.Raise("reload", count: bullets);
            yield return Wait(Say("reload", bullets));
        }

        GoTo(GameState.DeathCheck);
        if (deadSeat == 0)
        {
            GoTo(GameState.QuizActive);
            yield break;
        }

        // "상대방이 쓰러졌다." (자기를 쏴서 죽었으면 따로) → 생존자에게만 "살아남았군." 과 생존·사망 글자
        GameEvents.Raise("player_died", deadSeat);
        yield return Wait(Say(deadSeat == m_ShooterSeat ? "player_died_self" : "player_died"));

        GoTo(GameState.GameOver);
        if (m_Player != null)
            m_Player.UnlockSeat();
        var winner = 3 - deadSeat;
        Debug.Log($"[Flow] 좌석 {deadSeat} 사망 → 좌석 {winner} 승리. 잠시 뒤 다시 시작", this);
        GameEvents.Raise("player_won", winner);
        yield return new WaitForSeconds(Mathf.Max(m_RestartDelay, Say("player_won", audienceSeat: winner)));

        GoTo(GameState.Lobby);
        BeginGame();
    }

    // 사격 결과 이벤트. 대사는 결과 브리핑에서 나오지만 소리는 바로 난다
    void RaiseShot(string name, Gun.ShotResult shot)
    {
        var target = name == "shot_hit_self" ? 0 : 3 - m_ShooterSeat;
        var gameEvent = new GameEvent(name, m_ShooterSeat, target);
        if (shot.HitBox != null)
            gameEvent = gameEvent.WithPart(shot.HitBox.Part);
        if (shot.IsLive && shot.DidHit)
            gameEvent = gameEvent.WithPosition(shot.EndPoint);
        GameEvents.Raise(gameEvent);
    }

    // ── 총을 직접 만지는 이벤트: 총을 쥔 기기(사수 기기)에서만 낸다. 네트워크 쪽이 상대 기기에 전달한다 ──

    bool ShooterIsHere => IsLocalSeat == null || IsLocalSeat(m_PresentedSeat);

    void RaiseFromShooter(string name, int count = -1)
    {
        if (ShooterIsHere)
            GameEvents.Raise(name, m_PresentedSeat, count: count);
    }

    void OnGunGrabbed()
    {
        if (ShooterIsHere)
            GameEvents.Raise(new GameEvent("gun_grabbed", m_PresentedSeat) { LocalOnly = true });
    }

    void OnGunDropped() => RaiseFromShooter("gun_dropped");
    void OnGunReturned() => RaiseFromShooter("gun_returned");
    void OnCylinderOpened() => RaiseFromShooter("cylinder_opened");
    void OnBulletInserted(int bullets) => RaiseFromShooter("bullet_inserted", bullets);
    void OnHammerCocked() => RaiseFromShooter("hammer_cock");

    // ── 상대 이탈 ──

    /// <summary>
    /// 게임 중에 상대가 나갔을 때 부른다(네트워크 쪽). 하던 진행을 멈추고 남은 쪽의 승리로 끝낸 뒤 로비로 돌아간다.
    /// 다시 START를 받을 수 있게 <see cref="HasStarted"/>를 되돌린다. 이 기기가 진행하도록(<see cref="DrivesGame"/>) 바꾼 뒤 부른다.
    /// </summary>
    public void EndByOpponentLeft(int leftSeat)
    {
        if (m_StateMachine.Current == GameState.Lobby || m_StateMachine.Current == GameState.GameOver)
        {
            HasStarted = false;
            return;
        }

        StopAllCoroutines();
        m_SeatingRoutine = null;
        m_ShooterTimer = null;
        if (m_Round != null)
        {
            m_Round.AnswerWrong -= OnAnswerWrong;
            m_Round.Resolved -= OnQuizResolved;
            m_Round = null;
        }
        m_Board.Clear();
        QuizEnded?.Invoke();
        WithdrawGun();

        var winner = leftSeat == 1 ? 2 : 1;
        GameEvents.Raise(new GameEvent("opponent_disconnected", leftSeat) { LocalOnly = true });
        GameEvents.Raise(new GameEvent("player_won", winner) { LocalOnly = true });
        GoTo(GameState.GameOver);
        if (m_Player != null)
            m_Player.UnlockSeat();
        StartCoroutine(ReturnToLobbyAfterOpponentLeft());
    }

    IEnumerator ReturnToLobbyAfterOpponentLeft()
    {
        yield return new WaitForSeconds(Mathf.Max(m_RestartDelay, Say("player_won", audienceSeat: LocalSeat)));
        GoTo(GameState.Lobby);
        HasStarted = false;
    }

    Damageable BodyOf(int seat) => m_Bodies != null && seat >= 1 && seat <= m_Bodies.Length ? m_Bodies[seat - 1] : null;

    // 양팔을 다 못 쓰는지 (총을 쥘 수 없다)
    bool HasNoHands(int seat)
    {
        var body = BodyOf(seat);
        return body != null && body.IsArmDisabled(BodyPart.LeftArm) && body.IsArmDisabled(BodyPart.RightArm);
    }

    // 맞고 살아남은 부위에 대한 대사. 팔은 양팔을 다 잃었는지에 따라 다르다
    static string HitPartLine(BodyPart part, Damageable body)
    {
        switch (part)
        {
            case BodyPart.LeftArm:
            case BodyPart.RightArm:
                return body.IsArmDisabled(BodyPart.LeftArm) && body.IsArmDisabled(BodyPart.RightArm) ? "hit_both_arms" : "hit_arm";
            default:
                return "hit_torso";
        }
    }

    /// <summary>좌석의 몸을 바꾼다. 멀티에서는 마네킹 대신 실제 플레이어의 몸을 넣는다.</summary>
    public void SetBody(int seat, Damageable body)
    {
        if (seat < 1)
            return;
        if (m_Bodies == null || m_Bodies.Length < seat)
            Array.Resize(ref m_Bodies, seat);
        if (isActiveAndEnabled)
            Unwatch(m_Bodies[seat - 1]);
        m_Bodies[seat - 1] = body;
        if (isActiveAndEnabled)
            Watch(body);
    }

    // 몸이 맞으면 좌석 번호와 함께 알린다 (네트워크 쪽이 클라이언트에 보낸다)
    readonly Dictionary<Damageable, Action<BodyPart, DamageOutcome>> m_BodyHandlers = new Dictionary<Damageable, Action<BodyPart, DamageOutcome>>();

    void Watch(Damageable body)
    {
        if (body == null || m_BodyHandlers.ContainsKey(body))
            return;

        Action<BodyPart, DamageOutcome> handler = (part, outcome) =>
        {
            var seat = Array.IndexOf(m_Bodies, body) + 1;
            if (seat >= 1 && DrivesGame)
            {
                SeatHit?.Invoke(seat, part);
                // 몸통·팔은 비명. 몸통은 중상(붉은 외곽, 숨소리), 팔은 그 팔을 못 쓴다 (몸이 알아서 처리)
                if (outcome == DamageOutcome.Wounded || outcome == DamageOutcome.ArmDisabled)
                    GameEvents.Raise("player_hurt", seat);
                if (outcome == DamageOutcome.Wounded)
                    GameEvents.Raise("player_wounded", seat);
            }
        };
        m_BodyHandlers[body] = handler;
        body.Damaged += handler;
    }

    void Unwatch(Damageable body)
    {
        if (body == null || !m_BodyHandlers.TryGetValue(body, out var handler))
            return;

        body.Damaged -= handler;
        m_BodyHandlers.Remove(body);
    }

    int FindDeadSeat()
    {
        for (var i = 0; i < m_Bodies.Length; i++)
            if (m_Bodies[i] != null && m_Bodies[i].IsDead)
                return i + 1;
        return 0;
    }

    void ShowButtonResult(int seat, int choiceIndex, bool correct)
    {
        if (seat >= 1 && seat <= m_Terminals.Length && m_Terminals[seat - 1] != null)
            m_Terminals[seat - 1].ShowResult(choiceIndex, correct);
        ButtonResultShown?.Invoke(seat, choiceIndex, correct);
    }

    static char Letter(int choiceIndex) => choiceIndex >= 0 && choiceIndex < 4 ? "ABCD"[choiceIndex] : '?';
}
