/// <summary>
/// 게임 진행 상태. 각 상태가 하는 일과 다음 상태는 docs/03_tech_review.md 3절 상태 표를 따른다.
/// </summary>
public enum GameState
{
    /// <summary>두 사람 접속 대기.</summary>
    Lobby,
    /// <summary>각자 의자 뒤에서 등장. 착석 안내 → 경고 → 강제 착석.</summary>
    Seating,
    /// <summary>규칙 설명 세 문장.</summary>
    Intro,
    /// <summary>문제 출제, 버튼 입력, 압박 대사.</summary>
    QuizActive,
    /// <summary>정답 판정 (동시 판정 포함).</summary>
    QuizResolve,
    /// <summary>정답자 앞에 총과 여분 총알 지급.</summary>
    GunHandover,
    /// <summary>사수가 상대 쏘기, 자신 쏘기(공포탄이면 한 번 더), 여분 총알 넣고 돌리기 중에 고른다.</summary>
    ShooterTurn,
    /// <summary>결과 처리. 총알이 0발이면 진행자가 재장전한다(넣는 수가 매번 늘어남).</summary>
    ShotResolve,
    /// <summary>결과 브리핑 (진행자 반응).</summary>
    HostReaction,
    /// <summary>아무도 못 맞혔을 때 공포 단계 +1. 나중에 구현한다.</summary>
    BothFailPenalty,
    /// <summary>사망자 확인.</summary>
    DeathCheck,
    /// <summary>승리·사망 연출.</summary>
    GameOver,
}
