using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// RoomScene에 LAN 멀티플레이 구성을 한 번에 만든다. 이미 있는 것은 건너뛰므로 여러 번 실행해도 된다.
/// 만드는 것: NetworkPlayer 프리팹, NetworkManager(+ UnityTransport, LanSession), SeatRegistry, 임시 접속 패널, Build Settings 등록.
/// </summary>
public static class RoomNetworkSetup
{
    const string k_ScenePath = "Assets/_Project/Scenes/RoomScene.unity";
    const string k_PlayerPrefabPath = "Assets/_Project/Prefabs/NetworkPlayer.prefab";
    const string k_ButtonMaterialPath = "Assets/_Project/Art/Material/Terminal_Plate.mat";
    const string k_OutlineMaterialPath = "Assets/_Project/Art/Material/Outline.mat";
    const string k_AvatarMaterialPath = "Assets/_Project/Art/Material/Avatar.mat";

    [MenuItem("BombDefuse/Set Up Room Network")]
    static void SetUpFromMenu()
    {
        var summary = SetUp();
        EditorUtility.DisplayDialog("Set Up Room Network", summary + "\n\nSave the scene (Ctrl+S).", "OK");
    }

    /// <summary>대화상자 없이 실행하고 결과 요약을 돌려준다. 씬 저장은 하지 않는다.</summary>
    public static string SetUp()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != k_ScenePath)
            return $"Open {k_ScenePath} first.";

        var log = new List<string>();
        var playerPrefab = GetOrCreatePlayerPrefab(log);
        AddAvatarBody(log);
        SetUpNetworkManager(playerPrefab, log);
        SetUpSeatRegistry(log);
        SetUpSessionPanel(log);
        SetUpGameStart(log);
        AddSceneToBuildSettings(log);

        EditorSceneManager.MarkSceneDirty(scene);
        var summary = log.Count > 0 ? string.Join("\n", log) : "Everything is already set up.";
        Debug.Log($"[RoomNetworkSetup]\n{summary}");
        return summary;
    }

    static GameObject GetOrCreatePlayerPrefab(List<string> log)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(k_PlayerPrefabPath);
        if (existing != null)
            return existing;

        var root = new GameObject("NetworkPlayer");
        root.AddComponent<NetworkObject>();
        var player = root.AddComponent<NetworkPlayer>();

        var avatarMaterial = GetOrCreateAvatarMaterial(log);
        var visorMaterial = AssetDatabase.LoadAssetAtPath<Material>(k_ButtonMaterialPath);
        var head = CreateTrackedPart(root.transform, "Head", new Vector3(0.18f, 0.22f, 0.2f), avatarMaterial);
        // 얼굴 방향이 보이도록 앞면에 눈가리개를 붙인다
        var visor = CreatePrimitive(PrimitiveType.Cube, "Visor", head, new Vector3(0f, 0.02f, 0.1f), new Vector3(0.16f, 0.05f, 0.02f));
        if (visorMaterial != null)
            visor.GetComponent<MeshRenderer>().sharedMaterial = visorMaterial;
        var leftHand = CreateTrackedPart(root.transform, "LeftHand", new Vector3(0.08f, 0.04f, 0.12f), avatarMaterial);
        var rightHand = CreateTrackedPart(root.transform, "RightHand", new Vector3(0.08f, 0.04f, 0.12f), avatarMaterial);

        var serialized = new SerializedObject(player);
        serialized.FindProperty("m_Head").objectReferenceValue = head;
        serialized.FindProperty("m_LeftHand").objectReferenceValue = leftHand;
        serialized.FindProperty("m_RightHand").objectReferenceValue = rightHand;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_PlayerPrefabPath);
        Object.DestroyImmediate(root);
        log.Add($"Created {k_PlayerPrefabPath}");
        return prefab;
    }

    // 상대 아바타가 총에 맞을 수 있게 몸(Damageable + AvatarBody)을 붙인다
    static void AddAvatarBody(List<string> log)
    {
        var root = PrefabUtility.LoadPrefabContents(k_PlayerPrefabPath);
        try
        {
            if (root.GetComponent<AvatarBody>() != null)
                return;

            var damageable = root.AddComponent<Damageable>();
            var config = AssetDatabase.FindAssets("t:TuningConfig")
                .Select(guid => AssetDatabase.LoadAssetAtPath<TuningConfig>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault();
            var damageableSerialized = new SerializedObject(damageable);
            damageableSerialized.FindProperty("m_Config").objectReferenceValue = config;
            damageableSerialized.ApplyModifiedPropertiesWithoutUndo();

            var body = root.AddComponent<AvatarBody>();
            var bodySerialized = new SerializedObject(body);
            bodySerialized.FindProperty("m_Head").objectReferenceValue = root.transform.Find("Head");
            bodySerialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, k_PlayerPrefabPath);
            log.Add($"Added AvatarBody to {k_PlayerPrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 방이 어두워서 조명을 받으면 거의 보이지 않는다. 조명과 관계없이 밝은 회색으로 그린다
    static Material GetOrCreateAvatarMaterial(List<string> log)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(k_AvatarMaterialPath);
        if (material != null)
            return material;

        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.55f));
        AssetDatabase.CreateAsset(material, k_AvatarMaterialPath);
        log.Add($"Created {k_AvatarMaterialPath}");
        return material;
    }

    // 주인 권한 NetworkTransform이 붙은 빈 오브젝트 + 회색 상자 모양
    static Transform CreateTrackedPart(Transform parent, string name, Vector3 size, Material material)
    {
        var part = new GameObject(name).transform;
        part.SetParent(parent, false);

        var networkTransform = part.gameObject.AddComponent<NetworkTransform>();
        networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        networkTransform.InLocalSpace = false;
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;

        var shape = CreatePrimitive(PrimitiveType.Cube, "Shape", part, Vector3.zero, size);
        if (material != null)
            shape.GetComponent<MeshRenderer>().sharedMaterial = material;
        return part;
    }

    static Transform CreatePrimitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale)
    {
        var shape = GameObject.CreatePrimitive(type);
        shape.name = name;
        // 아바타 모양은 총알·손 레이를 막지 않는다 (피격 판정은 나중에 히트박스로 따로 만든다)
        Object.DestroyImmediate(shape.GetComponent<Collider>());
        shape.transform.SetParent(parent, false);
        shape.transform.localPosition = localPosition;
        shape.transform.localScale = localScale;
        return shape.transform;
    }

    static void SetUpNetworkManager(GameObject playerPrefab, List<string> log)
    {
        var networkManager = Object.FindAnyObjectByType<NetworkManager>();
        if (networkManager == null)
        {
            var go = new GameObject("NetworkManager");
            Undo.RegisterCreatedObjectUndo(go, "Create NetworkManager");
            networkManager = go.AddComponent<NetworkManager>();
            log.Add("Created NetworkManager");
        }

        var managerObject = networkManager.gameObject;
        var transport = managerObject.GetComponent<UnityTransport>();
        if (transport == null)
            transport = managerObject.AddComponent<UnityTransport>();
        if (managerObject.GetComponent<LanSession>() == null)
        {
            managerObject.AddComponent<LanSession>();
            log.Add("Added LanSession");
        }

        networkManager.NetworkConfig ??= new NetworkConfig();
        var config = networkManager.NetworkConfig;
        config.NetworkTransport = transport;
        config.PlayerPrefab = playerPrefab;
        // 호스트가 모든 판정을 한다 (docs/03_tech_review.md 3절). 템플릿의 Distributed Authority는 쓰지 않는다
        config.NetworkTopology = NetworkTopologyTypes.ClientServer;
        config.ConnectionApproval = true;
        EditorUtility.SetDirty(networkManager);
    }

    static void SetUpSeatRegistry(List<string> log)
    {
        var seatPoints = GameObject.Find("SeatPoints");
        if (seatPoints == null)
        {
            log.Add("WARNING: No SeatPoints object. Add a SeatRegistry by hand.");
            return;
        }

        var registry = Object.FindAnyObjectByType<SeatRegistry>();
        if (registry == null)
            registry = Undo.AddComponent<SeatRegistry>(seatPoints);

        var serialized = new SerializedObject(registry);
        var array = serialized.FindProperty("m_Seats");
        var isFilled = array.arraySize > 0;
        for (var i = 0; i < array.arraySize; i++)
            isFilled &= array.GetArrayElementAtIndex(i).objectReferenceValue != null;
        if (isFilled)
            return;

        // SeatPoints의 자식 순서가 좌석 번호다 (SeatPoint_1 → 좌석 1). 각 지점에서 가장 가까운 의자를 고른다
        var allSeats = Object.FindObjectsByType<Seat>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var seats = new List<Seat>();
        foreach (Transform point in seatPoints.transform)
        {
            var nearest = allSeats.OrderBy(s => (s.transform.position - point.position).sqrMagnitude).FirstOrDefault();
            if (nearest != null && !seats.Contains(nearest))
                seats.Add(nearest);
        }

        array.arraySize = seats.Count;
        for (var i = 0; i < seats.Count; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = seats[i];
        serialized.ApplyModifiedProperties();
        log.Add($"SeatRegistry on SeatPoints: {string.Join(", ", seats.Select(s => s.name))}");
    }

    static void SetUpSessionPanel(List<string> log)
    {
        var panelObject = GameObject.Find("LanSessionPanel");
        var panel = panelObject != null ? panelObject.transform : CreateSessionPanel(log);

        var hasStartButton = panel.GetComponentsInChildren<LanSessionButton>(true)
            .Any(b => new SerializedObject(b).FindProperty("m_Command").enumValueIndex == (int)LanSessionButton.Command.StartGame);
        if (!hasStartButton)
        {
            // 접속 버튼 줄 아래에 넓은 START 버튼을 두고, 판을 그만큼 늘린다
            var board = panel.Find("Board");
            if (board != null)
            {
                board.localPosition = new Vector3(0f, -0.09f, 0.03f);
                board.localScale = new Vector3(0.9f, 0.68f, 0.02f);
            }
            CreateButton(panel, LanSessionButton.Command.StartGame, "START", "Press Trigger to start the game", new Vector2(0f, -0.3f), 0.5f);
            log.Add("Added START button to LanSessionPanel");
        }

        LinkButtonLabels(panel);
    }

    static Transform CreateSessionPanel(List<string> log)
    {
        var origin = Object.FindAnyObjectByType<XROrigin>();
        var originTransform = origin != null ? origin.transform : null;
        var basePosition = originTransform != null ? originTransform.position : Vector3.zero;
        var forward = originTransform != null ? originTransform.forward : Vector3.forward;
        var right = originTransform != null ? originTransform.right : Vector3.right;

        // 시작 위치에서 오른쪽 앞, 눈높이보다 조금 아래. 앞면(-Z)이 시작 위치를 본다
        var position = basePosition + forward * 0.9f + right * 0.8f + Vector3.up * 1.35f;
        var eye = basePosition + Vector3.up * 1.6f;
        var toPanel = Vector3.ProjectOnPlane(position - eye, Vector3.up);

        var panel = new GameObject("LanSessionPanel").transform;
        Undo.RegisterCreatedObjectUndo(panel.gameObject, "Create LanSessionPanel");
        panel.SetPositionAndRotation(position, Quaternion.LookRotation(toPanel, Vector3.up));

        var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "Board";
        Object.DestroyImmediate(board.GetComponent<Collider>());
        board.transform.SetParent(panel, false);
        board.transform.localPosition = new Vector3(0f, 0f, 0.03f);
        board.transform.localScale = new Vector3(0.9f, 0.5f, 0.02f);

        var status = CreateText(panel, "Status", new Vector3(0f, 0.12f, -0.01f), new Vector2(0.8f, 0.18f), "Offline");
        status.gameObject.AddComponent<LanSessionStatusText>();
        var statusSerialized = new SerializedObject(status.GetComponent<LanSessionStatusText>());
        statusSerialized.FindProperty("m_Text").objectReferenceValue = status;
        statusSerialized.ApplyModifiedPropertiesWithoutUndo();

        CreateButton(panel, LanSessionButton.Command.Host, "HOST", "Press Trigger to host", new Vector2(-0.28f, -0.1f), 0.24f);
        CreateButton(panel, LanSessionButton.Command.Join, "JOIN", "Press Trigger to join", new Vector2(0f, -0.1f), 0.24f);
        CreateButton(panel, LanSessionButton.Command.Leave, "LEAVE", "Press Trigger to leave", new Vector2(0.28f, -0.1f), 0.24f);

        log.Add("Created LanSessionPanel (move it if it blocks anything)");
        return panel;
    }

    static void CreateButton(Transform panel, LanSessionButton.Command command, string label, string prompt, Vector2 position, float width)
    {
        var button = GameObject.CreatePrimitive(PrimitiveType.Cube);
        button.name = $"Button_{label}";
        button.transform.SetParent(panel, false);
        button.transform.localPosition = new Vector3(position.x, position.y, 0f);
        button.transform.localScale = new Vector3(width, 0.12f, 0.04f);
        var buttonMaterial = AssetDatabase.LoadAssetAtPath<Material>(k_ButtonMaterialPath);
        if (buttonMaterial != null)
            button.GetComponent<MeshRenderer>().sharedMaterial = buttonMaterial;

        button.AddComponent<XRSimpleInteractable>();
        var sessionButton = button.AddComponent<LanSessionButton>();
        var serialized = new SerializedObject(sessionButton);
        serialized.FindProperty("m_Command").enumValueIndex = (int)command;
        serialized.FindProperty("m_Prompt").stringValue = prompt;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        var outlineMaterial = AssetDatabase.LoadAssetAtPath<Material>(k_OutlineMaterialPath);
        if (outlineMaterial != null)
        {
            var outline = button.AddComponent<HoverOutline>();
            var outlineSerialized = new SerializedObject(outline);
            outlineSerialized.FindProperty("m_OutlineMaterial").objectReferenceValue = outlineMaterial;
            outlineSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // 글자가 버튼 스케일을 따라 찌그러지지 않게 버튼이 아니라 패널의 자식으로 둔다
        CreateText(panel, $"Label_{label}", new Vector3(position.x, position.y, -0.025f), new Vector2(width - 0.02f, 0.1f), label);
    }

    // 누를 수 없을 때 글자를 흐리게 하도록 각 버튼에 같은 이름의 Label_ 글자를 연결한다
    static void LinkButtonLabels(Transform panel)
    {
        foreach (var button in panel.GetComponentsInChildren<LanSessionButton>(true))
        {
            var serialized = new SerializedObject(button);
            var labelProperty = serialized.FindProperty("m_Label");
            if (labelProperty.objectReferenceValue != null)
                continue;

            var label = panel.Find("Label_" + button.name.Replace("Button_", ""));
            if (label == null)
                continue;

            labelProperty.objectReferenceValue = label.GetComponent<TMP_Text>();
            serialized.ApplyModifiedProperties();
        }
    }

    // 게임은 Play하자마자가 아니라 호스트가 START를 누르면 시작한다
    static void SetUpGameStart(List<string> log)
    {
        var flow = Object.FindAnyObjectByType<GameFlowController>();
        if (flow == null)
        {
            log.Add("WARNING: No GameFlowController. The START button won't start anything.");
            return;
        }

        var flowSerialized = new SerializedObject(flow);
        var startOnPlay = flowSerialized.FindProperty("m_StartOnPlay");
        if (startOnPlay.boolValue)
        {
            startOnPlay.boolValue = false;
            flowSerialized.ApplyModifiedProperties();
            log.Add("GameFlowController now waits for START");
        }

        if (flow.GetComponent<LanGameStarter>() == null)
        {
            var starter = Undo.AddComponent<LanGameStarter>(flow.gameObject);
            var starterSerialized = new SerializedObject(starter);
            starterSerialized.FindProperty("m_Flow").objectReferenceValue = flow;
            starterSerialized.ApplyModifiedProperties();
            log.Add($"Added LanGameStarter to {flow.name}");
        }

        if (flow.GetComponent<LanQuizSync>() == null)
        {
            // 퀴즈 화면과 단말기는 진행 컨트롤러에 연결된 것을 그대로 쓴다
            var quizSync = Undo.AddComponent<LanQuizSync>(flow.gameObject);
            var syncSerialized = new SerializedObject(quizSync);
            syncSerialized.FindProperty("m_Flow").objectReferenceValue = flow;
            syncSerialized.FindProperty("m_Board").objectReferenceValue = flowSerialized.FindProperty("m_Board").objectReferenceValue;
            var flowTerminals = flowSerialized.FindProperty("m_Terminals");
            var syncTerminals = syncSerialized.FindProperty("m_Terminals");
            syncTerminals.arraySize = flowTerminals.arraySize;
            for (var i = 0; i < flowTerminals.arraySize; i++)
                syncTerminals.GetArrayElementAtIndex(i).objectReferenceValue = flowTerminals.GetArrayElementAtIndex(i).objectReferenceValue;
            syncSerialized.ApplyModifiedProperties();
            log.Add($"Added LanQuizSync to {flow.name}");
        }

        if (flow.GetComponent<LanGunSync>() == null)
        {
            var gunSync = Undo.AddComponent<LanGunSync>(flow.gameObject);
            var gunSerialized = new SerializedObject(gunSync);
            gunSerialized.FindProperty("m_Flow").objectReferenceValue = flow;
            gunSerialized.FindProperty("m_Gun").objectReferenceValue = flowSerialized.FindProperty("m_Gun").objectReferenceValue;
            gunSerialized.ApplyModifiedProperties();
            log.Add($"Added LanGunSync to {flow.name}");
        }
    }

    static TextMeshPro CreateText(Transform parent, string name, Vector3 localPosition, Vector2 size, string text)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.rectTransform.sizeDelta = size;
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 0.6f;
        tmp.color = Color.white;
        return tmp;
    }

    static void AddSceneToBuildSettings(List<string> log)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        var index = scenes.FindIndex(s => s.path == k_ScenePath);
        if (index == 0 && scenes[0].enabled)
            return;

        if (index >= 0)
            scenes.RemoveAt(index);
        // 빌드하면 처음 열리는 씬
        scenes.Insert(0, new EditorBuildSettingsScene(k_ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        log.Add("Put RoomScene first in Build Settings");
    }
}
