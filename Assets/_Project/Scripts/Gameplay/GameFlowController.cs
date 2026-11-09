using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 한 판을 <see cref="GameStateMachine"/>으로 진행한다. 수치는 <see cref="TuningConfig"/>에서 읽어 각 로직에 넘긴다.
/// 흐름: 퀴즈 → (정답자 있음) 총 지급 → 사수 턴 → 사격 결과 → 결과 브리핑 → 사망 확인 → 다음 퀴즈 또는 게임 종료.
/// 사수 턴: 상대 쏘기(턴 끝), 자신 쏘기(공포탄이면 한 번 더), 여분 총알 넣고 돌리기(턴 끝). 상대·자신은 겨눈 몸으로 구분한다.
/// 아직 없는 것: 착석 단계와 규칙 설명(바로 지나감), 양측 패널티(정답자가 없으면 다음 문제), 진행자 대사(정해진 시간만 기다림), 네트워크.
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
    [SerializeField, Tooltip("총 지급 위치에서 테이블 바깥쪽으로 첫 여분 총알까지(m). 총을 집을 때 장전 영역에 닿지 않게 떨어뜨린다")] float m_SpareBulletOffset = 0.2f;
    [SerializeField, Tooltip("여분 총알 사이 간격(m)")] float m_SpareBulletSpacing = 0.05f;

    [Header("Timing (진행자 대사가 생기면 대사 길이에 맞춘다)")]
    [SerializeField, Min(0f), Tooltip("퀴즈가 끝나고 버튼 색을 보여 주는 시간(초)")] float m_QuizResultDelay = 1.5f;
    [SerializeField, Min(0f), Tooltip("쏜 뒤 총을 치우기까지(초)")] float m_ShotResultDelay = 1f;
    [SerializeField, Min(0f), Tooltip("결과 브리핑 시간(초)")] float m_HostReactionDuration = 1.5f;
    [SerializeField, Min(0f), Tooltip("게임이 끝나고 처음부터 다시 시작하기까지(초)")] float m_RestartDelay = 3f;

    readonly GameStateMachine m_StateMachine = new GameStateMachine();
    QuizDeck m_Deck;
    QuizRound m_Round;
    int m_ShooterSeat;
    Coroutine m_ShooterTimer;
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
        if (!m_Round.IsResolved)
            m_Board.SetRemainingTime(m_Round.GetRemainingRatio(Now));
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

        // 착석 단계와 규칙 설명은 아직 없어서 바로 지나간다
        GoTo(GameState.Seating);
        GoTo(GameState.Intro);
        GoTo(GameState.QuizActive);
    }

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
    }

    void OnQuizResolved(QuizResult result)
    {
        switch (result.Outcome)
        {
            case QuizOutcome.Correct:
                Debug.Log($"[Flow] 좌석 {result.WinnerSeat} 정답", this);
                ShowButtonResult(result.WinnerSeat, m_Round.Question.AnswerIndex, true);
                break;
            case QuizOutcome.Timeout:
                Debug.Log("[Flow] 시간 초과 → 정답자 없음", this);
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
        yield return new WaitForSeconds(m_QuizResultDelay);

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
        Debug.Log($"[Flow] 좌석 {m_ShooterSeat}에게 총 지급 (총알 {m_Gun.BulletCount}발, 여분 {m_SpareBullets.Count}발)", this);
        GoTo(GameState.ShooterTurn);
    }

    IEnumerator ShooterTimeout()
    {
        yield return new WaitForSeconds(m_Config.ShooterTurnTimeLimit);
        m_ShooterTimer = null;
        Debug.Log($"[Flow] 사수 턴 {m_Config.ShooterTurnTimeLimit}초가 지나 총을 반납한다", this);
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
            Debug.Log("[Flow] 자신을 쐈는데 공포탄 → 한 번 더 쏜다", this);
            return;
        }

        EndShooterTurn();
    }

    void OnCylinderClosed(int inserted)
    {
        if (m_StateMachine.Current != GameState.ShooterTurn || inserted == 0)
            return;

        Debug.Log($"[Flow] 좌석 {m_ShooterSeat}: 총알 {inserted}발을 넣고 돌렸다 → 턴 종료 (총알 {m_Gun.BulletCount}발)", this);
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

        GoTo(GameState.HostReaction);
        yield return new WaitForSeconds(m_HostReactionDuration);

        GoTo(GameState.DeathCheck);
        var deadSeat = FindDeadSeat();
        if (deadSeat == 0)
        {
            GoTo(GameState.QuizActive);
            yield break;
        }

        GoTo(GameState.GameOver);
        Debug.Log($"[Flow] 좌석 {deadSeat} 사망 → 좌석 {3 - deadSeat} 승리. {m_RestartDelay}초 뒤 다시 시작", this);
        yield return new WaitForSeconds(m_RestartDelay);

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
