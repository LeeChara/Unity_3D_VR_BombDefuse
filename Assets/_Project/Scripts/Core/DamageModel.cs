/// <summary>맞은 부위.</summary>
public enum BodyPart
{
    Head,
    Torso,
    /// <summary>필수 버전에서는 몸통과 같이 처리한다.</summary>
    Arm,
}

/// <summary>피격 결과.</summary>
public enum DamageOutcome
{
    /// <summary>중상이 되었다 (몸통·팔).</summary>
    Wounded,
    /// <summary>죽었다 (머리, 또는 중상에서 몸통을 또 맞음).</summary>
    Killed,
    /// <summary>이미 죽어 있어서 아무 일도 없었다.</summary>
    Ignored,
}

/// <summary>
/// 한 사람의 피해 상태. 네트워크와 씬을 모르는 순수 C#이다 (docs/01_rules_essential.md 6절).
/// 머리는 즉사. 몸통(팔 포함)은 허용 횟수까지 중상이고, 넘으면 죽는다.
/// </summary>
public sealed class DamageModel
{
    readonly int m_TorsoHitsAllowed;

    /// <param name="torsoHitsAllowed">몸통 피격 허용 횟수. 이 횟수를 넘으면 죽는다</param>
    public DamageModel(int torsoHitsAllowed)
    {
        m_TorsoHitsAllowed = torsoHitsAllowed < 0 ? 0 : torsoHitsAllowed;
    }

    public int TorsoHits { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsWounded => !IsDead && TorsoHits > 0;

    public DamageOutcome ApplyHit(BodyPart part)
    {
        if (IsDead)
            return DamageOutcome.Ignored;

        if (part == BodyPart.Head)
        {
            IsDead = true;
            return DamageOutcome.Killed;
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
    }
}
