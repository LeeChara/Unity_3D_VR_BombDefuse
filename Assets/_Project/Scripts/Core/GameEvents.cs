using System;
using UnityEngine;

/// <summary>
/// 게임 이벤트 하나 (docs/07_event_list.md 3절). 이름과 함께 누가, 몇 개, 어디인지가 온다.
/// 각 칸이 무슨 뜻인지는 이벤트마다 다르다 (<see cref="GameEventCatalog"/>의 설명).
/// </summary>
public struct GameEvent
{
    public string Name;

    /// <summary>주인공 좌석 (1부터). 없으면 0.</summary>
    public int Player;

    /// <summary>대상 좌석 (1부터). 없으면 0.</summary>
    public int Target;

    /// <summary>개수 (총알 개수, 넣은 수, 남은 시간 등). 없으면 -1.</summary>
    public int Count;

    public bool HasPart;
    public BodyPart Part;

    /// <summary>소리가 날 위치가 정해져 있는지 (맞은 지점 등).</summary>
    public bool HasPosition;
    public Vector3 Position;

    /// <summary>이 기기에서만 쓰고 네트워크로 보내지 않는다 (버튼 클릭, 총 잡기, 에디터 시험 재생).</summary>
    public bool LocalOnly;

    /// <summary>네트워크로 받은 이벤트. 다시 보내지 않는다.</summary>
    public bool FromNetwork;

    public GameEvent(string name, int player = 0, int target = 0, int count = -1)
    {
        Name = name;
        Player = player;
        Target = target;
        Count = count;
        HasPart = false;
        Part = default;
        HasPosition = false;
        Position = default;
        LocalOnly = false;
        FromNetwork = false;
    }

    public GameEvent WithPart(BodyPart part)
    {
        HasPart = true;
        Part = part;
        return this;
    }

    public GameEvent WithPosition(Vector3 position)
    {
        HasPosition = true;
        Position = position;
        return this;
    }

    public override string ToString()
    {
        var text = Name;
        if (Player > 0)
            text += $" player={Player}";
        if (Target > 0)
            text += $" target={Target}";
        if (Count >= 0)
            text += $" count={Count}";
        if (HasPart)
            text += $" part={Part}";
        return text;
    }
}

/// <summary>
/// 게임 이벤트 통로. 효과음·연출처럼 이벤트에 반응하는 쪽은 <see cref="Raised"/>를 구독한다.
/// 진행 이벤트는 호스트의 진행 컨트롤러가 내고, 네트워크 쪽(LanEventSync)이 클라이언트에 전달해 그쪽에서도 <see cref="Raised"/>가 난다.
/// 총을 직접 만지는 이벤트(실린더 열기 등)는 사수 기기에서 나고 상대 기기로 전달된다.
/// </summary>
public static class GameEvents
{
    /// <summary>이벤트가 났을 때. 이 기기에서 난 것과 네트워크로 받은 것 모두 온다.</summary>
    public static event Action<GameEvent> Raised;

    /// <summary>켜면 이벤트마다 콘솔에 남긴다.</summary>
    public static bool LogEvents = true;

    public static void Raise(GameEvent gameEvent)
    {
        if (string.IsNullOrEmpty(gameEvent.Name))
            return;

        if (LogEvents)
            Debug.Log($"[Event] {gameEvent}{(gameEvent.FromNetwork ? " (받음)" : "")}");
        Raised?.Invoke(gameEvent);
    }

    public static void Raise(string name, int player = 0, int target = 0, int count = -1) =>
        Raise(new GameEvent(name, player, target, count));

#if UNITY_EDITOR
    // 도메인 리로드를 끈 상태에서 Play를 다시 눌러도 구독이 남지 않게 한다
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Raised = null;
#endif
}
