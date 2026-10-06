using UnityEngine;

/// <summary>
/// 조끼에 달린 목숨 계기판. 남은 목숨 수만큼 LED가 켜진다.
/// </summary>
public class LifeGauge : MonoBehaviour
{
    [SerializeField] private Renderer[] leds;
    [SerializeField] private Material onMaterial;
    [SerializeField] private Material offMaterial;

    public int MaxLives => leds.Length;
    public int Lives { get; private set; }

    private void Awake() => SetLives(MaxLives);

    public void SetLives(int lives)
    {
        Lives = Mathf.Clamp(lives, 0, MaxLives);
        for (int i = 0; i < leds.Length; i++)
            leds[i].sharedMaterial = i < Lives ? onMaterial : offMaterial;
    }
}
