using UnityEngine;

/// <summary>
/// 탁자 옆의 문제 출제 모니터.
/// </summary>
public class QuizMonitor : MonoBehaviour
{
    [SerializeField] private TextMesh questionText;

    public void Show(string question, string[] choices)
    {
        if (questionText == null) return;
        var sb = new System.Text.StringBuilder(question);
        for (int i = 0; i < choices.Length; i++)
            sb.Append($"\n{(char)('A' + i)}. {choices[i]}");
        questionText.text = sb.ToString();
    }
}
