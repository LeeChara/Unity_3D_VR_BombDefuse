/// <summary>진행자 대사 한 줄 (대사 CSV의 한 줄).</summary>
public class HostLine
{
    /// <summary>이벤트 이름 (docs/07_event_list.md). 이 이름으로 대사 풀을 찾는다.</summary>
    public string Event;
    public string Id;
    /// <summary>대사. {bullets}처럼 중괄호로 감싼 이름은 진행 중인 값으로 바뀐다.</summary>
    public string Text;
    /// <summary>감정 (목소리·표정용, 지금은 쓰지 않는다).</summary>
    public string Emotion;
}
