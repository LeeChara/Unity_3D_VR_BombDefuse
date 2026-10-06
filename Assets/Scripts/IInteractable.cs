/// <summary>
/// 시선/포인터로 조작할 수 있는 오브젝트. 데스크톱 모드와 VR 입력이 같은 경로로 호출한다.
/// </summary>
public interface IInteractable
{
    string Prompt { get; }
    bool CanInteract { get; }
    void Interact();
}
