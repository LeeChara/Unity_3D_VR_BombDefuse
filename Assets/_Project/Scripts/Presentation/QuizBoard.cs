using UnityEngine;

/// <summary>
/// 두 플레이어의 퀴즈 화면을 함께 다룬다. 같은 문제와 남은 시간을 모든 화면에 동시에 띄운다.
/// </summary>
public class QuizBoard : MonoBehaviour
{
    [SerializeField] QuizScreen[] m_Screens;

    // Start가 아니라 Awake에서 비워야 다른 스크립트가 Start에서 띄운 문제를 지우지 않는다
    void Awake() => Clear();

    /// <summary>화면들의 가운데 (퀴즈 효과음이 나는 곳). 화면이 없으면 이 오브젝트 위치.</summary>
    public Vector3 Center
    {
        get
        {
            var sum = Vector3.zero;
            var count = 0;
            foreach (var screen in m_Screens)
            {
                if (screen == null)
                    continue;
                sum += screen.transform.position;
                count++;
            }
            return count > 0 ? sum / count : transform.position;
        }
    }

    public void Show(QuizQuestion question)
    {
        foreach (var screen in m_Screens)
            screen.Show(question);
    }

    /// <summary>남은 시간 비율 (1 = 가득, 0 = 없음).</summary>
    public void SetRemainingTime(float ratio)
    {
        foreach (var screen in m_Screens)
            screen.SetRemainingTime(ratio);
    }

    public void Clear()
    {
        foreach (var screen in m_Screens)
            screen.Clear();
    }
}
