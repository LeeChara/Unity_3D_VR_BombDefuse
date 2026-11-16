using UnityEngine;

/// <summary>
/// "말하는 중" 표시. 스피커 아이콘(🔊)을 선으로 그린다: 사각형 몸통 + 사다리꼴 나팔 + 소리 줄(호) 2개. 모델 에셋은 쓰지 않는다.
/// 말하는 동안만 보이고, 소리 줄은 목소리 크기(Vivox AudioEnergy)에 따라 0 → 1 → 2개로 늘고 준다. 항상 보는 사람 쪽을 향한다.
/// 콜라이더는 없어서 레이와 총알을 막지 않는다. 어디에 붙일지는 <see cref="VoiceIndicatorController"/>가 정한다.
/// </summary>
public class VoiceIndicator : MonoBehaviour
{
    [SerializeField, Tooltip("이 크기(0~1)부터 소리 줄 1개")] float m_Level1 = 0.08f;
    [SerializeField, Tooltip("이 크기(0~1)부터 소리 줄 2개")] float m_Level2 = 0.3f;
    [SerializeField, Tooltip("커질 때 따라가는 속도 (클수록 바로 반응)")] float m_AttackSpeed = 25f;
    [SerializeField, Tooltip("작아질 때 따라가는 속도 (느리면 줄이 천천히 줄어든다)")] float m_ReleaseSpeed = 6f;
    [SerializeField, Tooltip("말이 잠깐 끊겨도 이 시간(초) 동안은 계속 보여 준다 (단어 사이에 깜빡이지 않게)")] float m_HoldTime = 0.35f;

    GameObject[] m_Waves;
    float m_Level;
    float m_LastSpeakingTime = float.NegativeInfinity;

    /// <summary>스피커 아이콘을 만들어 돌려준다. size는 전체 높이(m). 처음에는 숨겨져 있다.</summary>
    public static VoiceIndicator Create(string name, Transform parent, Vector3 localPosition, float size, Color color)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        material.SetColor("_BaseColor", color);
        material.color = color;
        const float width = 0.055f;

        // 좌표는 높이 1 기준. 왼쪽이 몸통, 오른쪽(+X)으로 소리가 나간다
        Line(root.transform, "Body", material, width, true,
            new Vector3(-0.55f, 0.17f, 0f), new Vector3(-0.3f, 0.17f, 0f), new Vector3(-0.3f, -0.17f, 0f), new Vector3(-0.55f, -0.17f, 0f));
        Line(root.transform, "Horn", material, width, true,
            new Vector3(-0.3f, 0.17f, 0f), new Vector3(0.02f, 0.5f, 0f), new Vector3(0.02f, -0.5f, 0f), new Vector3(-0.3f, -0.17f, 0f));

        var waves = new GameObject[2];
        waves[0] = Arc(root.transform, "Wave1", material, width, 0.3f, 0.12f);
        waves[1] = Arc(root.transform, "Wave2", material, width, 0.52f, 0.12f);
        root.transform.localScale = Vector3.one * size;

        var indicator = root.AddComponent<VoiceIndicator>();
        indicator.m_Waves = waves;
        indicator.ApplyLevel();
        root.SetActive(false);
        return indicator;
    }

    static LineRenderer Line(Transform parent, string name, Material material, float width, bool loop, params Vector3[] points)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.alignment = LineAlignment.TransformZ;   // 아이콘 평면에 납작하게 그린다
        line.sharedMaterial = material;
        line.widthMultiplier = width;
        line.loop = loop;
        line.numCornerVertices = 3;
        line.numCapVertices = 3;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.positionCount = points.Length;
        line.SetPositions(points);
        return line;
    }

    // 나팔 오른쪽에 반지름 radius, 위아래 ±50도의 호
    static GameObject Arc(Transform parent, string name, Material material, float width, float radius, float centerX)
    {
        const int segments = 14;
        var points = new Vector3[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var angle = Mathf.Lerp(-50f, 50f, (float)i / segments) * Mathf.Deg2Rad;
            points[i] = new Vector3(centerX + Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        }

        return Line(parent, name, material, width, false, points).gameObject;
    }

    /// <summary>매 프레임 부른다. speaking은 Vivox의 말하는 중 판정, energy는 목소리 크기(0~1).</summary>
    public void SetVoice(bool speaking, float energy)
    {
        if (speaking)
            m_LastSpeakingTime = Time.time;

        var show = Time.time - m_LastSpeakingTime <= m_HoldTime;
        if (gameObject.activeSelf != show)
        {
            gameObject.SetActive(show);
            if (!show)
                m_Level = 0f;
        }

        if (!show)
            return;

        // 커질 때는 바로, 작아질 때는 천천히 따라간다
        var target = Mathf.Clamp01(energy);
        var speed = target > m_Level ? m_AttackSpeed : m_ReleaseSpeed;
        m_Level = Mathf.MoveTowards(m_Level, target, speed * Time.deltaTime);
        ApplyLevel();

        // 보는 사람 쪽을 향한다 (머리가 돌아가도 아이콘이 정면으로 보이게)
        var camera = Camera.main;
        if (camera != null)
        {
            var toViewer = transform.position - camera.transform.position;
            if (toViewer.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(toViewer, Vector3.up);
        }
    }

    void ApplyLevel()
    {
        m_Waves[0].SetActive(m_Level >= m_Level1);
        m_Waves[1].SetActive(m_Level >= m_Level2);
    }
}
