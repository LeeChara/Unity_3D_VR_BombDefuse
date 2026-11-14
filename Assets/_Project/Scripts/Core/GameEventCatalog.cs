using System.Collections.Generic;

/// <summary>
/// 게임 이벤트 목록 (docs/07_event_list.md 3절과 같다). 이벤트를 추가하면 여기와 07 문서를 함께 고친다.
/// 효과음 연결 도구가 이 목록으로 효과음 칸을 만들고, 빠진 소리를 알려 준다.
/// </summary>
public static class GameEventCatalog
{
    public class Info
    {
        public string Name;

        /// <summary>언제 나는지.</summary>
        public string When;

        /// <summary>함께 오는 정보 (<see cref="GameEvent"/>의 어느 칸에 무엇이 오는지).</summary>
        public string Fields;

        /// <summary>효과음 기본 위치. 효과음이 필요 없는 이벤트는 null.</summary>
        public SoundLocation? DefaultLocation;

        public SoundAudience DefaultAudience;

        /// <summary>사수 기기에서 나서 상대 기기로 전달되는 이벤트 (총을 직접 만지는 일). 나머지는 호스트만 낸다.</summary>
        public bool FromShooter;

        /// <summary>네트워크로 보내지 않고 이 기기에서만 나는 이벤트.</summary>
        public bool LocalOnly;

        public bool HasSound => DefaultLocation.HasValue;
    }

    static readonly List<Info> s_All = new List<Info>
    {
        // 착석
        New("seat_prompt", "착석 단계에 들어갈 때", "–", null),
        New("player_seated", "한 사람이 스스로 앉을 때", "Player", SoundLocation.Seat),
        New("seat_warning", "15초 동안 누군가 안 앉았을 때", "Player = 안 앉은 첫 좌석, Count = 안 앉은 사람 수", null),
        New("seat_forced", "경고 후 10초가 지나 강제로 앉힐 때 (좌석마다)", "Player", SoundLocation.Seat),
        New("rules_intro", "규칙 설명 단계에 들어갈 때", "–", null),

        // 퀴즈
        New("quiz_start", "문제를 낼 때", "Count = 제한시간(초)", SoundLocation.QuizScreen),
        New("quiz_pressure", "출제 후 5초마다", "Count = 남은 시간(초)", null),
        New("answer_wrong", "오답을 눌렀을 때", "Player, Count = 고른 번호 (0 = A)", SoundLocation.Terminal),
        New("answer_correct", "정답자가 정해졌을 때", "Player", SoundLocation.QuizScreen),
        New("quiz_timeout", "아무도 못 맞히고 시간이 끝났을 때", "–", SoundLocation.QuizScreen),
        New("button_pressed", "단말기 버튼을 누른 순간 (누른 기기에서만)", "Player, Count = 고른 번호", SoundLocation.Terminal, local: true),

        // 총 지급과 사수 턴
        New("gun_handover", "사수 앞에 총과 여분 총알이 나타날 때", "Player = 사수, Target = 대상, Count = 총알 개수", SoundLocation.Gun),
        New("gun_grabbed", "총을 잡은 순간 (잡은 기기에서만)", "Player = 사수", SoundLocation.Gun, local: true),
        New("cylinder_opened", "실린더가 열렸을 때", "Player = 사수", SoundLocation.Gun, fromShooter: true),
        New("bullet_inserted", "총알 한 발이 들어갔을 때", "Player = 사수, Count = 총알 개수", SoundLocation.Gun, fromShooter: true),
        New("cylinder_spun", "실린더를 닫았을 때 (넣은 총알이 있으면 돌아간다)", "Player = 사수, Count = 넣은 수", SoundLocation.Gun, fromShooter: true),
        New("shoot_again", "자신을 쐈는데 공포탄이라 한 번 더 쏠 때", "Player = 사수", null),
        New("gun_dropped", "총을 떨어뜨렸을 때", "Player = 사수", SoundLocation.Gun, fromShooter: true),
        New("gun_returned", "떨어뜨린 총이 테이블로 돌아왔을 때", "Player = 사수", null, fromShooter: true),
        New("shooter_timeout", "사수 턴 시간이 지나 총이 반납될 때", "Player = 사수", null),

        // 사격 (발사·공이치기·딸깍은 총의 GunEffects가 낸다)
        New("hammer_cock", "방아쇠를 당긴 순간", "Player = 사수", null, fromShooter: true),
        New("shot_hit_opponent", "실탄이 상대에게 맞았을 때", "Player = 사수, Target = 대상, Part, Position = 맞은 지점", SoundLocation.EventPosition),
        New("shot_blank_opponent", "상대를 쐈는데 공포탄일 때", "Player = 사수, Target = 대상", null),
        New("shot_miss", "실탄이 아무 몸에도 맞지 않았을 때", "Player = 사수, Position = 탄이 닿은 지점", SoundLocation.EventPosition),
        New("shot_blank_self", "자신을 쐈는데 공포탄일 때", "Player = 사수", null),
        New("shot_hit_self", "자신을 쏘아 실탄이 맞았을 때", "Player = 사수, Part, Position = 맞은 지점", SoundLocation.EventPosition),
        New("player_wounded", "몸통을 맞아 중상이 되었을 때", "Player = 맞은 좌석", SoundLocation.Listener, SoundAudience.EventPlayer),
        New("reload", "총알이 0발이 되어 진행자가 장전할 때 (결과 브리핑에서)", "Count = 새 총알 개수", SoundLocation.Host),

        // 사망과 종료
        New("player_died", "사망자가 확인되었을 때", "Player = 죽은 좌석", SoundLocation.Body),
        New("player_won", "승자가 정해졌을 때", "Player = 이긴 좌석", SoundLocation.Listener),
        New("opponent_disconnected", "게임 중에 상대가 나갔을 때 (남은 기기에서만)", "Player = 나간 좌석", null, local: true),
    };

    static Dictionary<string, Info> s_ByName;

    public static IReadOnlyList<Info> All => s_All;

    /// <summary>이름으로 찾는다. 목록에 없으면 null.</summary>
    public static Info Find(string name)
    {
        if (s_ByName == null)
        {
            s_ByName = new Dictionary<string, Info>();
            foreach (var info in s_All)
                s_ByName[info.Name] = info;
        }

        return name != null && s_ByName.TryGetValue(name, out var found) ? found : null;
    }

    static Info New(string name, string when, string fields, SoundLocation? location,
        SoundAudience audience = SoundAudience.Everyone, bool fromShooter = false, bool local = false) =>
        new Info
        {
            Name = name,
            When = when,
            Fields = fields,
            DefaultLocation = location,
            DefaultAudience = audience,
            FromShooter = fromShooter,
            LocalOnly = local,
        };
}
