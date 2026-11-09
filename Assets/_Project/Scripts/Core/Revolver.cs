using System;

/// <summary>
/// 리볼버 약실. 네트워크와 씬을 모르는 순수 C#이다 (docs/01_rules_essential.md 4절).
/// 방아쇠를 당길 때마다 지금 칸을 쏘고 한 칸 돈다. 실탄을 쏘면 그 칸은 비고,
/// 진행자가 <see cref="ReloadAfterShot"/>으로 새 총알을 무작위 칸에 넣고 실린더를 돌린다.
/// 약실 상태는 호스트만 알고 클라이언트에 보내지 않는다. 그래서 칸 정보를 밖에 공개하지 않는다.
/// </summary>
public sealed class Revolver
{
    readonly bool[] m_Chambers;
    readonly Random m_Random;
    int m_Position;

    /// <param name="chamberCount">약실 수</param>
    /// <param name="bulletCount">처음 넣을 총알 수 (1 ~ 약실 수)</param>
    public Revolver(int chamberCount, int bulletCount, Random random = null)
    {
        if (chamberCount < 1)
            throw new ArgumentOutOfRangeException(nameof(chamberCount));

        m_Chambers = new bool[chamberCount];
        m_Random = random ?? new Random();

        var count = Math.Max(1, Math.Min(bulletCount, chamberCount));
        for (var i = 0; i < count; i++)
            LoadRandomEmptyChamber();
        Spin();
    }

    public int ChamberCount => m_Chambers.Length;

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

    /// <summary>실탄을 쏜 뒤 진행자가 다시 장전한다. 새 총알을 빈 칸 하나에 넣고 실린더를 돌린다.</summary>
    public void ReloadAfterShot()
    {
        LoadRandomEmptyChamber();
        Spin();
    }

    void LoadRandomEmptyChamber()
    {
        var empty = m_Chambers.Length - BulletCount;
        if (empty == 0)
            return;

        // 빈 칸 중 하나를 고른다
        var pick = m_Random.Next(empty);
        for (var i = 0; i < m_Chambers.Length; i++)
        {
            if (m_Chambers[i])
                continue;
            if (pick-- == 0)
            {
                m_Chambers[i] = true;
                return;
            }
        }
    }
}
