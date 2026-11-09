using System;
using UnityEngine;

/// <summary>
/// 총에 맞을 수 있는 몸. 자식의 <see cref="Hitbox"/>가 맞으면 여기로 알린다.
/// 지금은 테스트용 마네킹에 붙어 있고, 나중에 플레이어 아바타에도 붙인다.
/// </summary>
public class Damageable : MonoBehaviour
{
    [SerializeField, Tooltip("수치 (몸통 피격 허용 횟수)")] TuningConfig m_Config;

    DamageModel m_Model;

    /// <summary>맞았을 때 (부위, 결과).</summary>
    public event Action<BodyPart, DamageOutcome> Damaged;

    /// <summary>처음 상태로 돌아왔을 때.</summary>
    public event Action Revived;

    public bool IsDead => Model.IsDead;
    public bool IsWounded => Model.IsWounded;

    DamageModel Model
    {
        get
        {
            if (m_Model == null)
            {
                if (m_Config == null)
                    Debug.LogWarning($"[Damageable] {name}에 TuningConfig가 연결되지 않아 몸통 허용 1회로 동작한다", this);
                m_Model = new DamageModel(m_Config != null ? m_Config.TorsoHitsAllowed : 1);
            }

            return m_Model;
        }
    }

    public DamageOutcome ApplyHit(BodyPart part)
    {
        var outcome = Model.ApplyHit(part);
        if (outcome == DamageOutcome.Ignored)
            return outcome;

        Debug.Log($"[Damageable] {name}: {part} 피격 → {(outcome == DamageOutcome.Killed ? "사망" : "중상")}", this);
        Damaged?.Invoke(part, outcome);
        return outcome;
    }

    [ContextMenu("Revive")]
    public void Revive()
    {
        Model.Reset();
        Debug.Log($"[Damageable] {name}: 처음 상태로 되돌림", this);
        Revived?.Invoke();
    }
}
