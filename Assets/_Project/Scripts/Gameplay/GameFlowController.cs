using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 한 판을 <see cref="GameStateMachine"/>으로 진행한다. 수치는 <see cref="TuningConfig"/>에서 읽어 각 로직에 넘긴다.
/// 흐름: 퀴즈 → (정답자 있음) 총 지급 → 사수 턴 → 사격 결과 → 결과 브리핑 → 사망 확인 → 다음 퀴즈 또는 게임 종료.
/// 사수 턴: 상대 쏘기(턴 끝), 자신 쏘기(공포탄이면 한 번 더), 여분 총알 넣고 돌리기(턴 끝). 상대·자신은 겨눈 몸으로 구분한다.
/// 착석 단계: 플레이어를 자기 의자 뒤에 세우고, 앉을 때까지 기다린다. 안내 → 경고 → 강제 착석. 앉으면 게임이 끝날 때까지 일어날 수 없다.
/// 진행자 대사는 <see cref="HostDialogue"/>가 이벤트 이름(docs/07_event_list.md)으로 골라 자막으로 띄운다. 대사가 끝날 때까지 기다린 뒤 다음으로 넘어간다.
/// 아직 없는 것: 양측 패널티(정답자가 없으면 다음 문제), 공포 단계, 네트워크.
/// </summary>
public class GameFlowController : MonoBehaviour
{
    [SerializeField, Tooltip("문제 CSV (Assets/_Project/Data/Quiz/questions.csv)")] TextAsset m_QuestionsCsv;
    [SerializeField] TuningConfig m_Config;
    [SerializeField] QuizBoard m_Board;
    [SerializeField, Tooltip("좌석 1, 2 순서")] QuizTerminal[] m_Terminals;
    [SerializeField] Gun m_Gun;
    [SerializeField, Tooltip("좌석 1, 2 순서. 정답자 앞에 총이 나타나는 위치")] Transform[] m_GunSpawnPoints;
    [SerializeField, Tooltip("좌석 1, 2 순서. 비워 두면 그 좌석은 죽지 않는다 (지금은 좌석 1이 플레이어 몸, 좌석 2가 마네킹)")]
    Damageable[] m_Bodies;
    [SerializeField, Tooltip("총과 함께 사수 앞에 놓을 여분 총알")] SpareBullet m_SpareBulletPrefab;
    [SerializeField, Tooltip("진행자 대사 (비워 두면 대사 없이 진행한다)")] HostDialogue m_Host;

    [Header("Seating")]
    [SerializeField, Tooltip("이 기기의 플레이어 (XR Origin)")] SeatedPlayer m_Player;
    [SerializeField, Tooltip("좌석 1, 2 순서의 의자")] Seat[] m_Seats;
    [SerializeField, Range(1, 2), Tooltip("이 기기의 플레이어 좌석. 다른 좌석의 의자는 앉을 수 없게 막는다 (멀티를 붙이면 접속 순서로 정한다)")]
    int m_PlayerSeat = 1;
    [SerializeField, Tooltip("총 지급 위치에서 테이블 바깥쪽으로 첫 여분 총알까지(m). 총을 집을 때 장전 영역에 닿지 않게 떨어뜨린다")] float m_SpareBulletOffset = 0.2f;
    [SerializeField, Tooltip("여분 총알 사이 간격(m)")] float m_SpareBulletSpacing = 0.05f;

    [Header("Timing (대사가 더 길면 대사가 끝날 때까지 기다린다)")]
    [SerializeField, Min(0f), Tooltip("퀴즈가 끝나고 버튼 색을 보여 주는 최소 시간(초)")] float m_QuizResultDelay = 1.5f;
    [SerializeField, Min(0f), Tooltip("쏜 뒤 총을 치우기까지(초)")] float m_ShotResultDelay = 1f;
    [SerializeField, Min(0f), Tooltip("결과 브리핑 최소 시간(초)")] float m_HostReactionDuration = 1.5f;
    [SerializeField, Min(0f), Tooltip("게임이 끝나고 처음부터 다시 시작하기까지 최소 시간(초)")] float m_RestartDelay = 3f;

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
    readonly List<SpareBullet> m_SpareBullets = new List<SpareBullet>();

