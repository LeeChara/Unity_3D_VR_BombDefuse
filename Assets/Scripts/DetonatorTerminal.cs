using UnityEngine;

/// <summary>
/// 플레이어 앞에 놓인 기폭 단말기. 연결된 폭탄은 맞은편(상대) 플레이어의 조끼다.
/// 키패드/정답 버튼/전선 입력을 받아 화면에 표시한다. (판정은 추후 게임 로직이 담당)
/// </summary>
public class DetonatorTerminal : MonoBehaviour
{
    private const int MaxInputLength = 6;

    [SerializeField] private string holderId;
    [SerializeField] private BombVest targetVest;
    [SerializeField] private DetonatorWire[] wires;
    [SerializeField] private PressableButton[] keypadKeys;
    [SerializeField] private PressableButton[] answerButtons;
    [SerializeField] private PressableButton triggerButton;
    [SerializeField] private TextMesh screenText;

    public string HolderId => holderId;
    public BombVest TargetVest => targetVest;
    public DetonatorWire[] Wires => wires;

    private string keypadInput = "";
    private string status = "LINK : ARMED";

    private void OnEnable()
    {
        foreach (var key in keypadKeys) key.Pressed += OnKeypad;
        foreach (var button in answerButtons) button.Pressed += OnAnswer;
        foreach (var wire in wires) wire.CutPerformed += OnWireCut;
        if (triggerButton != null) triggerButton.Pressed += OnTrigger;
        Refresh();
    }

    private void OnDisable()
    {
        foreach (var key in keypadKeys) key.Pressed -= OnKeypad;
        foreach (var button in answerButtons) button.Pressed -= OnAnswer;
        foreach (var wire in wires) wire.CutPerformed -= OnWireCut;
        if (triggerButton != null) triggerButton.Pressed -= OnTrigger;
    }

    private void OnKeypad(PressableButton key)
    {
        switch (key.Label)
        {
            case "*":
                keypadInput = "";
                break;
            case "#":
                status = $"SUBMIT : {(keypadInput.Length > 0 ? keypadInput : "-")}";
                Debug.Log($"[DetonatorTerminal] {holderId} 숫자 입력 제출: {keypadInput}");
                keypadInput = "";
                break;
            default:
                if (keypadInput.Length < MaxInputLength) keypadInput += key.Label;
                break;
        }
        Refresh();
    }

    private void OnAnswer(PressableButton button)
    {
        status = $"ANSWER : {button.Label}";
        Debug.Log($"[DetonatorTerminal] {holderId} 정답 선택: {button.Label}");
        Refresh();
    }

    private void OnWireCut(DetonatorWire wire)
    {
        status = $"WIRE {wire.Index + 1} {wire.ColorName.ToUpper()} CUT";
        Refresh();
    }

    private void OnTrigger(PressableButton _)
    {
        status = "!! DETONATE !!";
        Refresh();
        Trigger();
    }

    /// <summary>게임 로직이 화면 상태줄을 덮어쓸 때 사용</summary>
    public void SetStatus(string text)
    {
        status = text;
        Refresh();
    }

    private void Refresh()
    {
        if (screenText == null) return;
        string target = targetVest != null ? $"PLAYER {targetVest.OwnerId}" : "---";
        string input = keypadKeys.Length > 0 ? $"\n<size=34>> {keypadInput}_</size>" : "";
        screenText.text = $"TARGET : {target}\n<size=34>{status}</size>{input}";
    }

    public void Trigger()
    {
        if (targetVest != null) targetVest.Detonate();
    }
}
