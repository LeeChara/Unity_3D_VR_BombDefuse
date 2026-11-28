using System.Collections;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시야에 붙는 화면 효과. 이 기기 사람의 눈 앞에만 그린다.
/// - 중상: 시야 가장자리가 붉게 어두워지고 심장박동처럼 뛴다 (자기 좌석의 <c>player_wounded</c>부터 새 게임까지).
/// - 표적: 사수 턴 동안 총을 맞을 사람(<c>gun_handover</c>의 Target)이면 시야 가장자리가 어둡게 빨리 뛴다.
/// - 사격 실눈·먹먹함: 총을 손에 쥔 동안(쥔 기기에서만) 위아래에서 검은 그라데이션이 서서히 내려와 실눈을 뜬 것처럼 가운데만 보이고,
///   같은 속도로 귀가 먹먹해진다 (듣는 쪽 저역 필터). 놓으면 다시 뜨고 들린다.
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

    [Header("표적")]
    [SerializeField, Tooltip("사수 턴 동안 총을 맞을 사람(대상) 시야 가장자리 색")] Color m_TargetColor = new Color(0f, 0f, 0f, 1f);
    [SerializeField, Range(0f, 1f)] float m_TargetStrength = 0.7f;
    [SerializeField, Tooltip("표적일 때 분당 박동 수")] float m_TargetHeartRate = 100f;

    [Header("사격 실눈·먹먹함")]
    [SerializeField, Tooltip("쏘는 총. 비우면 씬에서 찾는다")] Gun m_Gun;
    [SerializeField, Min(0.01f), Tooltip("총을 잡은 뒤 눈이 다 가늘어질 때까지 시간(초)")] float m_SquintCloseTime = 2f;
    [SerializeField, Min(0.01f), Tooltip("총을 놓은 뒤 다시 눈을 뜨는 시간(초)")] float m_SquintOpenTime = 0.8f;
    [SerializeField] Color m_SquintColor = Color.black;
    [SerializeField, Range(0f, 1f), Tooltip("가장 가늘게 떴을 때 위아래 덮개 불투명도")] float m_SquintStrength = 0.97f;
    [SerializeField, Range(1f, 45f), Tooltip("가늘게 떴을 때 시선 위아래로 맑게 남는 각도(도)")] float m_SquintClearAngle = 9f;
    [SerializeField, Range(1f, 45f), Tooltip("맑은 곳에서 완전히 검어질 때까지 그라데이션 각도(도)")] float m_SquintSoftAngle = 14f;
    [SerializeField, Tooltip("실눈과 함께 귀가 먹먹해지는지")] bool m_Muffle = true;
    [SerializeField, Range(100f, 5000f), Tooltip("가장 먹먹할 때 남는 소리 높이(Hz). 낮을수록 더 막힌 소리")] float m_MuffleCutoff = 700f;
    [SerializeField, Range(1f, 5f), Tooltip("막힌 소리 끝이 울리는 정도")] float m_MuffleResonance = 1.2f;

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
    Image m_SquintTop;
    Image m_SquintBottom;
    float m_SquintWeight;
    AudioLowPassFilter m_MuffleFilter;
    TMP_Text m_ResultText;
    GameObject m_ResultPanel;
    bool m_Wounded;
    float m_WoundWeight;
    bool m_Targeted;
    float m_TargetWeight;
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
        var lid = CreateSquintSprite();
        m_SquintTop = CreateSquintImage(overlay, "SquintTop", lid, false);
        m_SquintBottom = CreateSquintImage(overlay, "SquintBottom", lid, true);
        m_Fade = CreateImage(overlay, "Fade", null);
        SetAlpha(m_Vignette, 0f);
        SetAlpha(m_Fade, 0f);
        UpdateSquint();

        if (m_Gun == null)
            m_Gun = FindAnyObjectByType<Gun>(FindObjectsInactive.Include);   // 총은 지급 전까지 꺼져 있다

        // 먹먹함: 듣는 쪽(AudioListener)에 저역 필터를 달면 이 기기에서 들리는 소리가 모두 막힌다
        var listener = FindAnyObjectByType<AudioListener>();
        if (listener != null)
        {
            m_MuffleFilter = listener.GetComponent<AudioLowPassFilter>();
            if (m_MuffleFilter == null)
                m_MuffleFilter = listener.gameObject.AddComponent<AudioLowPassFilter>();
            m_MuffleFilter.enabled = false;
        }

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
            case "gun_handover":
                m_Targeted = gameEvent.Target == LocalSeat;
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
        // 표적 표시는 총이 나와 있는 동안만
        if (next != GameState.GunHandover && next != GameState.ShooterTurn)
            m_Targeted = false;

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

        // 중상이 되면 서서히 들어오고, 박동마다 진해졌다 옅어진다. 표적이 되면 가장자리가 어둡게 빨리 뛴다
        m_WoundWeight = Mathf.MoveTowards(m_WoundWeight, m_Wounded ? 1f : 0f, Time.deltaTime / 0.8f);
        m_TargetWeight = Mathf.MoveTowards(m_TargetWeight, m_Targeted ? 1f : 0f, Time.deltaTime / 0.6f);
        var rate = Mathf.Lerp(m_HeartRate, m_TargetHeartRate, m_TargetWeight);
        var beat = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(Time.time * rate / 60f * Mathf.PI * 2f), 4f);
        var pulse = 1f - m_WoundPulse + m_WoundPulse * beat;
        var woundAlpha = m_WoundWeight * m_WoundStrength;
        var targetAlpha = m_TargetWeight * m_TargetStrength;
        // 중상이면 붉게, 아니면 표적 색으로
        var color = m_Wounded ? m_WoundColor : Color.Lerp(m_WoundColor, m_TargetColor, m_TargetWeight);
        m_Vignette.color = new Color(color.r, color.g, color.b, Mathf.Max(woundAlpha, targetAlpha) * pulse);

        // 이 기기의 손이 총을 쥐고 있는 동안 (상대 기기의 총은 잡을 수 없으므로 사수에게만 보인다)
        var squinting = m_Gun != null && m_Gun.IsEquipped;
        m_SquintWeight = Mathf.MoveTowards(m_SquintWeight, squinting ? 1f : 0f, Time.deltaTime / (squinting ? m_SquintCloseTime : m_SquintOpenTime));
        UpdateSquint();
        UpdateMuffle(Mathf.SmoothStep(0f, 1f, m_SquintWeight));
    }

    // 귀도 눈과 같은 속도로 막힌다. 소리 높이는 귀에 고르게 들리도록 로그로 옮긴다
    void UpdateMuffle(float weight)
    {
        if (m_MuffleFilter == null)
            return;
        weight = m_Muffle ? weight : 0f;
        m_MuffleFilter.enabled = weight > 0.001f;
        m_MuffleFilter.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(m_MuffleCutoff), weight));
        m_MuffleFilter.lowpassResonanceQ = Mathf.Lerp(1f, m_MuffleResonance, weight);
    }

    // 위아래 덮개가 화면 밖에서 시선 쪽으로 내려온다 (눈꺼풀처럼). 0이면 화면 밖, 1이면 맑은 각도까지
    void UpdateSquint()
    {
        var weight = Mathf.SmoothStep(0f, 1f, m_SquintWeight);
        var visible = weight > 0.001f;
        m_SquintTop.enabled = visible;
        m_SquintBottom.enabled = visible;
        if (!visible)
            return;

        var open = k_CanvasSize * 0.5f * 1000f;
        var closed = Mathf.Tan(m_SquintClearAngle * Mathf.Deg2Rad) * k_CanvasDistance * 1000f;
        var edge = Mathf.Lerp(open, closed, weight);
        var color = new Color(m_SquintColor.r, m_SquintColor.g, m_SquintColor.b, m_SquintStrength * Mathf.Clamp01(weight * 2f));
        m_SquintTop.rectTransform.anchoredPosition = new Vector2(0f, edge);
        m_SquintBottom.rectTransform.anchoredPosition = new Vector2(0f, -edge);
        m_SquintTop.color = color;
        m_SquintBottom.color = color;
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

    // 덮개 하나: 아래 끝이 맑은 경계이고 위(화면 밖)로 갈수록 진해진다. 아래 덮개는 뒤집어 쓴다
    Image CreateSquintImage(Canvas canvas, string name, Sprite sprite, bool flip)
    {
        var image = CreateImage(canvas, name, sprite);
        var rect = image.rectTransform;
        var size = k_CanvasSize * 1000f;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(size, size);
        if (flip)
            rect.localRotation = Quaternion.Euler(0f, 0f, 180f);
        return image;
    }

    // 세로 그라데이션: 아래 끝(맑은 경계)에서 그라데이션 각도만큼 올라가면 완전히 진해진다
    Sprite CreateSquintSprite()
    {
        const int height = 256;
        var texture = new Texture2D(1, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Squint" };
        var clear = Mathf.Tan(m_SquintClearAngle * Mathf.Deg2Rad) * k_CanvasDistance;
        var soft = Mathf.Tan((m_SquintClearAngle + m_SquintSoftAngle) * Mathf.Deg2Rad) * k_CanvasDistance - clear;
        var pixels = new Color32[height];
        for (var y = 0; y < height; y++)
        {
            var distance = (y + 0.5f) / height * k_CanvasSize;
            var a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / soft));
            pixels[y] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, 1, height), new Vector2(0.5f, 0.5f));
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
