#if UNITY_EDITOR
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Management;

/// <summary>
/// 에디터 테스트 전용. 빌드에는 들어가지 않는다.
/// - Multiplayer Play Mode의 가상 플레이어(Player 2) 창: VR을 켜지 않고 키보드·마우스 시뮬레이터로 조작한다.
///   같은 컴퓨터에서 두 창이 한 헤드셋을 동시에 잡으려다 머리 추적이 깨지는 것을 막는다.
/// - 메인 에디터: 헤드셋이 붙어 있으면(XR 로더가 살아 있으면) 시뮬레이터를 아예 만들지 않는다.
///   시뮬레이터는 켜지는 순간 진짜 헤드셋 입력 장치를 지우기 때문에(XRI의 Remove Other HMD Devices 기본값),
///   한 번 생기면 나중에 치워도 머리 추적이 돌아오지 않는다. 헤드셋이 없으면 프로젝트 설정대로 시뮬레이터를 쓴다.
/// </summary>
static class VirtualPlayerInput
{
    static bool s_DisabledXRInit;
    static bool s_DisabledAutoSimulator;

    // 가상 플레이어는 Library/VP/ 아래의 복제 프로젝트에서 돈다
    static bool IsVirtualPlayer => Application.dataPath.Replace('\\', '/').Contains("/Library/VP/");

    // 헤드셋이 있으면 XR 로더가 살아 있다. 없으면 XR_ERROR_FORM_FACTOR_UNAVAILABLE로 로더가 없다.
    // XR은 AfterAssembliesLoaded에 동기로 초기화되므로 BeforeSceneLoad부터는 믿을 수 있다
    static XRLoader ActiveLoader
    {
        get
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            return manager != null ? manager.activeLoader : null;
        }
    }

    // XR은 AfterAssembliesLoaded에 InitManagerOnStart를 보고 시작하므로, 그보다 먼저 끈다
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void SkipXRInit()
    {
        var settings = XRGeneralSettings.Instance;
        if (!IsVirtualPlayer || settings == null || !settings.InitManagerOnStart)
            return;

        settings.InitManagerOnStart = false;
        s_DisabledXRInit = true;
    }

    // 시뮬레이터는 AfterSceneLoad에 프로젝트 설정을 보고 생기므로, 그보다 먼저 설정을 끈다
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void GateSimulator()
    {
        if (IsVirtualPlayer || ActiveLoader == null)
            return;

        if (SetAutoSimulator(false))
        {
            s_DisabledAutoSimulator = true;
            Debug.Log($"[VirtualPlayerInput] Headset found ({ActiveLoader.name}): keyboard/mouse simulator will not be created");
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterSceneLoad()
    {
        if (IsVirtualPlayer)
            UseSimulator();
        else
            new GameObject(nameof(MainEditorWatcher)).AddComponent<MainEditorWatcher>();
    }

    static void UseSimulator()
    {
        // 설정 에셋을 메모리에서만 바꿨으므로 되돌린다 (저장되면 메인 에디터도 VR을 켜지 않게 된다)
        var settings = XRGeneralSettings.Instance;
        if (s_DisabledXRInit && settings != null)
            settings.InitManagerOnStart = true;
        s_DisabledXRInit = false;

        // 그래도 XR이 켜져 있으면 끈다
        if (settings != null && settings.Manager != null && settings.Manager.activeLoader != null)
        {
            settings.Manager.StopSubsystems();
            settings.Manager.DeinitializeLoader();
        }

        // 프로젝트 설정으로 이미 시뮬레이터가 생겼으면 그대로 쓴다
        if (XRInteractionSimulator.instance != null)
            return;

        var prefab = SimulatorPrefab;
        if (prefab == null)
        {
            Debug.LogWarning("[VirtualPlayerInput] XR Interaction Simulator prefab is missing. Reimport the XR Interaction Simulator sample.");
            return;
        }

        var simulator = Object.Instantiate(prefab);
        simulator.name = prefab.name;
        Object.DontDestroyOnLoad(simulator);
        Debug.Log("[VirtualPlayerInput] Virtual player: XR off, keyboard/mouse simulator on");
    }

    // ── 시뮬레이터 설정 에셋 (클래스가 internal이라 리플렉션으로 다룬다) ──

    static ScriptableObject SimulatorSettings => Resources.Load<ScriptableObject>("XRDeviceSimulatorSettings");

    static GameObject SimulatorPrefab
    {
        get
        {
            var settings = SimulatorSettings;
            return settings != null
                ? new UnityEditor.SerializedObject(settings).FindProperty("m_SimulatorPrefab")?.objectReferenceValue as GameObject
                : null;
        }
    }

    // 메모리에서만 바꾼다. 바꿨으면 true
    static bool SetAutoSimulator(bool enabled)
    {
        var settings = SimulatorSettings;
        var property = settings != null
            ? settings.GetType().GetProperty("automaticallyInstantiateSimulatorPrefab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            : null;
        if (property == null || (bool)property.GetValue(settings) == enabled)
            return false;

        property.SetValue(settings, enabled);
        return true;
    }

    // 메인 에디터: 바꿔 둔 설정을 되돌리고, 혹시 생긴 시뮬레이터를 치우고, 문제를 찾을 때 보려고 XR 상태를 남긴다
    sealed class MainEditorWatcher : MonoBehaviour
    {
        static readonly float[] k_ReportTimes = { 3f, 20f, 60f };

        float m_StartTime;
        int m_NextReport;

        void Start()
        {
            DontDestroyOnLoad(gameObject);
            m_StartTime = Time.unscaledTime;
        }

        void Update()
        {
            // 시뮬레이터 생성(AfterSceneLoad)은 끝났으니 설정을 원래대로
            if (s_DisabledAutoSimulator)
            {
                SetAutoSimulator(true);
                s_DisabledAutoSimulator = false;
            }

            var elapsed = Time.unscaledTime - m_StartTime;
            if (m_NextReport < k_ReportTimes.Length && elapsed >= k_ReportTimes[m_NextReport])
            {
                m_NextReport++;
                Debug.Log($"[VirtualPlayerInput] {elapsed:F0}s: {Describe()}");
            }

            // 헤드셋이 있는데 시뮬레이터가 생겼으면(설정을 못 껐거나 수동으로 넣었거나) 치운다. 머리 추적은 돌아오지 않을 수 있다
            var simulator = XRInteractionSimulator.instance;
            if (simulator != null && ActiveLoader != null)
            {
                Destroy(simulator.gameObject);
                Debug.LogWarning($"[VirtualPlayerInput] Headset found but a simulator was created; removed it. Head tracking may stay broken this session. {Describe()}");
            }
        }

        static string Describe()
        {
            var loader = ActiveLoader;
            var display = loader != null ? loader.GetLoadedSubsystem<UnityEngine.XR.XRDisplaySubsystem>() : null;
            var devices = new System.Text.StringBuilder();
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (device is UnityEngine.InputSystem.XR.XRHMD || device is UnityEngine.InputSystem.XR.XRController)
                    devices.Append(device.name).Append(", ");
            }

            var camera = Camera.main != null ? Camera.main.transform.position.ToString("F2") : "none";
            return $"loader={(loader != null ? loader.name : "none")} display={(display != null ? display.running.ToString() : "none")} " +
                $"simulator={(XRInteractionSimulator.instance != null)} xrDevices=[{devices}] camera={camera}";
        }
    }
}
#endif
