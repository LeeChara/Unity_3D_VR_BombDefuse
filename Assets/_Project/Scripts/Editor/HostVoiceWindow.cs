using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 진행자 목소리 효과 창 (메뉴 BombDefuse > Host Voice). <see cref="HostVoiceSettings"/> 값을 고치고 대사별로 원래 목소리와 비교해 듣는다.
/// 미리 듣기는 Play 없이도 되고, 들으면서 값을 바꾸면 바로 바뀐다. Play 중에 고친 값은 진행자에게도 바로 들어간다.
/// </summary>
public class HostVoiceWindow : EditorWindow
{
    const string k_SettingsPath = "Assets/_Project/Data/HostVoiceSettings.asset";
    const string k_VoiceBankPath = "Assets/_Project/Audio/VoiceBank.asset";

    // 칸 이름의 한글 (창에는 "한글 (영어)"로 보인다)
    static readonly System.Collections.Generic.Dictionary<string, string> k_Labels = new System.Collections.Generic.Dictionary<string, string>
    {
        { "Pitch", "목소리 높이" },
        { "LowPassHz", "고음 줄이기" },
        { "HighPassHz", "저음 줄이기" },
        { "Distortion", "찌그러짐" },
        { "Chorus", "울렁임" },
        { "ChorusRate", "울렁임 속도" },
        { "ChorusDepth", "울렁임 깊이" },
        { "Echo", "메아리" },
        { "EchoDelayMs", "메아리 간격" },
        { "EchoDecay", "메아리 잔류" },
        { "Reverb", "공간 잔향" },
    };

    HostVoiceSettings m_Settings;
    SerializedObject m_SerializedSettings;
    VoiceBank m_VoiceBank;
    Vector2 m_Scroll;

    string[] m_Keys = new string[0];
    int m_Index;
    AudioClip m_ClipOverride;
    bool m_WithEffects;

    GameObject m_PreviewObject;
    AudioSource m_Source;

    [MenuItem("BombDefuse/Host Voice")]
    static void Open() => GetWindow<HostVoiceWindow>("진행자 목소리");

    void OnEnable()
    {
        minSize = new Vector2(460f, 480f);
        m_Settings = AssetDatabase.LoadAssetAtPath<HostVoiceSettings>(k_SettingsPath);
        m_VoiceBank = AssetDatabase.LoadAssetAtPath<VoiceBank>(k_VoiceBankPath);
        m_Keys = m_VoiceBank != null
            ? m_VoiceBank.Entries.Where(e => e.Clip != null && !string.IsNullOrEmpty(e.Key)).Select(e => e.Key).OrderBy(k => k).ToArray()
            : new string[0];
    }

    void OnDisable()
    {
        if (m_PreviewObject != null)
            DestroyImmediate(m_PreviewObject);
    }

