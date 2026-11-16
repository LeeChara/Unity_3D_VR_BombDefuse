/// <summary>맞은 부위. 값은 네트워크와 프리팹에 숫자로 저장되므로 순서를 바꾸지 않는다.</summary>
public enum BodyPart
{
    Head,
    Torso,
    LeftArm,
    RightArm,
}

/// <summary>피격 결과.</summary>
public enum DamageOutcome
{
    /// <summary>중상이 되었다 (몸통).</summary>
    Wounded,
    /// <summary>죽었다 (머리, 또는 중상에서 몸통을 또 맞음).</summary>
    Killed,
    /// <summary>이미 죽어 있어서 아무 일도 없었다.</summary>
    Ignored,
    /// <summary>팔을 맞아 그 팔을 쓸 수 없게 되었다 (이미 못 쓰는 팔이면 그대로).</summary>
    ArmDisabled,
}

/// <summary>
/// 한 사람의 피해 상태. 네트워크와 씬을 모르는 순수 C#이다 (docs/01_rules_essential.md 6절).
/// 머리는 즉사. 몸통은 허용 횟수까지 중상이고, 넘으면 죽는다. 팔은 그 팔을 쓸 수 없게 될 뿐 죽지 않는다.
/// </summary>
public sealed class DamageModel
{
    readonly int m_TorsoHitsAllowed;
    readonly bool[] m_ArmDisabled = new bool[2];

    /// <param name="torsoHitsAllowed">몸통 피격 허용 횟수. 이 횟수를 넘으면 죽는다</param>
    public DamageModel(int torsoHitsAllowed)
    {
        m_TorsoHitsAllowed = torsoHitsAllowed < 0 ? 0 : torsoHitsAllowed;
    }

    public int TorsoHits { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsWounded => !IsDead && TorsoHits > 0;

    /// <summary>처음 상태가 아닌지 (죽음, 중상, 못 쓰는 팔).</summary>
    public bool HasInjury => IsDead || TorsoHits > 0 || m_ArmDisabled[0] || m_ArmDisabled[1];

    /// <summary>이 팔을 쓸 수 없는지. 팔이 아닌 부위는 false.</summary>
    public bool IsArmDisabled(BodyPart arm) => ArmIndex(arm) is var index && index >= 0 && m_ArmDisabled[index];

    public DamageOutcome ApplyHit(BodyPart part)
    {
        if (IsDead)
            return DamageOutcome.Ignored;

        switch (part)
        {
            case BodyPart.Head:
                IsDead = true;
                return DamageOutcome.Killed;
            case BodyPart.LeftArm:
            case BodyPart.RightArm:
                m_ArmDisabled[ArmIndex(part)] = true;
                return DamageOutcome.ArmDisabled;
        }

        TorsoHits++;
        if (TorsoHits > m_TorsoHitsAllowed)
        {
            IsDead = true;
            return DamageOutcome.Killed;
        }

        return DamageOutcome.Wounded;
    }

    /// <summary>처음 상태로 되돌린다.</summary>
    public void Reset()
    {
        TorsoHits = 0;
        IsDead = false;
        m_ArmDisabled[0] = false;
        m_ArmDisabled[1] = false;
    }

    static int ArmIndex(BodyPart part) => part == BodyPart.LeftArm ? 0 : part == BodyPart.RightArm ? 1 : -1;
}
