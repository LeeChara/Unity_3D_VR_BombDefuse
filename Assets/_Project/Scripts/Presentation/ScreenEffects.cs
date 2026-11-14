using System.Collections;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시야에 붙는 화면 효과. 이 기기 사람의 눈 앞에만 그린다.
/// - 중상: 시야 가장자리가 붉게 어두워지고 심장박동처럼 뛴다 (자기 좌석의 <c>player_wounded</c>부터 새 게임까지).
/// - 사망: 자기 좌석이 죽으면(<c>player_died</c>) 화면이 거의 검게 덮인다.
/// - 엔딩: 승자가 정해지면(<c>player_won</c>) 가운데에 결과 글자를 띄운다. 상대가 나가면(<c>opponent_disconnected</c>) 그 사실도 적는다.
/// - 인트로: 새 게임(<c>Seating</c>)은 검은 화면에서 서서히 밝아지며 시작한다. 로비로 돌아가면 화면을 걷는다.
/// 진행자 자막보다 뒤에 그려서 자막은 항상 읽을 수 있다. 결과 글자는 자막보다 앞에 그린다.
/// </summary>
public class ScreenEffects : MonoBehaviour
{
    [SerializeField] GameFlowController m_Flow;
    [SerializeField, Tooltip("깊이 검사를 끈 UI 재질 (Art/Material/UIOverlay)")] Material m_OverlayMaterial;
    [SerializeField, Tooltip("결과 글자의 글꼴을 자막에서 가져온다")] HostSubtitle m_Subtitle;

    [Header("중상")]
    [SerializeField] Color m_WoundColor = new Color(0.35f, 0f, 0f, 1f);
    [SerializeField, Range(0f, 1f), Tooltip("가장자리가 가장 진할 때 불투명도")] float m_WoundStrength = 0.85f;
    [SerializeField, Range(0f, 1f), Tooltip("박동 사이에 약해지는 정도")] float m_WoundPulse = 0.25f;
    [SerializeField, Tooltip("분당 심장박동 수")] float m_HeartRate = 70f;
    [SerializeField, Range(0f, 90f), Tooltip("시선에서 이 각도(도)까지는 맑다")] float m_ClearAngle = 15f;
    [SerializeField, Range(0f, 90f), Tooltip("시선에서 이 각도(도)부터 가장 진하다")] float m_FullAngle = 48f;

    [Header("사망·인트로")]
    [SerializeField] Color m_DeathColor = new Color(0.08f, 0f, 0f, 1f);
    [SerializeField, Range(0f, 1f)] float m_DeathStrength = 0.9f;
    [SerializeField, Min(0f)] float m_DeathFadeDuration = 1.2f;
    [SerializeField, Min(0f), Tooltip("새 게임이 검은 화면에서 밝아지는 시간(초)")] float m_IntroFadeDuration = 2.5f;

    [Header("엔딩")]
    [SerializeField] string m_WinText = "생존";
    [SerializeField] string m_LoseText = "사망";
    [SerializeField] string m_OpponentLeftText = "상대가 떠났다";
    [SerializeField] Color m_WinColor = new Color(0.9f, 0.85f, 0.75f);
    [SerializeField] Color m_LoseColor = new Color(0.75f, 0.08f, 0.05f);

    const float k_CanvasDistance = 0.3f;
    const float k_CanvasSize = 1.6f;
    const float k_TextDistance = 1.2f;

    Image m_Vignette;
    Image m_Fade;
    TMP_Text m_ResultText;
    GameObject m_ResultPanel;
    bool m_Wounded;
    float m_WoundWeight;
    Coroutine m_FadeRoutine;
    bool m_OpponentLeft;

    void Start()
    {
        var origin = FindAnyObjectByType<XROrigin>();
        var eye = origin != null && origin.Camera != null ? origin.Camera.transform : Camera.main != null ? Camera.main.transform : null;
        if (eye == null)
        {
            Debug.LogWarning("[ScreenEffects] 카메라를 찾지 못해 화면 효과를 끈다", this);
            enabled = false;
            return;
        }

        var overlay = CreateCanvas("ScreenEffects", eye, k_CanvasDistance, -1);
        m_Vignette = CreateImage(overlay, "Vignette", CreateVignetteSprite());
        m_Fade = CreateImage(overlay, "Fade", null);
        SetAlpha(m_Vignette, 0f);
        SetAlpha(m_Fade, 0f);

        var result = CreateCanvas("EndingText", eye, k_TextDistance, 1);
        m_ResultPanel = result.gameObject;
        m_ResultText = CreateText(result);
        m_ResultPanel.SetActive(false);

        if (m_Flow != null)
            m_Flow.StateMachine.StateChanged += OnStateChanged;
        GameEvents.Raised += OnEvent;
    }

    void OnDestroy()
    {
        if (m_Flow != null)
            m_Flow.StateMachine.StateChanged -= OnStateChanged;
        GameEvents.Raised -= OnEvent;
    }

    int LocalSeat => m_Flow != null ? m_Flow.LocalSeat : 1;

