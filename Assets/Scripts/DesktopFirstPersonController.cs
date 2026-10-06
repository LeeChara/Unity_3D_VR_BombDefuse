using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// HMD 없이 테스트하기 위한 데스크톱 1인칭 조작.
/// 마우스로 시점 회전, 좌클릭으로 조준한 오브젝트(IInteractable) 조작, Esc로 커서 해제.
/// </summary>
[RequireComponent(typeof(Camera))]
public class DesktopFirstPersonController : MonoBehaviour
{
    [Header("Look")]
    [SerializeField] private float sensitivity = 0.12f;
    [SerializeField] private float yawLimit = 110f;
    [SerializeField] private float minPitch = -75f;
    [SerializeField] private float maxPitch = 60f;

    [Header("Interact")]
    [SerializeField] private float interactRange = 1.5f;
    [SerializeField] private LayerMask interactMask = ~0;

    [Tooltip("1인칭에서 시야를 가리는 자기 몸(머리, 팔 등). 시작 시 비활성화한다.")]
    [SerializeField] private GameObject[] hideInFirstPerson;

    private Quaternion baseRotation;
    private float yaw, pitch;
    private IInteractable hovered;
    private GUIStyle promptStyle;

    private void Awake()
    {
        // 좌석 정면(시작 시 yaw)을 기준으로 좌우 회전을 제한한다
        baseRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        pitch = NormalizeAngle(transform.eulerAngles.x);
        foreach (var go in hideInFirstPerson)
            if (go != null) go.SetActive(false);
    }

    private void Start() => LockCursor(true);

    /// <summary>시점을 다른 좌석으로 옮긴다. 회전 제한의 기준도 그 좌석 정면으로 바뀐다.</summary>
    public void SetView(Transform eyeAnchor, float startPitch)
    {
        transform.position = eyeAnchor.position;
        baseRotation = Quaternion.Euler(0f, eyeAnchor.eulerAngles.y, 0f);
        yaw = 0f;
        pitch = startPitch;
        transform.rotation = baseRotation * Quaternion.Euler(pitch, yaw, 0f);
    }

    private void OnDisable() => LockCursor(false);

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) LockCursor(false);

        bool locked = Cursor.lockState == CursorLockMode.Locked;
        if (locked)
        {
            Vector2 delta = mouse.delta.ReadValue() * sensitivity;
            yaw = Mathf.Clamp(yaw + delta.x, -yawLimit, yawLimit);
            pitch = Mathf.Clamp(pitch - delta.y, minPitch, maxPitch);
            transform.rotation = baseRotation * Quaternion.Euler(pitch, yaw, 0f);
        }

        hovered = locked ? FindInteractable() : null;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (!locked) LockCursor(true);
            else hovered?.Interact();
        }
    }

    private IInteractable FindInteractable()
    {
        Ray ray = new Ray(transform.position, transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, interactMask, QueryTriggerInteraction.Ignore))
            return null;
        IInteractable target = hit.collider.GetComponentInParent<IInteractable>();
        return target != null && target.CanInteract ? target : null;
    }

    private static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private static float NormalizeAngle(float a) => a > 180f ? a - 360f : a;

    private void OnGUI()
    {
        promptStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = 18,
            normal = { textColor = Color.white },
        };

        float cx = Screen.width / 2f, cy = Screen.height / 2f;
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            GUI.Label(new Rect(cx - 200, cy - 12, 400, 24), "Click to look around", promptStyle);
            return;
        }

        // 크로스헤어: 조준 대상이 있으면 크고 노랗게
        float size = hovered != null ? 8f : 4f;
        GUI.color = hovered != null ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 1f, 1f, 0.7f);
        GUI.DrawTexture(new Rect(cx - size / 2, cy - size / 2, size, size), Texture2D.whiteTexture);
        GUI.color = Color.white;

        if (hovered != null)
            GUI.Label(new Rect(cx - 200, cy + 14, 400, 24), $"{hovered.Prompt}  (LMB)", promptStyle);
        GUI.Label(new Rect(10, Screen.height - 30, 500, 24), "Mouse: look   LMB: interact   Esc: release cursor");
    }
}
