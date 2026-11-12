using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 한 판을 <see cref="GameStateMachine"/>으로 진행한다. 수치는 <see cref="TuningConfig"/>에서 읽어 각 로직에 넘긴다.
/// 흐름: 퀴즈 → (정답자 있음) 총 지급 → 사수 턴 → 사격 결과 → 결과 브리핑 → 사망 확인 → 다음 퀴즈 또는 게임 종료.
/// 사수 턴: 상대 쏘기(턴 끝), 자신 쏘기(공포탄이면 한 번 더). 상대·자신은 겨눈 몸으로 구분한다. 총알은 진행자가 넣어 두고 사수는 쏘는 방법만 고른다.
/// 착석 단계: 이 기기의 플레이어를 자기 의자 뒤에 세우고, 앉을 때까지 기다린다. 안내 → 경고 → 강제 착석. 앉으면 게임이 끝날 때까지 일어날 수 없다.
/// 진행자 대사는 <see cref="HostDialogue"/>가 이벤트 이름(docs/07_event_list.md)으로 골라 자막으로 띄운다. 대사가 끝날 때까지 기다린 뒤 다음으로 넘어간다.
/// 아직 없는 것: 양측 패널티(정답자가 없으면 다음 문제), 공포 단계.
/// 멀티: 호스트에서만 돈다. 네트워크 쪽(Net/)이 이벤트를 받아 클라이언트에 보내고, <see cref="IsLocalSeat"/>, <see cref="SubmitAnswer"/>, <see cref="SetBody"/>로 상대 입력과 몸을 넣는다.
/// </summary>
public class GameFlowController : MonoBehaviour
{
    [SerializeField, Tooltip("문제 CSV (Assets/_Project/Data/Quiz/questions.csv)")] TextAsset m_QuestionsCsv;
    [SerializeField] TuningConfig m_Config;
    [SerializeField] QuizBoard m_Board;
    [SerializeField, Tooltip("좌석 1, 2 순서")] QuizTerminal[] m_Terminals;
    [SerializeField] Gun m_Gun;
    [SerializeField, Tooltip("총이 나타나는 위치 (테이블 가운데). 사수가 누구든 여기에 나온다")] Transform m_GunSpawnPoint;
    [SerializeField, Tooltip("좌석 1, 2 순서. 비워 두면 그 좌석은 죽지 않는다 (지금은 좌석 1이 플레이어 몸, 좌석 2가 마네킹)")]
    Damageable[] m_Bodies;
    [SerializeField, Tooltip("진행자 대사 (비워 두면 대사 없이 진행한다)")] HostDialogue m_Host;

    [Header("Seating")]
    [SerializeField, Tooltip("이 기기의 플레이어 (XR Origin)")] SeatedPlayer m_Player;
    [SerializeField, Tooltip("좌석 1, 2 순서의 의자")] Seat[] m_Seats;
    [SerializeField, Range(1, 2), Tooltip("혼자 테스트할 때 이 기기의 플레이어 좌석. 멀티에서는 IsLocalSeat로 정해진 좌석을 쓴다")]
    int m_PlayerSeat = 1;

    [Header("Timing (대사가 더 길면 대사가 끝날 때까지 기다린다)")]
    [SerializeField, Min(0f), Tooltip("퀴즈가 끝나고 버튼 색을 보여 주는 최소 시간(초)")] float m_QuizResultDelay = 1.5f;
    [SerializeField, Min(0f), Tooltip("쏜 뒤 총을 치우기까지(초)")] float m_ShotResultDelay = 1f;
    [SerializeField, Min(0f), Tooltip("결과 브리핑 최소 시간(초)")] float m_HostReactionDuration = 1.5f;
    [SerializeField, Min(0f), Tooltip("게임이 끝나고 처음부터 다시 시작하기까지 최소 시간(초)")] float m_RestartDelay = 3f;

    [Header("Start")]
    [SerializeField, Tooltip("켜면 Play하자마자 시작한다 (혼자 테스트할 때). 끄면 StartGame()을 부를 때까지 기다린다 (RoomScene은 LAN 패널의 START 버튼)")]
    bool m_StartOnPlay = true;

