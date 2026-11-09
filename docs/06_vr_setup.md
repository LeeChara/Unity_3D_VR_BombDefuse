# 06. VR 프로젝트 설정

> 프로젝트는 **Unity 6.3 LTS (6000.3.9f1)** 의 **VR Multiplayer 템플릿**으로 새로 만들었다.
> XR·멀티플레이 기본 설정은 템플릿에 이미 들어 있어서, 에디터에서 따로 켜야 하는 것은 없다.

## 팀원 준비

1. Unity Hub에서 **6000.3.9f1** 설치. Quest 단독 빌드를 할 사람은 **Android Build Support** 모듈(OpenJDK, SDK & NDK 포함)도 같이 설치한다
2. `git lfs install` (한 번만)
3. 저장소를 받은 뒤 Hub에서 `Add project from disk`로 연다. 첫 실행은 `Library`를 새로 만들어서 오래 걸린다

## 들어 있는 것

| 항목 | 버전 / 상태 |
|---|---|
| XR Interaction Toolkit | 3.5.1 (Starter Assets, Hands, Interaction Simulator 샘플 포함) |
| OpenXR / XR Plug-in Management | 1.17.1 / 4.5.4 |
| XR 로더 | PC(Standalone), Android 모두 OpenXR 사용, `Initialize XR on Startup` 켜짐 |
| OpenXR 컨트롤러 프로필 (PC) | Oculus Touch, HTC Vive, Valve Index |
| OpenXR 기능 (Android) | Meta Quest Support, Oculus Touch / Touch Plus / Touch Pro |
| Netcode for GameObjects | 2.13.1 |
| Multiplayer Services (Lobby, Relay) | 2.3.0 |
| Vivox (보이스챗) | 16.11.0 |
| Multiplayer Play Mode | 2.0.2 |
| Unity Cloud | 조직 `radiorarian`에 연결됨. 로비·릴레이·Vivox가 이 연결을 쓴다 |
| 샘플 씬 | `Assets/Scenes/SampleScene.unity` (멀티플레이 예제), `Assets/Scenes/BasicScene.unity` |
| 메인 씬 | `Assets/_Project/Scenes/RoomScene.unity` |
| Unity Pipeline | 0.10.0-exp.1 (템플릿 이후 추가). 열린 에디터를 명령줄·MCP로 조작하는 에디터 전용 패키지. 빌드에는 들어가지 않는다 |
| 한글 폰트 | Noto Sans KR Regular/Bold (OFL). TextMesh Pro 기본 폰트로 설정 |

## 헤드셋으로 확인하기 (PC VR)

1. Quest를 Link 케이블 또는 Air Link로 PC에 연결
2. Meta Quest Link 앱 설정에서 **OpenXR 런타임을 Meta Quest Link로 지정**
3. 헤드셋에서 Link를 켜서 **Link 홈 화면에 들어간 상태**로 Play를 누른다. Play를 누를 때 헤드셋이 연결돼 있지 않으면 `XR_ERROR_FORM_FACTOR_UNAVAILABLE` 오류가 나고 그 Play 동안은 헤드셋으로 화면이 나오지 않는다
4. Interaction Simulator가 켜져 있으면 끈다 (아래)
5. 통과 기준: 고개를 돌리면 시점이 따라오고, 컨트롤러로 물체를 잡을 수 있다

## 헤드셋 없이 확인하기

- 템플릿에 포함된 XRI **Interaction Simulator**로 키보드와 마우스로 조작할 수 있다.
- 켜고 끄기: `Edit > Project Settings > XR Plug-in Management > XR Interaction Toolkit` → **Use XR Interaction Simulator in scenes**. 켜 두면 Play할 때 시뮬레이터가 자동으로 생기고, 헤드셋을 연결해도 입력을 시뮬레이터가 가져간다. 헤드셋으로 테스트할 때는 끈다.
- 2인 테스트: 아래 "2인 테스트" 참고

## 2인 테스트

### 에디터 하나로 (Multiplayer Play Mode)

1. `RoomScene`을 연다
2. 툴바의 Play Mode Scenario 목록에서 **Two Player Scenario**를 고른다 (메인 에디터 + 가상 플레이어 1명)
3. Play를 누른다. 가상 플레이어 창이 처음 뜰 때는 시간이 걸린다
4. 메인 에디터 Game 창 왼쪽 위의 **Host**를 누르고, 가상 플레이어 창에서 **Join**을 누른다
5. 통과 기준: 상태 문구가 호스트는 `Players 2/2 / Press START`, 가상 플레이어는 `Connected / Waiting for host to start`가 된다. 두 사람이 테이블을 사이에 두고 각자 의자 뒤에 서 있고, 상대의 머리(눈가리개가 달린 회색 상자)가 보인다
6. 메인 에디터에서 **Start Game**을 누른다. 양쪽 모두 `Game started`가 되고 퀴즈 화면에 **같은 문제**가 뜬다. 지금은 클라이언트가 단말기를 눌러도 반영되지 않는다
- 게임은 START를 눌러야 시작한다. 혼자 게임 흐름만 볼 때는 `GameFlow` 오브젝트의 `GameFlowController` → **Start On Play**를 켠다 (커밋하기 전에 다시 끈다)
- 헤드셋 없이는 손 위치가 움직이지 않는다. 손까지 보려면 Interaction Simulator를 켠다
- 왼쪽 위 버튼은 에디터와 개발 빌드에서만 뜬다 (`LanSession`의 `Show Debug Gui`로 끌 수 있다)

### 헤드셋 두 대로 (LAN)

1. 두 기기를 **같은 공유기(같은 서브넷)**에 연결한다
2. 한쪽이 방 오른쪽 앞의 패널에서 **HOST**를 레이로 가리키고 트리거, 다른 쪽이 **JOIN**. 둘이 붙으면 호스트 쪽 **START**가 밝아지고, 누르면 게임이 시작된다
3. JOIN은 호스트의 알림(UDP 47777)을 3초 동안 듣고 자동으로 붙는다. 처음 호스트를 열 때 Windows 방화벽이 물으면 **허용**한다 (게임 포트 7777, 알림 포트 47777)
- 알림을 못 들으면 `127.0.0.1`로 붙으려다 실패한다. 이때는 방화벽과 같은 네트워크인지부터 확인한다. 공유기가 브로드캐스트를 막는 경우도 있다

### 씬 구성을 다시 만들 때

- `BombDefuse > Set Up Room Network` 메뉴가 NetworkManager, 플레이어 프리팹, 좌석 목록, 접속 패널, Build Settings 등록을 한 번에 한다. 이미 있는 것은 건너뛴다

## 남은 정리 (급하지 않음)

- 빌드 이름(`productName`)과 Unity Cloud 프로젝트 이름이 `VR`로 되어 있다. 게임 이름이 정해지면 바꾼다
- 템플릿에만 필요한 요소(AR Foundation, Android XR 패키지, 에디터 튜토리얼, `Readme.asset`)는 쓰지 않으면 나중에 제거한다
