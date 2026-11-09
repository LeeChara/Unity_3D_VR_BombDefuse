using System;

/// <summary>
/// 리볼버 약실. 네트워크와 씬을 모르는 순수 C#이다 (docs/01_rules_essential.md 4절).
/// 방아쇠를 당길 때마다 지금 칸을 쏘고 한 칸 돈다. 실탄을 쏘면 그 칸은 빈다.
/// 총알이 0발이 되면 진행자가 <see cref="ReloadIfEmpty"/>로 장전하는데, 장전할 때마다 넣는 수가 늘어난다 (최대 약실 수).
/// 사수는 여분 총알을 <see cref="Insert"/>로 넣고 <see cref="Spin"/>으로 돌릴 수 있다.
/// 약실 상태는 호스트만 알고 클라이언트에 보내지 않는다. 그래서 칸 정보를 밖에 공개하지 않는다.
/// </summary>
public sealed class Revolver
{
    readonly bool[] m_Chambers;
    readonly Random m_Random;
    readonly int m_ReloadIncrement;
    int m_Position;

    /// <param name="chamberCount">약실 수</param>
    /// <param name="startingBullets">처음 넣을 총알 수 (1 ~ 약실 수)</param>
    /// <param name="reloadIncrement">재장전할 때마다 넣는 총알이 늘어나는 수</param>
    public Revolver(int chamberCount, int startingBullets, int reloadIncrement = 0, Random random = null)
    {
        if (chamberCount < 1)
            throw new ArgumentOutOfRangeException(nameof(chamberCount));

        m_Chambers = new bool[chamberCount];
        m_Random = random ?? new Random();
        m_ReloadIncrement = Math.Max(0, reloadIncrement);

        ReloadSize = Math.Max(1, Math.Min(startingBullets, chamberCount));
        for (var i = 0; i < ReloadSize; i++)
            Insert();
        Spin();
    }

    public int ChamberCount => m_Chambers.Length;

    /// <summary>마지막으로 진행자가 넣은 총알 수. 다음 재장전은 이보다 늘어난다.</summary>
    public int ReloadSize { get; private set; }

    /// <summary>지금 들어 있는 총알 수. 개수는 진행자가 말로 알려 주는 공개 정보다.</summary>
    public int BulletCount
    {
        get
        {
            var count = 0;
            foreach (var loaded in m_Chambers)
                if (loaded)
                    count++;
            return count;
        }
    }

    public bool IsFull => BulletCount == m_Chambers.Length;

    /// <summary>방아쇠를 당긴다. 실탄이면 true. 쏜 칸은 비고, 실린더는 한 칸 돈다.</summary>
    public bool Pull()
    {
        var live = m_Chambers[m_Position];
        m_Chambers[m_Position] = false;
        m_Position = (m_Position + 1) % m_Chambers.Length;
        return live;
    }

    /// <summary>실린더를 무작위 위치로 돌린다.</summary>
    public void Spin() => m_Position = m_Random.Next(m_Chambers.Length);

    /// <summary>빈 칸 하나에 총알을 넣는다. 꽉 차 있으면 false.</summary>
    public bool Insert()
    {
        var empty = m_Chambers.Length - BulletCount;
        if (empty == 0)
            return false;

        var pick = m_Random.Next(empty);
        for (var i = 0; i < m_Chambers.Length; i++)
        {
            if (m_Chambers[i])
                continue;
            if (pick-- == 0)
            {
                m_Chambers[i] = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 총알이 0발이면 진행자가 장전한다. 넣는 수는 지난번보다 늘어나고(최대 약실 수), 실린더를 돌린다.
    /// 장전했으면 true.
    /// </summary>
    public bool ReloadIfEmpty()
    {
        if (BulletCount > 0)
            return false;

        ReloadSize = Math.Min(ReloadSize + m_ReloadIncrement, m_Chambers.Length);
        for (var i = 0; i < ReloadSize; i++)
            Insert();
        Spin();
        return true;
    }
}
