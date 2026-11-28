using UnityEngine;

/// <summary>
/// 진행자 목소리 효과. 녹음 파일은 그대로 두고 재생할 때 높이·필터를 입힌다.
/// 메뉴 BombDefuse > Host Voice 창에서 고치고 원래 목소리와 비교해 들어 본다. Play 중에 고치면 바로 바뀐다.
/// <see cref="HostDialogue"/>가 목소리 재생기에 건다.
/// </summary>
[CreateAssetMenu(fileName = "HostVoiceSettings", menuName = "BombDefuse/Host Voice Settings")]
public class HostVoiceSettings : ScriptableObject
{
    [Range(0.5f, 1.5f), Tooltip("목소리 높이. 1보다 낮으면 낮고 느려진다 (대사 대기 시간도 그만큼 늘어난다)")] public float Pitch = 1f;
    [Range(10f, 22000f), Tooltip("이보다 높은 소리를 줄인다 (22000 = 끔). 낮추면 먹먹하고 멀게")] public float LowPassHz = 22000f;
    [Range(10f, 3000f), Tooltip("이보다 낮은 소리를 줄인다 (10 = 끔). 올리면 얇고 무전기 같게")] public float HighPassHz = 10f;
    [Range(0f, 1f), Tooltip("찌그러짐 (0 = 끔)")] public float Distortion;
    [Range(0f, 1f), Tooltip("여러 목소리가 겹친 듯한 울렁임 (0 = 끔)")] public float Chorus;
    [Range(0.1f, 10f), Tooltip("울렁임 속도")] public float ChorusRate = 0.8f;
    [Range(0f, 1f), Tooltip("울렁임 깊이")] public float ChorusDepth = 0.3f;
    [Range(0f, 1f), Tooltip("메아리 크기 (0 = 끔)")] public float Echo;
    [Range(10f, 1000f), Tooltip("메아리 간격(ms)")] public float EchoDelayMs = 180f;
    [Range(0f, 0.95f), Tooltip("메아리가 줄어드는 정도 (클수록 오래 남음)")] public float EchoDecay = 0.4f;
    [Tooltip("공간 잔향 (Off = 끔)")] public AudioReverbPreset Reverb = AudioReverbPreset.Off;

    /// <summary>이 소리 재생기에 효과를 건다. 필요한 필터를 같은 오브젝트에 붙이고, 쓰지 않는 필터는 끈다.</summary>
    public void Apply(AudioSource source)
    {
        if (source == null)
            return;
        var go = source.gameObject;
        source.pitch = Pitch;

        var lowPass = Get<AudioLowPassFilter>(go, LowPassHz < 21999f);
        if (lowPass != null)
        {
            lowPass.cutoffFrequency = LowPassHz;
            lowPass.lowpassResonanceQ = 1f;
        }

        var highPass = Get<AudioHighPassFilter>(go, HighPassHz > 10.5f);
        if (highPass != null)
        {
            highPass.cutoffFrequency = HighPassHz;
            highPass.highpassResonanceQ = 1f;
        }

        var distortion = Get<AudioDistortionFilter>(go, Distortion > 0.001f);
        if (distortion != null)
            distortion.distortionLevel = Distortion;

        var chorus = Get<AudioChorusFilter>(go, Chorus > 0.001f);
        if (chorus != null)
        {
            chorus.dryMix = 1f - Chorus * 0.5f;
            chorus.wetMix1 = Chorus;
            chorus.wetMix2 = Chorus;
            chorus.wetMix3 = Chorus * 0.5f;
            chorus.delay = 40f;
            chorus.rate = ChorusRate;
            chorus.depth = ChorusDepth;
        }

        var echo = Get<AudioEchoFilter>(go, Echo > 0.001f);
        if (echo != null)
        {
            echo.delay = EchoDelayMs;
            echo.decayRatio = EchoDecay;
            echo.dryMix = 1f;
            echo.wetMix = Echo;
        }

        var reverb = Get<AudioReverbFilter>(go, Reverb != AudioReverbPreset.Off);
        if (reverb != null)
            reverb.reverbPreset = Reverb;
    }

    /// <summary>효과를 모두 끈다 (원래 목소리).</summary>
    public static void Clear(AudioSource source)
    {
        if (source == null)
            return;
        source.pitch = 1f;
        foreach (var filter in source.GetComponents<Behaviour>())
            if (filter is AudioLowPassFilter || filter is AudioHighPassFilter || filter is AudioDistortionFilter
                || filter is AudioChorusFilter || filter is AudioEchoFilter || filter is AudioReverbFilter)
                filter.enabled = false;
    }

    // 쓰면 붙여서 켜고, 안 쓰면 (있을 때만) 끈다. 끈 필터는 돌아가지 않는다
    static T Get<T>(GameObject go, bool used) where T : Behaviour
    {
        var filter = go.GetComponent<T>();
        if (!used)
        {
            if (filter != null)
                filter.enabled = false;
            return null;
        }

        if (filter == null)
            filter = go.AddComponent<T>();
        filter.enabled = true;
        return filter;
    }
}