    readonly GameStateMachine m_StateMachine = new GameStateMachine();
    QuizDeck m_Deck;
    QuizRound m_Round;
    int m_ShooterSeat;
    Coroutine m_ShooterTimer;
    Coroutine m_SeatingRoutine;
    double m_NextPressureTime;
    // 사수 턴이 어떻게 끝났는지. 결과 브리핑에서 이 이벤트의 대사를 말한다
    string m_ShotReaction;
    bool m_ReloadedThisTurn;

    /// <summary>상태 변경을 구독하려는 쪽(조명·사운드 등)이 쓴다.</summary>
    public GameStateMachine StateMachine => m_StateMachine;

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
            m_Gun.Fired += OnGunFired;
    }

    void OnDisable()
    {
        foreach (var terminal in m_Terminals)
            if (terminal != null)
                terminal.AnswerPressed -= OnAnswerPressed;
        if (m_Gun != null)
            m_Gun.Fired -= OnGunFired;
    }

    void Update()
    {
        if (m_StateMachine.Current != GameState.QuizActive || m_Round == null || m_Round.IsResolved)
            return;

        m_Round.Tick(Now);
        if (m_Round.IsResolved)
            return;

        m_Board.SetRemainingTime(m_Round.GetRemainingRatio(Now));
        if (m_Config.PressureLineInterval > 0f && Now >= m_NextPressureTime)
        {
            m_NextPressureTime = Now + m_Config.PressureLineInterval;
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

        // 클라이언트는 호스트의 상태를 따라갈 뿐 진행을 돌리지 않는다
        if (!DrivesGame)
            return;

        switch (next)
        {
            case GameState.QuizActive:
                StartQuiz();
                break;
            case GameState.GunHandover:
                HandOverGun();
                break;
            case GameState.ShooterTurn:
                m_ShooterTimer = StartCoroutine(ShooterTimeout());
                break;
        }
    }

    void BeginGame()
    {
        foreach (var body in m_Bodies)
            if (body != null && body.IsDead)
                body.Revive();
        m_Gun.ResetRevolver();

        GoTo(GameState.Seating);
        m_SeatingRoutine = StartCoroutine(Seating());
    }

    // ── 착석 ──

    // 이 기기의 플레이어 좌석. 멀티면 네트워크 쪽이 정한 좌석, 혼자면 m_PlayerSeat
    int LocalSeatNumber
    {
        get
        {
            if (IsLocalSeat != null && m_Seats != null)
                for (var seat = 1; seat <= m_Seats.Length; seat++)
                    if (IsLocalSeat(seat))
                        return seat;
            return m_PlayerSeat;
        }
    }

    IEnumerator Seating()
    {
        var seatNumber = LocalSeatNumber;
        var seat = m_Seats != null && seatNumber - 1 < m_Seats.Length ? m_Seats[seatNumber - 1] : null;
        if (m_Player == null || seat == null)
        {
            Debug.LogWarning("[Flow] 플레이어나 의자가 연결되지 않아 착석 단계를 건너뛴다", this);
        }
        else
        {
            // 다른 좌석의 의자는 앉을 수 없게 하고, 자기 의자 뒤에 세운다
            for (var i = 0; i < m_Seats.Length; i++)
                if (m_Seats[i] != null)
                    m_Seats[i].enabled = i == seatNumber - 1;
            m_Player.PlaceBehind(seat);
            Say("seat_prompt");

            var warned = false;
            var start = Time.time;
            while (m_Player.CurrentSeat != seat)
            {
                var elapsed = Time.time - start;
                if (!warned && elapsed >= m_Config.SeatingWarningDelay)
                {
                    warned = true;
                    Say("seat_warning");
                }
                else if (warned && elapsed >= m_Config.SeatingWarningDelay + m_Config.ForceSeatDelay)
                {
                    m_Player.SitOn(seat);
                    m_Player.LockSeat();
                    yield return new WaitForSeconds(Say("seat_forced"));
                    break;
                }

                yield return null;
            }

            // 앉으면 게임이 끝날 때까지 일어날 수 없다
            m_Player.LockSeat();
            Debug.Log($"[Flow] 좌석 {seatNumber} 착석 (player_seated)", this);
        }

        m_SeatingRoutine = null;
        m_Board.Clear();

        // 규칙 설명: 진행자가 세 문장으로 말하고, 끝나면 첫 문제를 낸다
        GoTo(GameState.Intro);
        yield return new WaitForSeconds(Say("rules_intro"));
        GoTo(GameState.QuizActive);
    }

    /// <summary>진행자가 이 이벤트의 대사를 말한다. 다 읽는 데 걸리는 시간(초)을 돌려준다 (대사가 없으면 0).</summary>
    float Say(string eventName, int bullets = -1) => m_Host != null ? m_Host.Say(eventName, bullets) : 0f;

    // ── 퀴즈 ──

    void StartQuiz()
    {
        foreach (var terminal in m_Terminals)
            if (terminal != null)
                terminal.ClearResults();

        var question = m_Deck.Draw(out var reshuffled);
        if (reshuffled)
            Debug.Log("[Flow] 문제를 다 써서 처음부터 다시 낸다", this);

        // 공포 단계는 아직 없어서 단계 0의 제한시간을 쓴다
        var timeLimit = m_Config.GetQuizTimeLimit(0);
        m_Round = new QuizRound(question, Now, timeLimit);
        m_Round.AnswerWrong += OnAnswerWrong;
        m_Round.Resolved += OnQuizResolved;

        m_Board.Show(question);
        QuizShown?.Invoke(question, timeLimit);
        Say("quiz_start");
        m_NextPressureTime = Now + m_Config.PressureLineInterval;
        Debug.Log($"[Flow] {question.Id} 출제: {question.Question} (정답 {Letter(question.AnswerIndex)}, {timeLimit}초)", this);
    }

    // 이 기기의 단말기 입력
    void OnAnswerPressed(int seat, int choiceIndex)
    {
        if (IsLocalSeat != null && !IsLocalSeat(seat))
            return;

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
        Say("answer_wrong");
    }

    void OnQuizResolved(QuizResult result)
    {
        QuizEnded?.Invoke();
        var lineDuration = 0f;
        switch (result.Outcome)
        {
            case QuizOutcome.Correct:
                Debug.Log($"[Flow] 좌석 {result.WinnerSeat} 정답", this);
                ShowButtonResult(result.WinnerSeat, m_Round.Question.AnswerIndex, true);
                lineDuration = Say("answer_correct");
                break;
            case QuizOutcome.Timeout:
                Debug.Log("[Flow] 시간 초과 → 정답자 없음", this);
                lineDuration = Say("quiz_timeout");
                break;
            case QuizOutcome.AllWrong:
                // 두 번째 오답 대사가 방금 나왔으므로 따로 말하지 않는다
                Debug.Log("[Flow] 둘 다 오답 → 정답자 없음", this);
                break;
        }

        StartCoroutine(AfterQuiz(result, lineDuration));
    }

    IEnumerator AfterQuiz(QuizResult result, float lineDuration)
    {
        GoTo(GameState.QuizResolve);
        yield return new WaitForSeconds(Mathf.Max(m_QuizResultDelay, lineDuration));

        if (result.HasWinner)
        {
            m_ShooterSeat = result.WinnerSeat;
            GoTo(GameState.GunHandover);
        }
        else
        {
            // 양측 패널티를 만들기 전까지는 바로 다음 문제로 간다 (임시 경로)
            GoTo(GameState.QuizActive);
        }
    }

    // ── 총 ──

    void HandOverGun()
    {
        if (!PresentGunForSeat(m_ShooterSeat))
            return;

        m_ShotReaction = null;
        m_ReloadedThisTurn = false;
        Say("gun_handover", m_Gun.BulletCount);
        Debug.Log($"[Flow] 좌석 {m_ShooterSeat}에게 총 지급 (총알 {m_Gun.BulletCount}발)", this);
        GunPresented?.Invoke(m_ShooterSeat);
        GoTo(GameState.ShooterTurn);
    }

    /// <summary>
    /// 테이블 가운데에 총을 꺼낸다. 사수 좌석이 이 기기의 좌석이 아니면(<see cref="IsLocalSeat"/>) 잡을 수 없게 둔다.
    /// 진행을 돌리지 않는 네트워크 클라이언트는 호스트의 알림을 받아 이것만 부른다.
    /// </summary>
    public bool PresentGunForSeat(int seat)
    {
        if (m_GunSpawnPoint == null)
        {
            Debug.LogError("[Flow] 총 지급 위치가 없다", this);
            return false;
        }

        m_Gun.Present(m_GunSpawnPoint);
        m_Gun.SetGrabbable(IsLocalSeat == null || IsLocalSeat(seat));
        return true;
    }

    /// <summary>총을 치운다.</summary>
    public void WithdrawGun() => m_Gun.Withdraw();

    IEnumerator ShooterTimeout()
    {
        yield return new WaitForSeconds(m_Config.ShooterTurnTimeLimit);
        m_ShooterTimer = null;
        Debug.Log($"[Flow] 사수 턴 {m_Config.ShooterTurnTimeLimit}초가 지나 총을 반납한다", this);
        m_ShotReaction = "shooter_timeout";
        StartCoroutine(ResolveShot());
    }

    void OnGunFired(Gun.ShotResult shot)
    {
        if (m_StateMachine.Current != GameState.ShooterTurn)
            return;

        // 자기 몸을 겨눴으면 자신 쏘기, 상대 몸이나 아무 데도 아니면 상대 쏘기
        var shooterBody = BodyOf(m_ShooterSeat);
        var self = shot.HitBox != null && shooterBody != null && shot.HitBox.Owner == shooterBody;
        Debug.Log($"[Flow] 좌석 {m_ShooterSeat}: {(self ? "자신 쏘기" : "상대 쏘기")}, {(shot.IsLive ? "실탄" : "공포탄")}", this);

        if (self && !shot.IsLive)
        {
            // 한 번 더 쏘기 때문에 결과 브리핑을 기다리지 않고 바로 말한다
            Debug.Log("[Flow] 자신을 쐈는데 공포탄 → 한 번 더 쏜다", this);
            Say("shot_blank_self");
            return;
        }

        if (shot.IsLive)
            m_ShotReaction = self ? "shot_hit_self" : shot.HitBox != null ? "shot_hit_opponent" : "shot_miss";
        else
            m_ShotReaction = "shot_blank_opponent";
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

        // 결과 브리핑: 사격 결과(또는 시간 초과)에 반응하고, 진행자가 장전했으면 바뀐 총알 수를 알려 준다
        GoTo(GameState.HostReaction);
        var reaction = m_ShotReaction;
        var bullets = m_Gun.BulletCount;
        var lineDuration = reaction != null ? Say(reaction, bullets) : 0f;
        yield return new WaitForSeconds(Mathf.Max(m_HostReactionDuration, lineDuration));
        if (m_ReloadedThisTurn)
            yield return new WaitForSeconds(Say("reload", bullets));

        GoTo(GameState.DeathCheck);
        var deadSeat = FindDeadSeat();
        if (deadSeat == 0)
        {
            GoTo(GameState.QuizActive);
            yield break;
        }

        yield return new WaitForSeconds(Say("player_died"));

        GoTo(GameState.GameOver);
        if (m_Player != null)
            m_Player.UnlockSeat();
        Debug.Log($"[Flow] 좌석 {deadSeat} 사망 → 좌석 {3 - deadSeat} 승리. 잠시 뒤 다시 시작", this);
        yield return new WaitForSeconds(Mathf.Max(m_RestartDelay, Say("player_won")));

        GoTo(GameState.Lobby);
        BeginGame();
    }

    Damageable BodyOf(int seat) => seat >= 1 && seat <= m_Bodies.Length ? m_Bodies[seat - 1] : null;

    /// <summary>좌석의 몸을 바꾼다. 멀티에서는 마네킹 대신 실제 플레이어의 몸을 넣는다.</summary>
    public void SetBody(int seat, Damageable body)
    {
        if (seat < 1)
            return;
        if (m_Bodies == null || m_Bodies.Length < seat)
            Array.Resize(ref m_Bodies, seat);
        m_Bodies[seat - 1] = body;
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