    /// <summary>상태 변경을 구독하려는 쪽(조명·사운드 등)이 쓴다.</summary>
    public GameStateMachine StateMachine => m_StateMachine;

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
        }
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
        }
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

    Seat PlayerSeat => m_Seats != null && m_PlayerSeat - 1 < m_Seats.Length ? m_Seats[m_PlayerSeat - 1] : null;

    IEnumerator Seating()
    {
        var seat = PlayerSeat;
        if (m_Player == null || seat == null)
        {
            Debug.LogWarning("[Flow] 플레이어나 의자가 연결되지 않아 착석 단계를 건너뛴다", this);
        }
        else
        {
            // 다른 좌석의 의자는 앉을 수 없게 하고, 자기 의자 뒤에 세운다
            for (var i = 0; i < m_Seats.Length; i++)
                if (m_Seats[i] != null)
                    m_Seats[i].enabled = i == m_PlayerSeat - 1;
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
            Debug.Log($"[Flow] 좌석 {m_PlayerSeat} 착석 (player_seated)", this);
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
        Say("quiz_start");
        m_NextPressureTime = Now + m_Config.PressureLineInterval;
        Debug.Log($"[Flow] {question.Id} 출제: {question.Question} (정답 {Letter(question.AnswerIndex)}, {timeLimit}초)", this);
    }

    void OnAnswerPressed(int seat, int choiceIndex)
    {
        if (m_StateMachine.Current != GameState.QuizActive || m_Round == null)
            return;

        if (m_Round.Submit(seat, choiceIndex, Now) == QuizSubmitResult.Ignored)
            Debug.Log($"[Flow] 좌석 {seat}의 {Letter(choiceIndex)} 무시 (이 문제에서 잠김)", this);
    }

    void OnAnswerWrong(int seat, int choiceIndex)
    {
        Debug.Log($"[Flow] 좌석 {seat} 오답 {Letter(choiceIndex)} → 이 문제에서 잠김", this);
        ShowButtonResult(seat, choiceIndex, false);
        Say("answer_wrong");
    }

    void OnQuizResolved(QuizResult result)
    {
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
        var spawn = m_GunSpawnPoints != null && m_ShooterSeat - 1 < m_GunSpawnPoints.Length ? m_GunSpawnPoints[m_ShooterSeat - 1] : null;
        if (spawn == null)
        {
            Debug.LogError($"[Flow] 좌석 {m_ShooterSeat}의 총 지급 위치가 없다", this);
            return;
        }

        m_Gun.Present(spawn);
        SpawnSpareBullets(spawn);
        m_ShotReaction = null;
        m_ReloadedThisTurn = false;
        Say("gun_handover", m_Gun.BulletCount);
        Debug.Log($"[Flow] 좌석 {m_ShooterSeat}에게 총 지급 (총알 {m_Gun.BulletCount}발, 여분 {m_SpareBullets.Count}발)", this);
        GoTo(GameState.ShooterTurn);
    }

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

    void OnCylinderClosed(int inserted)
    {
        if (m_StateMachine.Current != GameState.ShooterTurn || inserted == 0)
            return;

        Debug.Log($"[Flow] 좌석 {m_ShooterSeat}: 총알 {inserted}발을 넣고 돌렸다 → 턴 종료 (총알 {m_Gun.BulletCount}발)", this);
        m_ShotReaction = "cylinder_spun";
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

    void SpawnSpareBullets(Transform spawn)
    {
        ClearSpareBullets();
        if (m_SpareBulletPrefab == null)
            return;

        // 총 지급 위치에서 테이블 바깥쪽(단말기 반대쪽)으로 한 줄로 세워 둔다
        var outward = spawn.right;
        for (var i = 0; i < m_Config.SpareBullets; i++)
        {
            var position = spawn.position + outward * (m_SpareBulletOffset + i * m_SpareBulletSpacing) + Vector3.up * 0.03f;
            var bullet = Instantiate(m_SpareBulletPrefab, position, Quaternion.identity);
            bullet.SetGun(m_Gun);
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

    IEnumerator ResolveShot()
    {
        m_Gun.IsTriggerLocked = true;
        GoTo(GameState.ShotResolve);
        yield return new WaitForSeconds(m_ShotResultDelay);
        m_Gun.Withdraw();
        ClearSpareBullets();

        // 결과 브리핑: 사격 결과(또는 총알 넣기·시간 초과)에 반응하고, 진행자가 장전했으면 바뀐 총알 수를 알려 준다
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
    }

    static char Letter(int choiceIndex) => choiceIndex >= 0 && choiceIndex < 4 ? "ABCD"[choiceIndex] : '?';
}
