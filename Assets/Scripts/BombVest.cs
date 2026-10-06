using UnityEngine;

/// <summary>
/// 플레이어가 착용한 폭탄 조끼. 상대 플레이어 앞의 단말기(DetonatorTerminal)가 이 조끼의 기폭장치다.
/// </summary>
public class BombVest : MonoBehaviour
{
    [SerializeField] private string ownerId;
    [SerializeField] private Renderer statusLed;
    [SerializeField] private LifeGauge lifeGauge;

    public string OwnerId => ownerId;
    public LifeGauge LifeGauge => lifeGauge;
    public bool IsDetonated { get; private set; }

    public void Detonate()
    {
        if (IsDetonated) return;
        IsDetonated = true;
        if (statusLed != null) statusLed.material.SetColor("_EmissionColor", Color.white * 8f);
        Debug.Log($"[BombVest] {ownerId}의 폭탄 기폭!");
    }
}
