using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 효과음과 진행자 목소리를 연결하는 창 (메뉴 BombDefuse > Sound Hookup).
/// 파일 이름 규칙대로 폴더에 넣고 "폴더에서 자동 연결"을 누르면 이름으로 찾아 연결하고, 빠진 소리를 목록으로 보여 준다.
/// - 효과음: Audio/SFX/이벤트이름.wav (여러 개면 이벤트이름_1.wav, 이벤트이름_2.wav). 총 소리는 gun_hammer, gun_blank, gun_fire.
/// - 목소리: Audio/Voice/대사id.wav ({bullets} 대사의 숫자별 녹음은 대사id_숫자.wav)
/// Play 중에는 ▶ 버튼으로 그 이벤트를 이 기기에서 내서 소리를 들어볼 수 있다.
/// </summary>
public class SoundHookupWindow : EditorWindow
{
    const string k_AudioFolder = "Assets/_Project/Audio";
    const string k_SfxFolder = k_AudioFolder + "/SFX";
    const string k_VoiceFolder = k_AudioFolder + "/Voice";
    const string k_SoundBankPath = k_AudioFolder + "/SoundBank.asset";
    const string k_VoiceBankPath = k_AudioFolder + "/VoiceBank.asset";
    const string k_LinesCsvPath = "Assets/_Project/Data/Dialogue/host_lines.csv";
    const string k_RecordingListPath = k_VoiceFolder + "/recording_list.csv";
    const string k_HostObjectName = "Host";

    // 총 소리: 파일 이름 → GunEffects의 칸
    static readonly (string file, string field, string label)[] k_GunSounds =
    {
        ("gun_hammer", "m_HammerClip", "공이치기 (방아쇠)"),
        ("gun_blank", "m_BlankClip", "딸깍 (공포탄)"),
        ("gun_fire", "m_LiveClip", "발사 (실탄)"),
    };

    static readonly string[] k_Tabs = { "효과음", "진행자 목소리", "총 소리" };
    static readonly Regex k_NumberSuffix = new Regex(@"^(.+?)(?:_\d+| ?\(\d+\))$");

    SoundBank m_SoundBank;
    VoiceBank m_VoiceBank;
    int m_Tab;
    Vector2 m_Scroll;
    bool m_OnlyMissing;
    readonly HashSet<string> m_Expanded = new HashSet<string>();
    readonly List<string> m_Report = new List<string>();
    List<HostLine> m_Lines;

    [MenuItem("BombDefuse/Sound Hookup")]
    static void Open() => GetWindow<SoundHookupWindow>("사운드 연결");

    void OnEnable()
    {
        m_SoundBank = AssetDatabase.LoadAssetAtPath<SoundBank>(k_SoundBankPath);
        m_VoiceBank = AssetDatabase.LoadAssetAtPath<VoiceBank>(k_VoiceBankPath);
        m_Lines = null;
    }

    void OnGUI()
    {
        DrawSetup();
        EditorGUILayout.Space();
        m_Tab = GUILayout.Toolbar(m_Tab, k_Tabs);
        EditorGUILayout.Space();

        m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
        switch (m_Tab)
        {
            case 0: DrawSfx(); break;
            case 1: DrawVoice(); break;
            case 2: DrawGun(); break;
        }
        DrawReport();
        EditorGUILayout.EndScrollView();
    }

    // ── 준비 ──

