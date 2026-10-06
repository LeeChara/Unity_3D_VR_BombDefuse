using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 어두운 밀실 + 가운데 탁자 + 마주 앉은 두 플레이어(폭탄 조끼 착용) + 각자 앞에 놓인 상대 폭탄의 기폭 단말기
/// 씬을 프리미티브로 생성한다.
/// 메뉴: Tools/BombDefuse/Build Bomb Room Scene (문제 풀이 방식), ... (Turn Cut) (번갈아 자르기 방식)
/// </summary>
public static class BombRoomSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/BombRoom.unity";
    private const string TurnCutScenePath = "Assets/Scenes/BombRoom_TurnCut.unity";
    private const string MaterialDir = "Assets/Materials/BombRoom";

    // 탁자 중심에서 각 좌석까지의 거리(z)
    private const float SeatDistance = 1.0f;
    private const float TableHeight = 0.75f;

    private static readonly Color AccentA = new Color(0.15f, 0.55f, 1.0f);
    private static readonly Color AccentB = new Color(1.0f, 0.45f, 0.1f);

    // 기폭장치 전선 총수(m)와 조끼 목숨 수(= 진짜 전선 수 n)
    private const int WireCount = 8;
    private const int LifeCount = 3;

    private static readonly (string name, Color color)[] WireColors =
    {
        ("Red", new Color(0.8f, 0.05f, 0.05f)),
        ("Blue", new Color(0.05f, 0.15f, 0.8f)),
        ("Yellow", new Color(0.85f, 0.75f, 0.05f)),
        ("Green", new Color(0.05f, 0.6f, 0.1f)),
        ("White", new Color(0.85f, 0.85f, 0.85f)),
        ("Black", new Color(0.03f, 0.03f, 0.03f)),
        ("Orange", new Color(0.95f, 0.4f, 0.05f)),
        ("Purple", new Color(0.45f, 0.1f, 0.7f)),
    };

    private static readonly Color[] AnswerColors =
    {
        new Color(0.9f, 0.15f, 0.1f), new Color(0.1f, 0.4f, 0.95f),
        new Color(0.95f, 0.8f, 0.1f), new Color(0.1f, 0.8f, 0.25f),
    };

    private static Material concreteFloor, concreteWall, metalDark, wood, fabric, skin, vestCloth,
        strap, dynamite, tape, plasticBlack, ledRed, ledGreen, ledOff, screenGreen, screenBlue, bulbWarm, buttonRed, buttonYellow,
        wireRed, wireBlue, wireYellow, accentA, accentB, doorMetal;
    private static Material[] wireMats, answerMats;

    /// <summary>Quiz: 문제를 먼저 맞힌 쪽이 전선을 자른다. TurnCut: 문제 없이 번갈아 전선을 자른다.</summary>
    private enum GameMode { Quiz, TurnCut }

    [MenuItem("Tools/BombDefuse/Build Bomb Room Scene")]
    public static void Build() => Build(GameMode.Quiz, ScenePath);

    [MenuItem("Tools/BombDefuse/Build Bomb Room Scene (Turn Cut)")]
    public static void BuildTurnCut() => Build(GameMode.TurnCut, TurnCutScenePath);

    private static void Build(GameMode mode, string scenePath)
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        CreateMaterials();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetupEnvironment();

        BuildRoom();
        BuildTable();
        BuildLamp();

        Seat seatA = BuildSeat("Player_A", new Vector3(0f, 0f, -SeatDistance), 0f, AccentA);
        Seat seatB = BuildSeat("Player_B", new Vector3(0f, 0f, SeatDistance), 180f, AccentB);

        // 각 플레이어 앞의 단말기는 "상대" 폭탄의 기폭장치
        bool quiz = mode == GameMode.Quiz;
        DetonatorTerminal terminalA = quiz
            ? BuildTerminal("Terminal_A (Detonates B)", seatA.TerminalAnchor, "A", seatB.Vest, accentB)
            : BuildWireBoard("WireBoard_A (Detonates B)", seatA.TerminalAnchor, "A", seatB.Vest, accentB);
        DetonatorTerminal terminalB = quiz
            ? BuildTerminal("Terminal_B (Detonates A)", seatB.TerminalAnchor, "B", seatA.Vest, accentA)
            : BuildWireBoard("WireBoard_B (Detonates A)", seatB.TerminalAnchor, "B", seatA.Vest, accentA);

        if (quiz)
        {
            BuildQuizMonitor();
            BuildCamera(seatA, hideHeadAndArms: true);
        }
        else
        {
            TextMesh status = BuildMonitor("Status_Monitor", "PLAYER A's TURN");
            DesktopFirstPersonController controller = BuildCamera(seatA, hideHeadAndArms: false);
            BuildTurnCutManager(controller, status, (seatA, terminalA), (seatB, terminalB));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
        EditorSceneManager.SaveScene(scene, scenePath);
        AddSceneToBuildSettings(scenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[BombRoomSceneBuilder] 씬 생성 완료: {scenePath}");
    }

    private static void BuildTurnCutManager(DesktopFirstPersonController controller, TextMesh status,
        params (Seat seat, DetonatorTerminal terminal)[] players)
    {
        TurnCutGameManager manager = new GameObject("TurnCut_GameManager").AddComponent<TurnCutGameManager>();
        var so = new SerializedObject(manager);
        so.FindProperty("viewController").objectReferenceValue = controller;
        so.FindProperty("statusText").objectReferenceValue = status;
        SerializedProperty list = so.FindProperty("players");
        list.arraySize = players.Length;
        for (int i = 0; i < players.Length; i++)
        {
            SerializedProperty p = list.GetArrayElementAtIndex(i);
            p.FindPropertyRelative("id").stringValue = players[i].terminal.HolderId;
            p.FindPropertyRelative("eyeAnchor").objectReferenceValue = players[i].seat.EyeAnchor;
            p.FindPropertyRelative("terminal").objectReferenceValue = players[i].terminal;
            p.FindPropertyRelative("headAndArms").objectReferenceValue = players[i].seat.HeadAndArms;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ───────────────────────── Environment ─────────────────────────

    private static void SetupEnvironment()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.025f, 0.025f, 0.03f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.reflectionIntensity = 0.2f;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = new Color(0.01f, 0.01f, 0.012f);
        RenderSettings.fogDensity = 0.12f;
    }

    private static void BuildRoom()
    {
        Transform room = new GameObject("Room").transform;
        const float size = 6f, height = 3f, wall = 0.2f;

        Box("Floor", room, new Vector3(0, -0.05f, 0), new Vector3(size, 0.1f, size), concreteFloor);
        Box("Ceiling", room, new Vector3(0, height + 0.05f, 0), new Vector3(size, 0.1f, size), concreteWall);
        Box("Wall_North", room, new Vector3(0, height / 2, size / 2), new Vector3(size, height, wall), concreteWall);
        Box("Wall_South", room, new Vector3(0, height / 2, -size / 2), new Vector3(size, height, wall), concreteWall);
        Box("Wall_East", room, new Vector3(size / 2, height / 2, 0), new Vector3(wall, height, size), concreteWall);
        Box("Wall_West", room, new Vector3(-size / 2, height / 2, 0), new Vector3(wall, height, size), concreteWall);

        // 잠긴 철문
        Transform door = new GameObject("Door").transform;
        door.SetParent(room, false);
        door.localPosition = new Vector3(size / 2 - wall / 2 - 0.03f, 0, 1.6f);
        Box("Door_Panel", door, new Vector3(0, 1.05f, 0), new Vector3(0.05f, 2.1f, 1.0f), doorMetal);
        Box("Door_Frame_Top", door, new Vector3(0, 2.15f, 0), new Vector3(0.08f, 0.1f, 1.15f), metalDark);
        Box("Door_Frame_L", door, new Vector3(0, 1.05f, -0.55f), new Vector3(0.08f, 2.2f, 0.06f), metalDark);
        Box("Door_Frame_R", door, new Vector3(0, 1.05f, 0.55f), new Vector3(0.08f, 2.2f, 0.06f), metalDark);
        Cylinder("Door_Handle", door, new Vector3(-0.06f, 1.0f, -0.38f), new Vector3(0.03f, 0.07f, 0.03f), metalDark, new Vector3(0, 0, 90));
        Box("Door_Slot", door, new Vector3(-0.03f, 1.55f, 0), new Vector3(0.01f, 0.08f, 0.35f), plasticBlack);

        // 천장 배관
        Cylinder("Pipe_1", room, new Vector3(-2.3f, 2.8f, 0), new Vector3(0.12f, size / 2, 0.12f), metalDark, new Vector3(90, 0, 0));
        Cylinder("Pipe_2", room, new Vector3(-2.05f, 2.85f, 0), new Vector3(0.07f, size / 2, 0.07f), metalDark, new Vector3(90, 0, 0));
        Cylinder("Pipe_3", room, new Vector3(0, 2.88f, 2.6f), new Vector3(0.08f, size / 2, 0.08f), metalDark, new Vector3(0, 0, 90));

        // 환풍구
        Transform vent = new GameObject("Vent").transform;
        vent.SetParent(room, false);
        vent.localPosition = new Vector3(-size / 2 + wall / 2 + 0.01f, 2.4f, -1.5f);
        Box("Vent_Frame", vent, Vector3.zero, new Vector3(0.03f, 0.4f, 0.6f), metalDark);
        for (int i = 0; i < 6; i++)
            Box($"Vent_Slat_{i}", vent, new Vector3(0.02f, -0.15f + i * 0.06f, 0), new Vector3(0.02f, 0.015f, 0.55f), plasticBlack);

        SetStaticRecursive(room.gameObject);
    }

    private static void BuildTable()
    {
        Transform table = new GameObject("Table").transform;
        const float width = 0.9f, length = 1.4f, top = 0.05f;

        Box("Table_Top", table, new Vector3(0, TableHeight - top / 2, 0), new Vector3(width, top, length), wood);
        float legH = TableHeight - top;
        float lx = width / 2 - 0.06f, lz = length / 2 - 0.06f;
        Box("Table_Leg_FL", table, new Vector3(-lx, legH / 2, -lz), new Vector3(0.06f, legH, 0.06f), metalDark);
        Box("Table_Leg_FR", table, new Vector3(lx, legH / 2, -lz), new Vector3(0.06f, legH, 0.06f), metalDark);
        Box("Table_Leg_BL", table, new Vector3(-lx, legH / 2, lz), new Vector3(0.06f, legH, 0.06f), metalDark);
        Box("Table_Leg_BR", table, new Vector3(lx, legH / 2, lz), new Vector3(0.06f, legH, 0.06f), metalDark);
        Box("Table_Apron_L", table, new Vector3(-lx, legH - 0.05f, 0), new Vector3(0.03f, 0.08f, length - 0.15f), metalDark);
        Box("Table_Apron_R", table, new Vector3(lx, legH - 0.05f, 0), new Vector3(0.03f, 0.08f, length - 0.15f), metalDark);

        SetStaticRecursive(table.gameObject);
    }

    private static void BuildLamp()
    {
        Transform lamp = new GameObject("Ceiling_Lamp").transform;
        Cylinder("Lamp_Cord", lamp, new Vector3(0, 2.5f, 0), new Vector3(0.012f, 0.5f, 0.012f), plasticBlack);
        Cylinder("Lamp_Shade", lamp, new Vector3(0, 2.0f, 0), new Vector3(0.42f, 0.06f, 0.42f), metalDark);
        Cylinder("Lamp_Shade_Top", lamp, new Vector3(0, 2.08f, 0), new Vector3(0.18f, 0.04f, 0.18f), metalDark);
        Sphere("Lamp_Bulb", lamp, new Vector3(0, 1.93f, 0), Vector3.one * 0.09f, bulbWarm, collider: false);

        Light spot = new GameObject("Lamp_SpotLight").AddComponent<Light>();
        spot.transform.SetParent(lamp, false);
        spot.transform.localPosition = new Vector3(0, 1.9f, 0);
        spot.transform.localRotation = Quaternion.Euler(90, 0, 0);
        spot.type = LightType.Spot;
        spot.color = new Color(1f, 0.82f, 0.6f);
        spot.intensity = 6f;
        spot.range = 5f;
        spot.spotAngle = 80f;
        spot.innerSpotAngle = 35f;
        spot.shadows = LightShadows.Soft;
    }

    // ───────────────────────── Seat / Player ─────────────────────────

    private struct Seat
    {
        public BombVest Vest;
        public Transform TerminalAnchor;
        public Transform EyeAnchor;
        /// <summary>1인칭 시점에서 시야를 가리는 머리/팔 묶음</summary>
        public GameObject HeadAndArms;
    }

    /// <summary>좌석 루트 로컬 공간: +z가 탁자 방향.</summary>
    private static Seat BuildSeat(string name, Vector3 position, float yaw, Color accent)
    {
        Transform seat = new GameObject(name + "_Seat").transform;
        seat.position = position;
        seat.rotation = Quaternion.Euler(0, yaw, 0);

        BuildChair(seat);
        BombVest vest = BuildMannequin(name, seat, accent, out GameObject headAndArms);

        // 플레이어 정면 탁자 위 (탁자 상판 높이)
        Transform terminalAnchor = new GameObject("Terminal_Anchor").transform;
        terminalAnchor.SetParent(seat, false);
        terminalAnchor.localPosition = new Vector3(0, TableHeight, 0.48f);

        // 1인칭 카메라 / 추후 XR Origin 배치용 시점 앵커
        Transform eye = new GameObject("Eye_Anchor").transform;
        eye.SetParent(seat, false);
        eye.localPosition = new Vector3(0, 1.38f, 0.05f);

        return new Seat { Vest = vest, TerminalAnchor = terminalAnchor, EyeAnchor = eye, HeadAndArms = headAndArms };
    }

    private static void BuildChair(Transform seat)
    {
        Transform chair = new GameObject("Chair").transform;
        chair.SetParent(seat, false);
        chair.localPosition = new Vector3(0, 0, -0.02f);

        Box("Chair_Seat", chair, new Vector3(0, 0.45f, 0), new Vector3(0.46f, 0.05f, 0.46f), metalDark);
        foreach (var (x, z, n) in new[] { (-1, -1, "BL"), (1, -1, "BR"), (-1, 1, "FL"), (1, 1, "FR") })
            Box($"Chair_Leg_{n}", chair, new Vector3(x * 0.2f, 0.215f, z * 0.2f), new Vector3(0.035f, 0.43f, 0.035f), metalDark);
        Box("Chair_Back", chair, new Vector3(0, 0.78f, -0.22f), new Vector3(0.46f, 0.5f, 0.035f), metalDark);
        SetStaticRecursive(chair.gameObject);
    }

    private static BombVest BuildMannequin(string name, Transform seat, Color accent, out GameObject headAndArms)
    {
        Transform body = new GameObject(name).transform;
        body.SetParent(seat, false);
        Transform upper = new GameObject("Head_And_Arms").transform;
        upper.SetParent(body, false);
        headAndArms = upper.gameObject;

        // 몸통 / 머리
        Box("Pelvis", body, new Vector3(0, 0.54f, 0), new Vector3(0.34f, 0.14f, 0.22f), fabric);
        Capsule("Torso", body, new Vector3(0, 0.88f, -0.02f), new Vector3(0.36f, 0.3f, 0.24f), fabric);
        Cylinder("Neck", upper, new Vector3(0, 1.22f, -0.02f), new Vector3(0.09f, 0.05f, 0.09f), skin);
        Sphere("Head", upper, new Vector3(0, 1.37f, 0), new Vector3(0.2f, 0.24f, 0.22f), skin);

        // 다리 (앉은 자세)
        foreach (int s in new[] { -1, 1 })
        {
            string side = s < 0 ? "L" : "R";
            Capsule($"Thigh_{side}", body, new Vector3(s * 0.1f, 0.55f, 0.2f), new Vector3(0.13f, 0.23f, 0.13f), fabric, new Vector3(90, 0, 0));
            Capsule($"Shin_{side}", body, new Vector3(s * 0.1f, 0.27f, 0.42f), new Vector3(0.11f, 0.24f, 0.11f), fabric);
            Box($"Foot_{side}", body, new Vector3(s * 0.1f, 0.03f, 0.48f), new Vector3(0.1f, 0.06f, 0.22f), plasticBlack);

            // 팔: 위팔은 살짝 앞으로, 아래팔은 탁자 위에 올림
            Capsule($"UpperArm_{side}", upper, new Vector3(s * 0.25f, 0.99f, 0.03f), new Vector3(0.1f, 0.15f, 0.1f), fabric, new Vector3(25, 0, 0));
            Capsule($"Forearm_{side}", upper, new Vector3(s * 0.22f, 0.82f, 0.24f), new Vector3(0.09f, 0.16f, 0.09f), fabric, new Vector3(90, 0, -s * 12));
            Sphere($"Hand_{side}", upper, new Vector3(s * 0.19f, 0.8f, 0.42f), new Vector3(0.08f, 0.05f, 0.1f), skin);
        }

        return BuildVest(body, name, accent);
    }

    private static BombVest BuildVest(Transform body, string ownerName, Color accent)
    {
        Transform vestRoot = new GameObject("BombVest").transform;
        vestRoot.SetParent(body, false);

        Box("Vest_Front", vestRoot, new Vector3(0, 0.92f, 0.12f), new Vector3(0.4f, 0.42f, 0.06f), vestCloth);
        Box("Vest_Back", vestRoot, new Vector3(0, 0.92f, -0.16f), new Vector3(0.4f, 0.42f, 0.05f), vestCloth);
        Box("Vest_Side_L", vestRoot, new Vector3(-0.2f, 0.9f, -0.02f), new Vector3(0.04f, 0.32f, 0.28f), vestCloth);
        Box("Vest_Side_R", vestRoot, new Vector3(0.2f, 0.9f, -0.02f), new Vector3(0.04f, 0.32f, 0.28f), vestCloth);
        Box("Vest_Strap_L", vestRoot, new Vector3(-0.11f, 1.15f, -0.02f), new Vector3(0.07f, 0.04f, 0.32f), strap);
        Box("Vest_Strap_R", vestRoot, new Vector3(0.11f, 1.15f, -0.02f), new Vector3(0.07f, 0.04f, 0.32f), strap);
        Box("Vest_Belt", vestRoot, new Vector3(0, 0.72f, -0.02f), new Vector3(0.43f, 0.04f, 0.33f), strap);

        // 플레이어 식별용 컬러 패치 (단말기 액센트 색과 짝)
        Material accentMat = accent == AccentA ? accentA : accentB;
        Box("Vest_IdPatch", vestRoot, new Vector3(0.14f, 1.08f, 0.152f), new Vector3(0.07f, 0.04f, 0.005f), accentMat, collider: false);

        // 다이너마이트 6개 + 테이프
        for (int i = 0; i < 6; i++)
            Cylinder($"Dynamite_{i}", vestRoot, new Vector3(-0.15f + i * 0.06f, 0.88f, 0.177f), new Vector3(0.052f, 0.11f, 0.052f), dynamite);
        Box("Tape_Upper", vestRoot, new Vector3(0, 0.95f, 0.18f), new Vector3(0.37f, 0.025f, 0.06f), tape, collider: false);
        Box("Tape_Lower", vestRoot, new Vector3(0, 0.81f, 0.18f), new Vector3(0.37f, 0.025f, 0.06f), tape, collider: false);

        // 타이머 / 수신 모듈
        Box("Receiver_Box", vestRoot, new Vector3(0, 1.04f, 0.175f), new Vector3(0.13f, 0.07f, 0.045f), plasticBlack);
        Box("Receiver_Display", vestRoot, new Vector3(-0.015f, 1.04f, 0.1985f), new Vector3(0.08f, 0.035f, 0.003f), ledRed, collider: false);
        Renderer led = Sphere("Status_LED", vestRoot, new Vector3(0.045f, 1.04f, 0.199f), Vector3.one * 0.016f, ledRed, collider: false);
        Cylinder("Receiver_Antenna", vestRoot, new Vector3(0.055f, 1.1f, 0.175f), new Vector3(0.006f, 0.04f, 0.006f), plasticBlack, collider: false);

        // 타이머 → 다이너마이트 전선
        Material[] wires = { wireRed, wireBlue, wireYellow };
        for (int i = 0; i < 3; i++)
            Cylinder($"Wire_{i}", vestRoot, new Vector3(-0.04f + i * 0.04f, 0.985f, 0.205f), new Vector3(0.007f, 0.035f, 0.007f), wires[i],
                new Vector3(0, 0, (i - 1) * -25f), collider: false);

        // 목숨 계기판: 수신 모듈 위 LED 바 (상대가 맞은편에서, 본인은 고개를 숙여 확인)
        Transform gaugeRoot = new GameObject("LifeGauge").transform;
        gaugeRoot.SetParent(vestRoot, false);
        gaugeRoot.localPosition = new Vector3(0, 1.1f, 0.165f);
        Box("Gauge_Housing", gaugeRoot, Vector3.zero, new Vector3(0.03f + LifeCount * 0.03f, 0.035f, 0.02f), plasticBlack, collider: false);
        var lifeLeds = new Renderer[LifeCount];
        for (int i = 0; i < LifeCount; i++)
            lifeLeds[i] = Box($"Life_LED_{i}", gaugeRoot, new Vector3((i - (LifeCount - 1) / 2f) * 0.03f, 0, 0.0105f),
                new Vector3(0.022f, 0.02f, 0.003f), ledGreen, collider: false);
        LifeGauge gauge = gaugeRoot.gameObject.AddComponent<LifeGauge>();
        var gaugeSo = new SerializedObject(gauge);
        SetArray(gaugeSo.FindProperty("leds"), lifeLeds);
        gaugeSo.FindProperty("onMaterial").objectReferenceValue = ledGreen;
        gaugeSo.FindProperty("offMaterial").objectReferenceValue = ledOff;
        gaugeSo.ApplyModifiedPropertiesWithoutUndo();

        Light glow = new GameObject("LED_Glow").AddComponent<Light>();
        glow.transform.SetParent(vestRoot, false);
        glow.transform.localPosition = new Vector3(0, 1.04f, 0.25f);
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.1f, 0.05f);
        glow.intensity = 0.4f;
        glow.range = 0.6f;
        glow.shadows = LightShadows.None;

        BombVest vest = vestRoot.gameObject.AddComponent<BombVest>();
        var so = new SerializedObject(vest);
        so.FindProperty("ownerId").stringValue = ownerName.Replace("Player_", "");
        so.FindProperty("statusLed").objectReferenceValue = led;
        so.FindProperty("lifeGauge").objectReferenceValue = gauge;
        so.ApplyModifiedPropertiesWithoutUndo();
        return vest;
    }

    // ───────────────────────── Terminal ─────────────────────────

    /// <summary>단말기 로컬 공간: 화면이 -z(앉은 플레이어 쪽)를 향함.</summary>
    private static DetonatorTerminal BuildTerminal(string name, Transform anchor, string holderId, BombVest target, Material targetAccent)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(anchor, false);

        Box("Base", t, new Vector3(0, 0.02f, 0), new Vector3(0.32f, 0.04f, 0.22f), plasticBlack);
        Box("Accent_Stripe", t, new Vector3(0, 0.02f, -0.1115f), new Vector3(0.32f, 0.012f, 0.004f), targetAccent, collider: false);

        // 키패드 3x4 (전화기 배열, 윗줄이 탁자 안쪽)
        string[] keyLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "*", "0", "#" };
        var keys = new PressableButton[keyLabels.Length];
        Transform keypad = new GameObject("Keypad").transform;
        keypad.SetParent(t, false);
        keypad.localPosition = new Vector3(-0.07f, 0.046f, -0.05f);
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 3; c++)
            {
                int i = r * 3 + c;
                Vector3 pos = new Vector3((c - 1) * 0.04f, 0, (1.5f - r) * 0.025f);
                Renderer key = Box($"Key_{keyLabels[i]}", keypad, pos, new Vector3(0.032f, 0.012f, 0.02f), metalDark);
                keys[i] = AddButton(key.gameObject, keyLabels[i], 0.004f);
                TopLabel(keyLabels[i], key.transform, 0.0028f, Color.white);
            }

        // 기폭 버튼 (가드 링 + 빨간 버튼)
        Cylinder("Trigger_Guard", t, new Vector3(0.09f, 0.044f, -0.05f), new Vector3(0.08f, 0.004f, 0.08f), buttonYellow, collider: false);
        Renderer trigger = Cylinder("Trigger_Button", t, new Vector3(0.09f, 0.055f, -0.05f), new Vector3(0.055f, 0.012f, 0.055f), buttonRed);
        PressableButton triggerButton = AddButton(trigger.gameObject, "DETONATE", 0.006f);

        // 기울어진 화면부
        Transform screen = new GameObject("Screen").transform;
        screen.SetParent(t, false);
        screen.localPosition = new Vector3(0, 0.13f, 0.07f);
        screen.localRotation = Quaternion.Euler(20, 0, 0);
        Box("Screen_Housing", screen, Vector3.zero, new Vector3(0.3f, 0.17f, 0.025f), plasticBlack);
        Box("Screen_Display", screen, new Vector3(0, 0, -0.0135f), new Vector3(0.26f, 0.13f, 0.002f), screenGreen, collider: false);
        Box("Screen_Stand", t, new Vector3(0, 0.07f, 0.09f), new Vector3(0.12f, 0.06f, 0.03f), plasticBlack);

        TextMesh text = new GameObject("Screen_Label").AddComponent<TextMesh>();
        text.transform.SetParent(screen, false);
        text.transform.localPosition = new Vector3(0, 0, -0.016f);
        text.text = $"TARGET : PLAYER {target.OwnerId}\n<size=34>LINK : ARMED</size>\n<size=34>> _</size>";
        text.richText = true;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 48;
        text.characterSize = 0.004f;
        text.color = new Color(0.02f, 0.08f, 0.02f);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.font = font;
        text.GetComponent<MeshRenderer>().sharedMaterial = font.material;

        Cylinder("Antenna", t, new Vector3(-0.13f, 0.28f, 0.1f), new Vector3(0.01f, 0.08f, 0.01f), metalDark, collider: false);
        Sphere("Antenna_Tip", t, new Vector3(-0.13f, 0.365f, 0.1f), Vector3.one * 0.018f, ledRed, collider: false);

        Light glow = new GameObject("Screen_Glow").AddComponent<Light>();
        glow.transform.SetParent(screen, false);
        glow.transform.localPosition = new Vector3(0, 0, -0.12f);
        glow.type = LightType.Point;
        glow.color = new Color(0.3f, 1f, 0.4f);
        glow.intensity = 0.5f;
        glow.range = 0.9f;
        glow.shadows = LightShadows.None;

        DetonatorWire[] wires = BuildWireBay(anchor, new Vector3(0.27f, 0, 0));
        PressableButton[] answers = BuildAnswerPanel(anchor);

        DetonatorTerminal terminal = t.gameObject.AddComponent<DetonatorTerminal>();
        var so = new SerializedObject(terminal);
        so.FindProperty("holderId").stringValue = holderId;
        so.FindProperty("targetVest").objectReferenceValue = target;
        SetArray(so.FindProperty("wires"), wires);
        SetArray(so.FindProperty("keypadKeys"), keys);
        SetArray(so.FindProperty("answerButtons"), answers);
        so.FindProperty("triggerButton").objectReferenceValue = triggerButton;
        so.FindProperty("screenText").objectReferenceValue = text;
        so.ApplyModifiedPropertiesWithoutUndo();
        return terminal;
    }

    private static PressableButton AddButton(GameObject go, string label, float travel)
    {
        PressableButton button = go.AddComponent<PressableButton>();
        var so = new SerializedObject(button);
        so.FindProperty("label").stringValue = label;
        so.FindProperty("travel").floatValue = travel;
        so.ApplyModifiedPropertiesWithoutUndo();
        return button;
    }

    /// <summary>버튼 윗면에 위에서 읽히는 라벨을 붙인다. 버튼과 함께 눌리도록 버튼 자식으로 두되 스케일은 상쇄한다.</summary>
    private static void TopLabel(string label, Transform button, float charSize, Color color, float topY = 0.5f)
    {
        TextMesh text = new GameObject("Label").AddComponent<TextMesh>();
        Transform tr = text.transform;
        tr.SetParent(button, false);
        tr.localPosition = new Vector3(0, topY + 0.05f, 0); // 큐브 윗면 0.5, 실린더 윗면 1.0
        tr.localRotation = Quaternion.Euler(90, 0, 0);
        Vector3 s = button.lossyScale;
        tr.localScale = new Vector3(1f / s.x, 1f / s.z, 1f / s.y); // 회전 후 축 기준으로 상쇄
        text.text = label;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 48;
        text.characterSize = charSize;
        text.color = color;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.font = font;
        text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }

    /// <summary>
    /// 번갈아 자르기 모드의 기폭장치: 단말기 없이 전선 기판만 플레이어 정면 중앙에 둔다.
    /// 게임 로직용 DetonatorTerminal 컴포넌트는 기판 루트에 붙인다(화면/버튼 없음).
    /// </summary>
    private static DetonatorTerminal BuildWireBoard(string name, Transform anchor, string holderId, BombVest target, Material targetAccent)
    {
        Transform board = new GameObject(name).transform;
        board.SetParent(anchor, false);

        // 정면 중앙이라 전선 간격을 넓혀 조준/절단하기 쉽게
        DetonatorWire[] wires = BuildWireBay(board, Vector3.zero, spacing: 0.036f, length: 0.2f, depth: 0.26f);
        float width = WireCount * 0.036f + 0.02f;
        Box("Accent_Stripe", board, new Vector3(0, 0.01f, -0.1315f), new Vector3(width, 0.012f, 0.004f), targetAccent, collider: false);

        DetonatorTerminal terminal = board.gameObject.AddComponent<DetonatorTerminal>();
        var so = new SerializedObject(terminal);
        so.FindProperty("holderId").stringValue = holderId;
        so.FindProperty("targetVest").objectReferenceValue = target;
        SetArray(so.FindProperty("wires"), wires);
        so.ApplyModifiedPropertiesWithoutUndo();
        return terminal;
    }

    /// <summary>전선함. 전선 m개가 플레이어 쪽(-z)으로 뻗어 있고, 각각 색으로 구분된다.</summary>
    private static DetonatorWire[] BuildWireBay(Transform parent, Vector3 position, float spacing = 0.022f, float length = 0.16f, float depth = 0.22f)
    {
        Transform bay = new GameObject("Wire_Bay").transform;
        bay.SetParent(parent, false);
        bay.localPosition = position;

        float width = WireCount * spacing + 0.02f;
        float post = depth / 2f - 0.02f;
        Box("Bay_Base", bay, new Vector3(0, 0.01f, 0), new Vector3(width, 0.02f, depth), plasticBlack);
        Box("Bay_Terminal_Back", bay, new Vector3(0, 0.035f, post), new Vector3(width, 0.03f, 0.025f), metalDark, collider: false);
        Box("Bay_Terminal_Front", bay, new Vector3(0, 0.035f, -post), new Vector3(width, 0.03f, 0.025f), metalDark, collider: false);

        var wires = new DetonatorWire[WireCount];
        for (int i = 0; i < WireCount; i++)
        {
            float x = (i - (WireCount - 1) / 2f) * spacing;
            Renderer r = Cylinder($"Wire_{i}_{WireColors[i].name}", bay, new Vector3(x, 0.045f, 0),
                new Vector3(0.008f, length / 2f, 0.008f), wireMats[i], new Vector3(90, 0, 0));
            // 절단 판정용으로 콜라이더를 조금 두껍게
            ((CapsuleCollider)r.GetComponent<Collider>()).radius = 1.2f;

            DetonatorWire wire = r.gameObject.AddComponent<DetonatorWire>();
            var so = new SerializedObject(wire);
            so.FindProperty("index").intValue = i;
            so.FindProperty("colorName").stringValue = WireColors[i].name;
            so.FindProperty("wireRenderer").objectReferenceValue = r;
            so.ApplyModifiedPropertiesWithoutUndo();
            wires[i] = wire;
        }
        return wires;
    }

    /// <summary>단말기 왼쪽의 객관식(A~D) 정답 버튼.</summary>
    private static PressableButton[] BuildAnswerPanel(Transform anchor)
    {
        Transform panel = new GameObject("Answer_Panel").transform;
        panel.SetParent(anchor, false);
        panel.localPosition = new Vector3(-0.27f, 0, 0);

        Box("Panel_Base", panel, new Vector3(0, 0.015f, 0), new Vector3(0.18f, 0.03f, 0.18f), plasticBlack);
        var buttons = new PressableButton[AnswerColors.Length];
        for (int i = 0; i < AnswerColors.Length; i++)
        {
            string label = ((char)('A' + i)).ToString();
            float x = (i % 2 == 0 ? -1 : 1) * 0.042f;
            float z = (i < 2 ? 1 : -1) * 0.042f;
            Renderer r = Cylinder($"Answer_Button_{label}", panel, new Vector3(x, 0.04f, z), new Vector3(0.06f, 0.012f, 0.06f), answerMats[i]);
            buttons[i] = AddButton(r.gameObject, label, 0.006f);
            TopLabel(label, r.transform, 0.005f, Color.black, topY: 1f);
        }
        return buttons;
    }

    // ───────────────────────── Monitor ─────────────────────────

    /// <summary>탁자 옆의 공용 문제 모니터.</summary>
    private static void BuildQuizMonitor()
    {
        TextMesh text = BuildMonitor("Quiz_Monitor", "Q. 17 x 3 = ?\nA. 41\nB. 51\nC. 54\nD. 57");
        QuizMonitor monitor = text.transform.root.gameObject.AddComponent<QuizMonitor>();
        var so = new SerializedObject(monitor);
        so.FindProperty("questionText").objectReferenceValue = text;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>탁자 옆(+x)에 서 있는 공용 모니터. 두 플레이어 모두 옆으로 고개를 돌려 본다.</summary>
    private static TextMesh BuildMonitor(string name, string initialText)
    {
        Transform root = new GameObject(name).transform;
        root.position = new Vector3(0.85f, 0, 0);
        root.rotation = Quaternion.Euler(0, 90, 0); // 화면(-z)이 탁자 중심(-x)을 향함

        Box("Monitor_Foot", root, new Vector3(0, 0.02f, 0), new Vector3(0.4f, 0.04f, 0.3f), metalDark);
        Cylinder("Monitor_Pole", root, new Vector3(0, 0.6f, 0.05f), new Vector3(0.05f, 0.6f, 0.05f), metalDark);

        Transform screen = new GameObject("Screen").transform;
        screen.SetParent(root, false);
        screen.localPosition = new Vector3(0, 1.3f, 0);
        Box("Screen_Housing", screen, Vector3.zero, new Vector3(0.75f, 0.45f, 0.05f), plasticBlack);
        Box("Screen_Display", screen, new Vector3(0, 0, -0.026f), new Vector3(0.7f, 0.4f, 0.002f), screenBlue, collider: false);

        TextMesh text = new GameObject("Screen_Text").AddComponent<TextMesh>();
        text.transform.SetParent(screen, false);
        text.transform.localPosition = new Vector3(-0.32f, 0.17f, -0.03f);
        text.text = initialText;
        text.anchor = TextAnchor.UpperLeft;
        text.alignment = TextAlignment.Left;
        text.fontSize = 64;
        text.characterSize = 0.0075f;
        text.color = Color.white;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.font = font;
        text.GetComponent<MeshRenderer>().sharedMaterial = font.material;

        Light glow = new GameObject("Screen_Glow").AddComponent<Light>();
        glow.transform.SetParent(screen, false);
        glow.transform.localPosition = new Vector3(0, 0, -0.3f);
        glow.type = LightType.Point;
        glow.color = new Color(0.4f, 0.6f, 1f);
        glow.intensity = 0.6f;
        glow.range = 1.5f;
        glow.shadows = LightShadows.None;
        return text;
    }

    private static void AddSceneToBuildSettings(string scenePath)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == scenePath)) return;
        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void SetArray(SerializedProperty prop, Object[] items)
    {
        prop.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    // ───────────────────────── Camera ─────────────────────────

    /// <summary>플레이어 시점의 1인칭 데스크톱 카메라. 단말기를 내려다보는 방향으로 시작한다.</summary>
    /// <param name="hideHeadAndArms">true면 시작 시 이 좌석의 머리/팔을 숨긴다. (좌석이 바뀌는 모드는 게임 매니저가 처리)</param>
    private static DesktopFirstPersonController BuildCamera(Seat seat, bool hideHeadAndArms)
    {
        GameObject go = new GameObject("Main Camera") { tag = "MainCamera" };
        Camera cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane = 0.02f;
        cam.fieldOfView = 70f;
        go.AddComponent<AudioListener>();
        go.transform.position = seat.EyeAnchor.position;
        go.transform.rotation = seat.EyeAnchor.rotation * Quaternion.Euler(35f, 0f, 0f);

        DesktopFirstPersonController controller = go.AddComponent<DesktopFirstPersonController>();
        var so = new SerializedObject(controller);
        SetArray(so.FindProperty("hideInFirstPerson"), hideHeadAndArms ? new Object[] { seat.HeadAndArms } : new Object[0]);
        so.ApplyModifiedPropertiesWithoutUndo();
        return controller;
    }

    // ───────────────────────── Materials ─────────────────────────

    private static void CreateMaterials()
    {
        EnsureFolder(MaterialDir);
        concreteFloor = Mat("Concrete_Floor", new Color(0.18f, 0.18f, 0.17f), 0.15f);
        concreteWall = Mat("Concrete_Wall", new Color(0.13f, 0.13f, 0.13f), 0.05f);
        metalDark = Mat("Metal_Dark", new Color(0.12f, 0.12f, 0.13f), 0.55f, 0.8f);
        doorMetal = Mat("Metal_Door", new Color(0.2f, 0.17f, 0.14f), 0.35f, 0.7f);
        wood = Mat("Table_Wood", new Color(0.24f, 0.15f, 0.09f), 0.3f);
        fabric = Mat("Mannequin_Fabric", new Color(0.22f, 0.22f, 0.24f), 0.1f);
        skin = Mat("Mannequin_Skin", new Color(0.55f, 0.45f, 0.38f), 0.25f);
        vestCloth = Mat("Vest_Cloth", new Color(0.2f, 0.22f, 0.13f), 0.1f);
        strap = Mat("Vest_Strap", new Color(0.06f, 0.06f, 0.05f), 0.2f);
        dynamite = Mat("Dynamite", new Color(0.55f, 0.06f, 0.04f), 0.35f);
        tape = Mat("Tape", new Color(0.12f, 0.12f, 0.12f), 0.6f);
        plasticBlack = Mat("Plastic_Black", new Color(0.04f, 0.04f, 0.045f), 0.5f);
        buttonRed = Mat("Button_Red", new Color(0.8f, 0.05f, 0.03f), 0.7f, 0f, new Color(0.6f, 0.02f, 0.01f));
        buttonYellow = Mat("Button_Yellow", new Color(0.85f, 0.65f, 0.05f), 0.5f);
        wireRed = Mat("Wire_Red", new Color(0.8f, 0.05f, 0.05f), 0.5f);
        wireBlue = Mat("Wire_Blue", new Color(0.05f, 0.15f, 0.8f), 0.5f);
        wireYellow = Mat("Wire_Yellow", new Color(0.85f, 0.75f, 0.05f), 0.5f);
        ledRed = Mat("LED_Red", Color.black, 0.8f, 0f, new Color(1f, 0.05f, 0.02f) * 4f);
        ledGreen = Mat("LED_Green", Color.black, 0.8f, 0f, new Color(0.1f, 1f, 0.2f) * 3f);
        ledOff = Mat("LED_Off", new Color(0.05f, 0.06f, 0.05f), 0.8f);
        screenGreen = Mat("Screen_Green", Color.black, 0.9f, 0f, new Color(0.15f, 1f, 0.3f) * 1.5f);
        screenBlue = Mat("Screen_Blue", Color.black, 0.9f, 0f, new Color(0.05f, 0.12f, 0.35f) * 1.5f);

        // 기폭장치 전선은 어두운 방에서도 구분되도록 약하게 자체 발광
        wireMats = new Material[WireColors.Length];
        for (int i = 0; i < WireColors.Length; i++)
            wireMats[i] = Mat($"DetWire_{WireColors[i].name}", WireColors[i].color, 0.5f, 0f, WireColors[i].color * 0.4f);
        answerMats = new Material[AnswerColors.Length];
        for (int i = 0; i < AnswerColors.Length; i++)
            answerMats[i] = Mat($"Answer_{(char)('A' + i)}", AnswerColors[i], 0.6f, 0f, AnswerColors[i] * 0.8f);
        bulbWarm = Mat("Bulb_Warm", Color.white, 0.5f, 0f, new Color(1f, 0.8f, 0.55f) * 6f);
        accentA = Mat("Accent_PlayerA", AccentA * 0.3f, 0.5f, 0f, AccentA * 2f);
        accentB = Mat("Accent_PlayerB", AccentB * 0.3f, 0.5f, 0f, AccentB * 2f);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static Material Mat(string name, Color baseColor, float smoothness, float metallic = 0f, Color? emission = null)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }

        m.SetColor("_BaseColor", baseColor);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            m.DisableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    // ───────────────────────── Primitive helpers ─────────────────────────

    private static Renderer Box(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider = true)
        => Prim(PrimitiveType.Cube, name, parent, pos, scale, mat, Vector3.zero, collider);

    private static Renderer Sphere(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider = true)
        => Prim(PrimitiveType.Sphere, name, parent, pos, scale, mat, Vector3.zero, collider);

    private static Renderer Cylinder(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default, bool collider = true)
        => Prim(PrimitiveType.Cylinder, name, parent, pos, scale, mat, euler, collider);

    private static Renderer Capsule(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default, bool collider = true)
        => Prim(PrimitiveType.Capsule, name, parent, pos, scale, mat, euler, collider);

    private static Renderer Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler, bool collider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        return r;
    }

    private static void SetStaticRecursive(GameObject go)
    {
        go.isStatic = true;
        foreach (Transform child in go.transform) SetStaticRecursive(child.gameObject);
    }
}
