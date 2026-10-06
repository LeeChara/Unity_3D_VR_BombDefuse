using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 번갈아 전선 자르기 모드 (문제 없음).
/// 차례가 된 플레이어는 자기 앞 기폭장치(= 상대 조끼)의 전선을 하나 자른다.
/// 진짜 전선이면 상대 목숨 -1, 진짜 전선을 모두 자르면 상대 조끼가 폭발하고 승리한다.
/// 데스크톱 핫시트: 차례가 바뀌면 카메라가 해당 플레이어 좌석으로 넘어간다.
/// </summary>
public class TurnCutGameManager : MonoBehaviour
{
    [Serializable]
    private class PlayerSlot
    {
        public string id;
        public Transform eyeAnchor;
        [Tooltip("이 플레이어 앞의 기폭장치 (상대 조끼와 연결)")]
        public DetonatorTerminal terminal;
        [Tooltip("1인칭일 때 숨길 자기 머리/팔")]
        public GameObject headAndArms;

        [NonSerialized] public HashSet<int> liveWires = new HashSet<int>();
        public BombVest TargetVest => terminal.TargetVest;
    }

    private enum State { WaitingCut, Resolving, GameOver }

    [SerializeField] private PlayerSlot[] players = new PlayerSlot[2];
    [SerializeField] private DesktopFirstPersonController viewController;
    [SerializeField] private TextMesh statusText;

    [Tooltip("시작 플레이어를 무작위로 정한다. 끄면 항상 첫 번째 플레이어부터")]
    [SerializeField] private bool randomFirstPlayer = true;
    [Tooltip("전선을 자른 뒤 결과를 보여주고 차례를 넘기기까지의 시간(초)")]
    [SerializeField] private float resultDelay = 1.8f;
    [SerializeField] private float viewPitch = 35f;

    private State state;
    private int current;
    private string banner = "";
    private GUIStyle bannerStyle;

    private PlayerSlot Current => players[current];
    private PlayerSlot Opponent => players[1 - current];

    private void Start()
    {
        foreach (PlayerSlot p in players)
        {
            AssignLiveWires(p);
            foreach (DetonatorWire wire in p.terminal.Wires)
                wire.CutPerformed += OnWireCut;
        }

        current = randomFirstPlayer ? UnityEngine.Random.Range(0, players.Length) : 0;
        BeginTurn();
    }

    private void OnDestroy()
    {
        foreach (PlayerSlot p in players)
            if (p.terminal != null)
                foreach (DetonatorWire wire in p.terminal.Wires)
                    wire.CutPerformed -= OnWireCut;
    }

    /// <summary>
    /// 기폭장치 전선 중 진짜(조끼 연결) 전선을 무작위로 정한다. 개수 = 상대 조끼의 목숨 수.
    /// TODO: 기획대로 각 플레이어가 자기 조끼의 기폭 전선을 직접 고르는 설정 단계로 교체
    /// </summary>
    private static void AssignLiveWires(PlayerSlot p)
    {
        LifeGauge gauge = p.TargetVest.LifeGauge;
        int wireCount = p.terminal.Wires.Length;
        int liveCount = Mathf.Min(gauge.MaxLives, wireCount);

        var indices = new List<int>();
        for (int i = 0; i < wireCount; i++) indices.Add(i);
        for (int i = 0; i < liveCount; i++)
        {
            int pick = UnityEngine.Random.Range(i, indices.Count);
            (indices[i], indices[pick]) = (indices[pick], indices[i]);
            p.liveWires.Add(indices[i]);
        }
        gauge.SetLives(liveCount);
    }

    private void BeginTurn()
    {
        state = State.WaitingCut;

        // 시점 이동: 차례인 플레이어 좌석으로. 자기 머리/팔은 숨기고 상대 것은 보이게
        foreach (PlayerSlot p in players)
            if (p.headAndArms != null) p.headAndArms.SetActive(p != Current);
        viewController.SetView(Current.eyeAnchor, viewPitch);

        SetWiresLocked(Current, false);
        SetWiresLocked(Opponent, true);
        Current.terminal.SetStatus("YOUR TURN : CUT 1");
        Opponent.terminal.SetStatus("WAITING...");

        banner = $"PLAYER {Current.id}'s TURN - cut one wire";
        UpdateMonitor($"PLAYER {Current.id}'s TURN\nCut one wire");
    }