    void OnEvent(GameEvent gameEvent)
    {
        switch (gameEvent.Name)
        {
            case "player_wounded" when gameEvent.Player == LocalSeat:
                m_Wounded = true;
                break;
            case "player_died" when gameEvent.Player == LocalSeat:
                m_Wounded = false;
                FadeTo(m_DeathColor, m_DeathStrength, m_DeathFadeDuration);
                break;
            case "opponent_disconnected":
                m_OpponentLeft = true;
                break;
            case "player_won":
                ShowResult(gameEvent.Player == LocalSeat);
                break;
        }
    }

    void OnStateChanged(GameState previous, GameState next)
    {
        switch (next)
        {
            case GameState.Seating:
                // 새 게임: 검은 화면에서 밝아진다
                ResetForNewGame();
                SetFade(Color.black, 1f);
                FadeTo(Color.black, 0f, m_IntroFadeDuration);
                break;
            case GameState.Lobby:
                ResetForNewGame();
                FadeTo(m_Fade.color, 0f, 1f);
                break;
        }
    }

    void ResetForNewGame()
    {
        m_Wounded = false;
        m_OpponentLeft = false;
        m_ResultPanel.SetActive(false);
    }

    void ShowResult(bool won)
    {
        var text = won ? m_WinText : m_LoseText;
        if (m_OpponentLeft)
            text = $"{text}\n<size=45%>{m_OpponentLeftText}</size>";
        m_ResultText.text = text;
        m_ResultText.color = won ? m_WinColor : m_LoseColor;
        m_ResultPanel.SetActive(true);
    }

    void Update()
    {
        if (m_Vignette == null)
            return;

        // 중상이 되면 서서히 들어오고, 박동마다 진해졌다 옅어진다
        m_WoundWeight = Mathf.MoveTowards(m_WoundWeight, m_Wounded ? 1f : 0f, Time.deltaTime / 0.8f);
        var beat = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(Time.time * m_HeartRate / 60f * Mathf.PI * 2f), 4f);
        var alpha = m_WoundWeight * m_WoundStrength * (1f - m_WoundPulse + m_WoundPulse * beat);
        m_Vignette.color = new Color(m_WoundColor.r, m_WoundColor.g, m_WoundColor.b, alpha);
    }

    // ── 덮개 ──

    void FadeTo(Color color, float alpha, float duration)
    {
        if (m_FadeRoutine != null)
            StopCoroutine(m_FadeRoutine);
        m_FadeRoutine = StartCoroutine(FadeRoutine(color, alpha, duration));
    }

    IEnumerator FadeRoutine(Color color, float targetAlpha, float duration)
    {
        var startColor = m_Fade.color;
        var startAlpha = startColor.a;
        var time = 0f;
        while (time < duration)
        {
            time += Time.deltaTime;
            var t = Mathf.SmoothStep(0f, 1f, time / duration);
            var rgb = Color.Lerp(startColor, color, t);
            SetFade(rgb, Mathf.Lerp(startAlpha, targetAlpha, t));
            yield return null;
        }

        SetFade(color, targetAlpha);
        m_FadeRoutine = null;
    }

    void SetFade(Color color, float alpha)
    {
        color.a = alpha;
        m_Fade.color = color;
        m_Fade.enabled = alpha > 0.001f;
    }

    static void SetAlpha(Image image, float alpha)
    {
        var color = image.color;
        color.a = alpha;
        image.color = color;
    }

    // ── 만들기 ──

    Canvas CreateCanvas(string name, Transform eye, float distance, int sortingOrder)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(eye, false);
        go.transform.localPosition = new Vector3(0f, 0f, distance);
        go.transform.localRotation = Quaternion.identity;
        // 1단위 = 1mm로 그려서 글자가 깔끔하게 나오게 한다
        go.transform.localScale = Vector3.one * 0.001f;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = sortingOrder;
        var rect = (RectTransform)go.transform;
        var size = distance / k_CanvasDistance * k_CanvasSize * 1000f;
        rect.sizeDelta = new Vector2(size, size);
        return canvas;
    }

    Image CreateImage(Canvas canvas, string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.material = m_OverlayMaterial;
        image.raycastTarget = false;
        return image;
    }

    TMP_Text CreateText(Canvas canvas)
    {
        var go = new GameObject("Result", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var text = go.AddComponent<TextMeshProUGUI>();
        var subtitleText = m_Subtitle != null ? m_Subtitle.GetComponentInChildren<TMP_Text>(true) : null;
        if (subtitleText != null)
        {
            text.font = subtitleText.font;
            // 자막 글자 재질은 깊이 검사를 끈 것이라 벽에 가려지지 않는다
            text.fontSharedMaterial = subtitleText.fontSharedMaterial;
        }
        text.fontSize = 220f;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    // 가운데는 투명하고 가장자리로 갈수록 진해지는 원형 그림
    Sprite CreateVignetteSprite()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Vignette" };
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5f) / size * 2f - 1f;
                var dy = (y + 0.5f) / size * 2f - 1f;
                // 그림의 한 점이 시선에서 몇 도 떨어져 있는지로 진하기를 정한다
                var distance = Mathf.Sqrt(dx * dx + dy * dy) * k_CanvasSize * 0.5f;
                var angle = Mathf.Atan2(distance, k_CanvasDistance) * Mathf.Rad2Deg;
                var a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(m_ClearAngle, m_FullAngle, angle));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