    void DrawSetup()
    {
        EditorGUILayout.LabelField("준비", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            m_SoundBank = (SoundBank)EditorGUILayout.ObjectField("효과음 표", m_SoundBank, typeof(SoundBank), false);
            m_VoiceBank = (VoiceBank)EditorGUILayout.ObjectField("목소리 표", m_VoiceBank, typeof(VoiceBank), false);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button(new GUIContent("에셋·폴더 만들기", $"{k_AudioFolder}에 효과음 표, 목소리 표, SFX·Voice 폴더를 만든다 (있으면 건너뜀)")))
                    CreateAssets();
                if (GUILayout.Button(new GUIContent("열린 씬에 설치", "진행 컨트롤러가 있는 씬에 효과음 재생기, 이벤트 동기화를 붙이고 진행자에 목소리 표를 연결한다")))
                    InstallInScene();
            }
        }
    }

    void CreateAssets()
    {
        m_Report.Clear();
        EnsureFolder(k_AudioFolder);
        EnsureFolder(k_SfxFolder);
        EnsureFolder(k_VoiceFolder);

        if (m_SoundBank == null)
        {
            m_SoundBank = CreateInstance<SoundBank>();
            AssetDatabase.CreateAsset(m_SoundBank, k_SoundBankPath);
            m_Report.Add($"만듦: {k_SoundBankPath}");
        }
        if (m_VoiceBank == null)
        {
            m_VoiceBank = CreateInstance<VoiceBank>();
            AssetDatabase.CreateAsset(m_VoiceBank, k_VoiceBankPath);
            m_Report.Add($"만듦: {k_VoiceBankPath}");
        }

        var added = FillMissingEntries();
        if (added > 0)
            m_Report.Add($"효과음 칸 {added}개를 기본값으로 채움");
        AssetDatabase.SaveAssets();
        if (m_Report.Count == 0)
            m_Report.Add("이미 모두 있다");
    }

    void InstallInScene()
    {
        m_Report.Clear();
        if (m_SoundBank == null || m_VoiceBank == null)
            CreateAssets();

        var flow = FindAnyObjectByType<GameFlowController>();
        if (flow == null)
        {
            m_Report.Add("열린 씬에 GameFlowController가 없다");
            return;
        }

        var player = flow.GetComponent<EventSoundPlayer>();
        if (player == null)
        {
            player = Undo.AddComponent<EventSoundPlayer>(flow.gameObject);
            m_Report.Add($"{flow.name}에 EventSoundPlayer를 붙임");
        }
        SetReference(player, "m_Bank", m_SoundBank);
        SetReference(player, "m_Flow", flow);

        if (flow.GetComponent<LanEventSync>() == null)
        {
            Undo.AddComponent<LanEventSync>(flow.gameObject);
            m_Report.Add($"{flow.name}에 LanEventSync를 붙임");
        }

        var host = FindAnyObjectByType<HostDialogue>();
        if (host != null)
        {
            SetReference(host, "m_Voice", m_VoiceBank);
            InstallVoiceSource(host);
        }
        else
            m_Report.Add("씬에 HostDialogue가 없어 목소리 표를 연결하지 못했다");

        EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);
        m_Report.Add("연결 완료. 씬을 저장한다 (Ctrl+S)");
    }

    // 진행자 목소리는 진행자 모델(씬의 "Host" 오브젝트)에서 나게 한다
    void InstallVoiceSource(HostDialogue host)
    {
        var serialized = new SerializedObject(host);
        if (serialized.FindProperty("m_VoiceSource").objectReferenceValue != null)
            return;

        var model = GameObject.Find(k_HostObjectName);
        if (model == null)
        {
            m_Report.Add($"씬에 \"{k_HostObjectName}\" 오브젝트가 없어 진행자 목소리는 {host.name} 위치에서 난다");
            return;
        }

        var source = model.GetComponent<AudioSource>();
        if (source == null)
        {
            source = Undo.AddComponent<AudioSource>(model);
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 1.5f;
            source.maxDistance = 20f;
            m_Report.Add($"{model.name}에 목소리용 AudioSource를 붙임");
        }
        SetReference(host, "m_VoiceSource", source);
    }

    void SetReference(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(field);
        if (property.objectReferenceValue == value)
            return;
        property.objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
        m_Report.Add($"{target.name}.{field} ← {value.name}");
    }

    // 목록에 있는데 표에 없는 효과음 칸을 기본 위치로 만든다
    int FillMissingEntries()
    {
        if (m_SoundBank == null)
            return 0;

        Undo.RecordObject(m_SoundBank, "Fill sound entries");
        var added = 0;
        foreach (var info in GameEventCatalog.All)
        {
            if (!info.HasSound || m_SoundBank.Find(info.Name) != null)
                continue;
            m_SoundBank.Entries.Add(new SoundBank.Entry
            {
                EventName = info.Name,
                Location = info.DefaultLocation.Value,
                Audience = info.DefaultAudience,
            });
            added++;
        }

        // 07 문서 순서대로 정렬한다 (목록에 없는 것은 뒤로)
        var order = GameEventCatalog.All.Select(info => info.Name).ToList();
        m_SoundBank.Entries.Sort((a, b) => Order(a.EventName).CompareTo(Order(b.EventName)));
        int Order(string name) => order.IndexOf(name) is var index && index >= 0 ? index : int.MaxValue;

        EditorUtility.SetDirty(m_SoundBank);
        return added;
    }

    // ── 효과음 ──

    void DrawSfx()
    {
        if (m_SoundBank == null)
        {
            EditorGUILayout.HelpBox("효과음 표가 없다. 위의 \"에셋·폴더 만들기\"를 누른다.", MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox(
            $"파일을 {k_SfxFolder}에 이벤트 이름으로 넣는다 (예: player_seated.wav). 여러 개면 _1, _2를 붙인다. 하위 폴더도 찾는다.\n" +
            "위치·볼륨 등은 줄을 펼쳐서 고친다. Play 중에는 ▶로 들어볼 수 있다.", MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("폴더에서 자동 연결"))
                AutoLinkSfx();
            if (GUILayout.Button("빠진 칸 채우기"))
                m_Report.Insert(0, $"효과음 칸 {FillMissingEntries()}개를 채웠다");
            m_OnlyMissing = GUILayout.Toggle(m_OnlyMissing, "빠진 것만", GUILayout.Width(80));
        }

        var linked = m_SoundBank.Entries.Count(entry => entry.HasClip);
        EditorGUILayout.LabelField($"연결됨 {linked} / {m_SoundBank.Entries.Count}", EditorStyles.miniBoldLabel);

        var serialized = new SerializedObject(m_SoundBank);
        var entries = serialized.FindProperty("m_Entries");
        for (var i = 0; i < m_SoundBank.Entries.Count; i++)
        {
            var entry = m_SoundBank.Entries[i];
            if (m_OnlyMissing && entry.HasClip)
                continue;

            var info = GameEventCatalog.Find(entry.EventName);
            using (new EditorGUILayout.HorizontalScope())
            {
                var expanded = m_Expanded.Contains(entry.EventName);
                var title = $"{Status(entry.HasClip)} {entry.EventName}";
                var nowExpanded = EditorGUILayout.Foldout(expanded, title, true);
                if (nowExpanded != expanded)
                {
                    if (nowExpanded) m_Expanded.Add(entry.EventName);
                    else m_Expanded.Remove(entry.EventName);
                }

                GUILayout.Label(info != null ? info.When : "(07 목록에 없는 이벤트)", EditorStyles.miniLabel, GUILayout.MaxWidth(320));
                GUILayout.Label($"{entry.Clips.Count(clip => clip != null)}개 · {entry.Location}", EditorStyles.miniLabel, GUILayout.Width(110));
                DrawPlayButton(entry.EventName);
            }

            if (m_Expanded.Contains(entry.EventName))
            {
                EditorGUI.indentLevel++;
                if (info != null)
                    EditorGUILayout.LabelField("함께 오는 정보", info.Fields, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.PropertyField(entries.GetArrayElementAtIndex(i), GUIContent.none, true);
                EditorGUI.indentLevel--;
            }
        }
        serialized.ApplyModifiedProperties();
    }

    void AutoLinkSfx()
    {
        m_Report.Clear();
        FillMissingEntries();
        Undo.RecordObject(m_SoundBank, "Auto link SFX");

        var found = new Dictionary<string, List<AudioClip>>();
        var unknown = new List<string>();
        var gunFiles = k_GunSounds.Select(gun => gun.file).ToHashSet();
        foreach (var (path, clip) in FindClips(k_SfxFolder))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (gunFiles.Contains(name))
                continue;   // 총 소리 탭에서 연결한다

            var eventName = m_SoundBank.Find(name) != null ? name : StripNumber(name);
            if (eventName == null || m_SoundBank.Find(eventName) == null)
            {
                unknown.Add(path);
                continue;
            }
            if (!found.TryGetValue(eventName, out var list))
                found[eventName] = list = new List<AudioClip>();
            list.Add(clip);
        }

        // 폴더 밖에서 직접 넣은 클립은 남기고, 폴더 안 클립은 찾은 것으로 바꾼다
        foreach (var entry in m_SoundBank.Entries)
        {
            var kept = entry.Clips.Where(clip => clip != null && !IsIn(clip, k_SfxFolder)).ToList();
            if (found.TryGetValue(entry.EventName, out var clips))
                kept.AddRange(clips.OrderBy(clip => clip.name));
            if (!kept.SequenceEqual(entry.Clips))
                entry.Clips = kept;
        }
        EditorUtility.SetDirty(m_SoundBank);
        AssetDatabase.SaveAssets();

        m_Report.Add($"효과음 {found.Sum(pair => pair.Value.Count)}개를 이벤트 {found.Count}개에 연결했다");
        foreach (var path in unknown)
            m_Report.Add($"이름이 이벤트와 맞지 않음: {path}");
        var missing = m_SoundBank.Entries.Where(entry => !entry.HasClip).Select(entry => entry.EventName).ToList();
        if (missing.Count > 0)
            m_Report.Add($"아직 소리가 없는 이벤트 {missing.Count}개: {string.Join(", ", missing)}");
    }

    void DrawPlayButton(string eventName)
    {
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            var tooltip = Application.isPlaying ? "이 기기에서만 이벤트를 낸다 (좌석 1 기준)" : "Play 중에만 들어볼 수 있다";
            if (GUILayout.Button(new GUIContent("▶", tooltip), GUILayout.Width(26)))
                GameEvents.Raise(new GameEvent(eventName, 1, 2, 3) { LocalOnly = true });
        }
    }

    // ── 진행자 목소리 ──

    void DrawVoice()
    {
        if (m_VoiceBank == null)
        {
            EditorGUILayout.HelpBox("목소리 표가 없다. 위의 \"에셋·폴더 만들기\"를 누른다.", MessageType.Info);
            return;
        }

        m_Lines ??= LoadLines();
        EditorGUILayout.HelpBox(
            $"녹음을 {k_VoiceFolder}에 대사 id 이름으로 넣는다 (예: gh01.wav). {{bullets}} 대사는 숫자마다 gh01_1.wav ~ gh01_6.wav로 넣을 수 있다.\n" +
            "\"녹음 목록 내보내기\"는 녹음할 대사와 파일 이름을 CSV로 만든다 (사운드 담당에게 전달).", MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("폴더에서 자동 연결"))
                AutoLinkVoice();
            if (GUILayout.Button("녹음 목록 내보내기"))
                ExportRecordingList();
            if (GUILayout.Button("대사 CSV 다시 읽기", GUILayout.Width(130)))
                m_Lines = LoadLines();
            m_OnlyMissing = GUILayout.Toggle(m_OnlyMissing, "빠진 것만", GUILayout.Width(80));
        }

        var recorded = m_Lines.Count(line => m_VoiceBank.Find(line.Id) != null || HasNumbered(line.Id));
        EditorGUILayout.LabelField($"녹음됨 {recorded} / {m_Lines.Count}줄", EditorStyles.miniBoldLabel);

        string currentEvent = null;
        foreach (var line in m_Lines)
        {
            var hasVoice = m_VoiceBank.Find(line.Id) != null || HasNumbered(line.Id);
            if (m_OnlyMissing && hasVoice)
                continue;

            if (line.Event != currentEvent)
            {
                currentEvent = line.Event;
                EditorGUILayout.LabelField(currentEvent, EditorStyles.boldLabel);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{Status(hasVoice)} {line.Id}", GUILayout.Width(90));
                GUILayout.Label(line.Text, EditorStyles.wordWrappedLabel);
                if (line.Text.Contains("{bullets}"))
                    GUILayout.Label($"숫자별 {NumberedCount(line.Id)}/6", EditorStyles.miniLabel, GUILayout.Width(70));
                var clip = m_VoiceBank.Find(line.Id) ?? m_VoiceBank.Find(line.Id, 1);
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(clip, typeof(AudioClip), false, GUILayout.Width(150));
            }
        }
    }

    List<HostLine> LoadLines()
    {
        var csv = AssetDatabase.LoadAssetAtPath<TextAsset>(k_LinesCsvPath);
        return csv != null ? HostLineCsvParser.Parse(csv.text) : new List<HostLine>();
    }

    bool HasNumbered(string lineId) => NumberedCount(lineId) > 0;

    int NumberedCount(string lineId)
    {
        var prefix = lineId + "_";
        return m_VoiceBank.Entries.Count(entry => entry.Clip != null && entry.Key != null && entry.Key.StartsWith(prefix));
    }

    void AutoLinkVoice()
    {
        m_Report.Clear();
        m_Lines = LoadLines();
        var ids = m_Lines.Select(line => line.Id).ToHashSet();
        Undo.RecordObject(m_VoiceBank, "Auto link voice");

        // 폴더 밖에서 직접 넣은 것은 남긴다
        var entries = m_VoiceBank.Entries.Where(entry => entry.Clip != null && !IsIn(entry.Clip, k_VoiceFolder)).ToList();
        var linked = 0;
        foreach (var (path, clip) in FindClips(k_VoiceFolder))
        {
            var key = Path.GetFileNameWithoutExtension(path);
            var lineId = ids.Contains(key) ? key : StripNumber(key, onlyUnderscore: true);
            if (lineId == null || !ids.Contains(lineId))
            {
                m_Report.Add($"이름이 대사 id와 맞지 않음: {path}");
                continue;
            }
            entries.RemoveAll(entry => entry.Key == key);
            entries.Add(new VoiceBank.Entry { Key = key, Clip = clip });
            linked++;
        }

        m_VoiceBank.Entries.Clear();
        m_VoiceBank.Entries.AddRange(entries.OrderBy(entry => entry.Key));
        EditorUtility.SetDirty(m_VoiceBank);
        AssetDatabase.SaveAssets();

        m_Report.Insert(0, $"녹음 {linked}개를 연결했다");
        var missing = m_Lines.Where(line => m_VoiceBank.Find(line.Id) == null && !HasNumbered(line.Id)).Select(line => line.Id).ToList();
        if (missing.Count > 0)
            m_Report.Add($"아직 녹음이 없는 대사 {missing.Count}줄: {string.Join(", ", missing)}");
    }

    void ExportRecordingList()
    {
        m_Lines = LoadLines();
        EnsureFolder(k_VoiceFolder);
        var csv = new StringBuilder();
        csv.AppendLine("file,event,id,text,emotion,recorded");
        foreach (var line in m_Lines)
        {
            if (line.Text.Contains("{bullets}"))
            {
                for (var bullets = 1; bullets <= 6; bullets++)
                    AppendRow(csv, $"{line.Id}_{bullets}", line, line.Text.Replace("{bullets}", bullets.ToString()),
                        m_VoiceBank.Find($"{line.Id}_{bullets}") != null);
            }
            else
            {
                AppendRow(csv, line.Id, line, line.Text, m_VoiceBank.Find(line.Id) != null);
            }
        }

        File.WriteAllText(k_RecordingListPath, csv.ToString(), new UTF8Encoding(true));
        AssetDatabase.ImportAsset(k_RecordingListPath);
        m_Report.Clear();
        m_Report.Add($"녹음 목록을 만들었다: {k_RecordingListPath} (엑셀에서 열 수 있다)");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<TextAsset>(k_RecordingListPath));
    }

    static void AppendRow(StringBuilder csv, string file, HostLine line, string text, bool recorded) =>
        csv.AppendLine(string.Join(",", Quote(file + ".wav"), Quote(line.Event), Quote(line.Id), Quote(text), Quote(line.Emotion), recorded ? "O" : ""));

    static string Quote(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    // ── 총 소리 ──

    void DrawGun()
    {
        var effects = FindGunEffects(out var source);
        if (effects == null)
        {
            EditorGUILayout.HelpBox("열린 씬에 GunEffects(총)가 없다.", MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox(
            $"공이치기·딸깍·발사는 총(GunEffects)이 직접 낸다. 비워 두면 코드로 만든 임시 소리를 쓴다.\n" +
            $"{k_SfxFolder}에 gun_hammer, gun_blank, gun_fire 이름으로 넣고 자동 연결하거나 아래 칸에 직접 넣는다.\n고치는 곳: {source}",
            MessageType.None);

        if (GUILayout.Button("폴더에서 자동 연결"))
            AutoLinkGun(effects);

        var serialized = new SerializedObject(effects);
        foreach (var (_, field, label) in k_GunSounds)
        {
            var property = serialized.FindProperty(field);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(Status(property.objectReferenceValue != null), GUILayout.Width(20));
                EditorGUILayout.PropertyField(property, new GUIContent(label));
            }
        }
        EditorGUILayout.PropertyField(serialized.FindProperty("m_Volume"));
        if (serialized.ApplyModifiedProperties())
            SaveGunEffects(effects);
    }

    // 총이 프리팹이면 프리팹 원본을 고친다 (씬마다 따로 넣지 않게)
    static GunEffects FindGunEffects(out string source)
    {
        source = null;
        var inScene = FindAnyObjectByType<GunEffects>(FindObjectsInactive.Include);
        if (inScene == null)
            return null;

        var prefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(inScene);
        if (prefab != null)
        {
            source = AssetDatabase.GetAssetPath(prefab);
            return prefab;
        }

        source = $"씬의 {inScene.name}";
        return inScene;
    }

    static void SaveGunEffects(GunEffects effects)
    {
        EditorUtility.SetDirty(effects);
        if (EditorUtility.IsPersistent(effects))
            AssetDatabase.SaveAssets();
        else
            EditorSceneManager.MarkSceneDirty(effects.gameObject.scene);
    }

    void AutoLinkGun(GunEffects effects)
    {
        m_Report.Clear();
        var clips = FindClips(k_SfxFolder).ToDictionary(pair => Path.GetFileNameWithoutExtension(pair.path), pair => pair.clip);
        var serialized = new SerializedObject(effects);
        foreach (var (file, field, label) in k_GunSounds)
        {
            if (!clips.TryGetValue(file, out var clip))
            {
                m_Report.Add($"{label}: {file} 파일이 없다");
                continue;
            }
            serialized.FindProperty(field).objectReferenceValue = clip;
            m_Report.Add($"{label} ← {clip.name}");
        }
        serialized.ApplyModifiedProperties();
        SaveGunEffects(effects);
    }

    // ── 공통 ──

    void DrawReport()
    {
        if (m_Report.Count == 0)
            return;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(string.Join("\n", m_Report), MessageType.Info);
        if (GUILayout.Button("결과 지우기", GUILayout.Width(90)))
            m_Report.Clear();
    }

    static string Status(bool ok) => ok ? "✔" : "✘";

    static IEnumerable<(string path, AudioClip clip)> FindClips(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder))
            yield break;
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null)
                yield return (path, clip);
        }
    }

    // 끝의 번호(_1, " (1)")를 뗀 이름. 번호가 없으면 null
    static string StripNumber(string name, bool onlyUnderscore = false)
    {
        var match = k_NumberSuffix.Match(name);
        if (!match.Success)
            return null;
        if (onlyUnderscore && !Regex.IsMatch(name, @"_\d+$"))
            return null;
        return match.Groups[1].Value;
    }

    static bool IsIn(Object asset, string folder) => AssetDatabase.GetAssetPath(asset).StartsWith(folder + "/");

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