    private void OnWireCut(DetonatorWire wire)
    {
        if (state != State.WaitingCut || Array.IndexOf(Current.terminal.Wires, wire) < 0) return;

        state = State.Resolving;
        SetWiresLocked(Current, true);

        BombVest target = Current.TargetVest;
        bool live = Current.liveWires.Contains(wire.Index);
        string cutName = $"{wire.ColorName.ToUpper()} wire";

        if (live)
        {
            LifeGauge gauge = target.LifeGauge;
            gauge.SetLives(gauge.Lives - 1);
            Current.terminal.SetStatus($"{cutName} : LIVE!");
            banner = $"PLAYER {Current.id} cut the {cutName} - LIVE! Player {target.OwnerId} lives {gauge.Lives}";
            UpdateMonitor($"PLAYER {Current.id} cut {cutName}\nLIVE WIRE!");

            if (gauge.Lives <= 0)
            {
                StartCoroutine(Explode(target));
                return;
            }
        }
        else
        {
            Current.terminal.SetStatus($"{cutName} : DUD");
            banner = $"PLAYER {Current.id} cut the {cutName} - dud";
            UpdateMonitor($"PLAYER {Current.id} cut {cutName}\nDud...");
        }

        StartCoroutine(NextTurnAfterDelay());
    }

    private IEnumerator NextTurnAfterDelay()
    {
        yield return new WaitForSeconds(resultDelay);
        current = 1 - current;
        BeginTurn();
    }

    private IEnumerator Explode(BombVest target)
    {
        state = State.GameOver;
        PlayerSlot winner = Current;
        SetWiresLocked(Opponent, true);

        yield return new WaitForSeconds(0.6f);
        target.Detonate();

        // 간단한 폭발 섬광 (카메라 흔들림 대신 빛으로만 연출 - 멀미 방지)
        Light flash = new GameObject("Explosion_Flash").AddComponent<Light>();
        flash.transform.position = target.transform.position + Vector3.up * 1f;
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.6f, 0.25f);
        flash.range = 6f;
        for (float t = 0f; t < 1.2f; t += Time.deltaTime)
        {
            flash.intensity = Mathf.Lerp(60f, 0f, t / 1.2f);
            yield return null;
        }
        Destroy(flash.gameObject);

        winner.terminal.SetStatus("TARGET DESTROYED");
        banner = $"PLAYER {winner.id} WINS!  (R: restart)";
        UpdateMonitor($"PLAYER {target.OwnerId}'s VEST EXPLODED\nPLAYER {winner.id} WINS!\n\nPress R to restart");
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (state == State.GameOver && keyboard != null && keyboard.rKey.wasPressedThisFrame)
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private static void SetWiresLocked(PlayerSlot p, bool locked)
    {
        foreach (DetonatorWire wire in p.terminal.Wires) wire.Locked = locked;
    }

    private void UpdateMonitor(string headline)
    {
        if (statusText == null) return;
        var sb = new System.Text.StringBuilder(headline).Append("\n\n");
        foreach (PlayerSlot p in players)
        {
            // 조끼 주인 기준 목숨: p가 착용한 조끼는 상대 단말기의 TargetVest
            LifeGauge gauge = Array.Find(players, o => o != p).TargetVest.LifeGauge;
            sb.Append($"PLAYER {p.id} LIVES : {gauge.Lives}/{gauge.MaxLives}\n");
        }
        statusText.text = sb.ToString();
    }

    private void OnGUI()
    {
        bannerStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.85f, 0.3f) },
        };
        GUI.Label(new Rect(0, 12, Screen.width, 30), banner, bannerStyle);
    }
}
