using System;
using System.Collections.Generic;

/// <summary>
/// 이벤트마다 진행자 대사 풀을 두고 무작위로 고른다. 같은 이벤트에서 같은 대사가 연속으로 나오지 않는다.
/// </summary>
public class HostLinePool
{
    readonly Dictionary<string, List<HostLine>> m_Pools = new Dictionary<string, List<HostLine>>();
    readonly Dictionary<string, HostLine> m_LastPicked = new Dictionary<string, HostLine>();
    readonly Random m_Random;

    public HostLinePool(IEnumerable<HostLine> lines, Random random = null)
    {
        m_Random = random ?? new Random();
        foreach (var line in lines)
        {
            if (!m_Pools.TryGetValue(line.Event, out var pool))
                m_Pools[line.Event] = pool = new List<HostLine>();
            pool.Add(line);
        }
    }

    public bool HasEvent(string eventName) => m_Pools.ContainsKey(eventName);

    /// <summary>이 이벤트의 대사를 하나 고른다. 대사가 없으면 null.</summary>
    public HostLine Pick(string eventName)
    {
        if (!m_Pools.TryGetValue(eventName, out var pool) || pool.Count == 0)
            return null;

        m_LastPicked.TryGetValue(eventName, out var last);
        HostLine picked;
        var lastIndex = last != null ? pool.IndexOf(last) : -1;
        if (pool.Count == 1 || lastIndex < 0)
        {
            picked = pool[m_Random.Next(pool.Count)];
        }
        else
        {
            // 직전 대사를 뺀 나머지 중에서 고른다
            var index = m_Random.Next(pool.Count - 1);
            if (index >= lastIndex)
                index++;
            picked = pool[index];
        }

        m_LastPicked[eventName] = picked;
        return picked;
    }
}
