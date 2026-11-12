using UnityEngine;

/// <summary>
/// 방의 좌석 목록. 좌석 번호는 배열 순서(0 = 좌석 1, 1 = 좌석 2)다. 씬에 하나만 둔다.
/// </summary>
public class SeatRegistry : MonoBehaviour
{
    [SerializeField, Tooltip("좌석 1, 좌석 2 순서")] Seat[] m_Seats = new Seat[2];

    static SeatRegistry s_Instance;

    public static SeatRegistry Instance => s_Instance;

    public int Count => m_Seats.Length;

    void Awake() => s_Instance = this;

    void OnDestroy()
    {
        if (s_Instance == this)
            s_Instance = null;
    }

    /// <summary>번호에 맞는 좌석. 범위를 벗어나면 null.</summary>
    public Seat GetSeat(int index) => index >= 0 && index < m_Seats.Length ? m_Seats[index] : null;
}