    void OnGUI()
    {
        m_Settings = (HostVoiceSettings)EditorGUILayout.ObjectField("목소리 설정", m_Settings, typeof(HostVoiceSettings), false);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button(new GUIContent("설정 만들기·씬에 연결", $"{k_SettingsPath}가 없으면 만들고, 열린 씬의 HostDialogue에 연결한 뒤 씬을 저장한다")))
                CreateAndConnect();
        }
        if (m_Settings == null)
        {
            EditorGUILayout.HelpBox("목소리 설정이 없다. 위 버튼을 누른다.", MessageType.Info);
            return;
        }

        if (m_SerializedSettings == null || m_SerializedSettings.targetObject != m_Settings)
            m_SerializedSettings = new SerializedObject(m_Settings);
        m_SerializedSettings.Update();

        m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("효과", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        // "한글 (영어)" 이름이 잘리지 않게 이름 칸을 넓힌다
        EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.5f, 120f, 260f);
        var property = m_SerializedSettings.GetIterator();
        property.NextVisible(true);
        while (property.NextVisible(false))
        {
            var label = k_Labels.TryGetValue(property.name, out var korean) ? $"{korean} ({property.displayName})" : property.displayName;
            EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip), true);
        }
        if (EditorGUI.EndChangeCheck())
        {
            m_SerializedSettings.ApplyModifiedProperties();
            ApplyPreview();
        }

        if (GUILayout.Button("처음 값으로 (원래 목소리)"))
        {
            Undo.RecordObject(m_Settings, "목소리 효과 초기화");
            var defaults = CreateInstance<HostVoiceSettings>();
            EditorUtility.CopySerialized(defaults, m_Settings);
            DestroyImmediate(defaults);
            EditorUtility.SetDirty(m_Settings);
            m_SerializedSettings.Update();
            ApplyPreview();
        }

        EditorGUILayout.Space();
        DrawPreview();
        EditorGUILayout.EndScrollView();
    }

    void DrawPreview()
    {
        EditorGUILayout.LabelField("미리 듣기", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            m_Index = EditorGUILayout.Popup("대사", m_Index, m_Keys);
            if (GUILayout.Button("◀", GUILayout.Width(24)) && m_Keys.Length > 0)
                m_Index = (m_Index - 1 + m_Keys.Length) % m_Keys.Length;
            if (GUILayout.Button("▶", GUILayout.Width(24)) && m_Keys.Length > 0)
                m_Index = (m_Index + 1) % m_Keys.Length;
        }
        m_ClipOverride = (AudioClip)EditorGUILayout.ObjectField(new GUIContent("다른 소리", "넣으면 위 대사 대신 이 소리로 듣는다"), m_ClipOverride, typeof(AudioClip), false);

        var clip = CurrentClip();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(clip == null))
            {
                if (GUILayout.Button("원래 목소리", GUILayout.Height(28)))
                    Play(clip, false);
                if (GUILayout.Button("효과 넣어 듣기", GUILayout.Height(28)))
                    Play(clip, true);
            }
            if (GUILayout.Button("정지", GUILayout.Width(50), GUILayout.Height(28)) && m_Source != null)
                m_Source.Stop();
        }

        if (clip != null)
            EditorGUILayout.LabelField($"길이: 원래 {clip.length:F1}초 → 효과 {clip.length / Mathf.Max(0.1f, m_Settings.Pitch):F1}초");
        if (Application.isPlaying)
            EditorGUILayout.HelpBox("Play 중에는 진행자에게도 바로 들어간다.", MessageType.None);
    }

    AudioClip CurrentClip()
    {
        if (m_ClipOverride != null)
            return m_ClipOverride;
        if (m_VoiceBank == null || m_Keys.Length == 0)
            return null;
        return m_VoiceBank.Find(m_Keys[Mathf.Clamp(m_Index, 0, m_Keys.Length - 1)]);
    }

    void Play(AudioClip clip, bool withEffects)
    {
        if (m_PreviewObject == null)
        {
            // 씬에 저장되지 않는 숨은 재생기
            m_PreviewObject = new GameObject("HostVoicePreview") { hideFlags = HideFlags.HideAndDontSave };
            m_Source = m_PreviewObject.AddComponent<AudioSource>();
            m_Source.playOnAwake = false;
            m_Source.spatialBlend = 0f;
            m_Source.ignoreListenerPause = true;
        }

        m_WithEffects = withEffects;
        ApplyPreview();
        m_Source.Stop();
        m_Source.clip = clip;
        m_Source.Play();
    }

    void ApplyPreview()
    {
        if (m_Source == null || m_Settings == null)
            return;
        if (m_WithEffects)
            m_Settings.Apply(m_Source);
        else
            HostVoiceSettings.Clear(m_Source);
    }

    void CreateAndConnect()
    {
        if (m_Settings == null)
        {
            m_Settings = CreateInstance<HostVoiceSettings>();
            AssetDatabase.CreateAsset(m_Settings, k_SettingsPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[HostVoice] 만듦: {k_SettingsPath}");
        }

        var connected = 0;
        foreach (var host in FindObjectsByType<HostDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(host);
            var field = so.FindProperty("m_VoiceEffects");
            if (field == null || field.objectReferenceValue == m_Settings)
                continue;
            field.objectReferenceValue = m_Settings;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
            connected++;
        }

        if (connected > 0)
            EditorSceneManager.SaveOpenScenes();
        Debug.Log(connected > 0 ? $"[HostVoice] HostDialogue {connected}개에 연결하고 씬 저장" : "[HostVoice] 이미 연결돼 있다");
    }
}
