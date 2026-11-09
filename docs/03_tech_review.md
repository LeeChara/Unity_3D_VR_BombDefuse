# 03. 기술 검토

> 현재 프로젝트: **Unity 6.3 LTS (6000.3.9f1)**, URP, Input System. **VR Multiplayer 템플릿**으로 새로 만들었다.
> 팀 전원 같은 에디터 버전(6000.3.9f1)을 쓴다. 패키지 버전은 템플릿에 들어 있는 것을 기준으로 한다 ([06_vr_setup.md](06_vr_setup.md) 참고).

---

## 1. 기술 스택 (권장)

| 영역 | 선택 | 이유 |
|---|---|---|
| XR | OpenXR + **XR Interaction Toolkit (XRI) 3.x** | Unity 공식 표준. 잡기·버튼 누르기가 기본으로 제공된다 |
| 네트워크 | **Netcode for GameObjects (NGO)** | Unity 공식이고, 2인 소규모 게임에 충분하다. 문서와 예제가 많다 |
| 연결 | 발표용: **LAN 직접 연결** / 출시용: Unity Relay + Lobby | 발표 현장 와이파이를 믿지 말 것. 노트북 핫스팟이나 유선 공유기를 준비한다 |
| 보이스챗 | **Vivox** (Unity Gaming Services) | NGO와 연동 예제가 있다. 이 게임에서 보이스챗은 핵심 기능이다 |
| 출발점 | **Unity VR Multiplayer 템플릿** | XRI + NGO + Vivox가 이미 연결된 공식 템플릿. 아바타 동기화까지 들어 있어서 1주차 작업이 크게 줄어든다. **이 템플릿으로 프로젝트를 새로 만들었다** |
| 멀티 테스트 | **Multiplayer Play Mode** (Unity 6 패키지) + **XR Device Simulator** | 헤드셋 없이 에디터 하나에서 플레이어 2명을 띄울 수 있다. 헤드셋 테스트는 하루 1~2회로 제한해서 반복 속도를 지킨다 |
| 연출 | Timeline, URP Volume (후처리) | 진행자 연출, 시야 제한·출혈 효과 |
| 데이터 | CSV → ScriptableObject 변환 | 콘텐츠 담당이 유니티 없이 스프레드시트로 문제를 관리한다 |

### 타깃 기기

> **미정.** 장비: VR 구동 PC 1대, 성능 낮은 노트북 1대, Quest 헤드셋 2대.

- **개발·테스트**: VR PC + Quest Link.
- **발표**: 원래 PC VR을 권장했다. 공포 분위기에는 조명·그림자·후처리가 중요한데, Quest 단독 실행은 성능 여유가 적기 때문이다. 다만 PC VR로 두 사람이 동시에 하려면 VR이 돌아가는 PC가 2대 필요한데, 노트북으로는 어렵다. 그래서 Quest 단독(Android 빌드)도 후보다.
- 결정이 늦어질수록 조명과 셰이더를 다시 작업할 위험이 커진다. Quest 단독이 될 가능성이 있으면 Android 빌드를 일찍 한 번 해 보고 성능을 확인한다.

## 2. "멀티플레이가 어렵다"는 말의 의미

**장비 문제가 아니라 기술 문제다.** 장비가 있으면 테스트가 편해질 뿐, 어려움은 그대로다.

1. **두 개의 진실**: 같은 순간에 두 플레이어의 화면이 서로 다르다. "누가 먼저 눌렀나", "총알이 머리에 맞았나"를 **누가 결정하는지** 정해야 한다.
2. **지연(latency)**: 상대 머리는 항상 조금 과거의 위치로 보인다. 피하기 기능이 있는 이 게임에서는 이게 곧 공정성 문제가 된다.
3. **디버깅 비용**: 버그가 한쪽 화면에서만 생긴다. 재현하려면 2인 환경이 필요하다.
4. **설계가 처음부터 달라진다**: 싱글로 만든 뒤 멀티를 붙이면 거의 다시 짜야 한다. 그래서 **1주차부터 멀티 구조로 시작한다.**

> 지난번에는 "싱글 버전을 먼저 만들라"고 조언했는데, 멀티플레이 전담 인원과 장비가 있다면 이 권장을 바꾼다. **게임 로직은 네트워크와 분리된 순수 C# 클래스로 만들고**, 네트워크 계층이 그것을 감싸는 구조로 처음부터 간다. 그러면 로직은 싱글로 테스트할 수 있고, 멀티 구조도 유지된다.

## 3. 아키텍처

### 권한 모델: 호스트가 모든 결정을 한다 (Host-authoritative)

- 플레이어 1이 호스트(서버 + 클라이언트), 플레이어 2는 클라이언트다.
- 클라이언트는 **요청만** 보낸다. "2번 버튼 눌렀어요", "방아쇠 당겼어요(조준 방향 포함)".
- **판정, 무작위(약실 등), 상태 변경은 전부 호스트만** 한다.
- **약실 상태는 클라이언트에 보내지 않는다.** 결과(발사/불발)만 보낸다. 보내면 메모리를 들여다보는 것만으로 치트가 된다.

### 상태 머신

```
Lobby → Seating → Intro
  → QuizActive → QuizResolve
      ├→ GunHandover (대상 구속) → ShooterTurn → ShotResolve → HostReaction
      └→ BothFailPenalty
  → DeathCheck → (QuizActive | GameOver)
```

| 상태 | 하는 일 | 다음 |
|---|---|---|
| Lobby | 두 사람 접속 대기 | 접속 완료 → Seating |
| Seating | 각자 의자 뒤에서 등장, 착석 안내 → 경고 → 강제 착석 (규칙서 2절) | 둘 다 앉음 → Intro |
| Intro | 규칙 설명 세 문장 | 끝 → QuizActive |
| QuizActive | 문제 출제, 버튼 입력, 압박 대사 | 정답·둘 다 오답·시간 초과 → QuizResolve |
| QuizResolve | 정답 판정 | 정답자 있음 → GunHandover / 없음 → BothFailPenalty |
| GunHandover | 총 지급, 대상 구속 | → ShooterTurn |
| ShooterTurn | 상대 쏘기, 자신 쏘기(공포탄이면 이 상태에 머물며 한 번 더). 제한시간이 지나면 총 반납 | 상대 쏘기·자신 쏘기 실탄·시간 초과 → ShotResolve |
| ShotResolve | 공이치기 연출, 실탄/공포탄, 피해, 총알이 0발이면 재장전(넣는 수가 1발씩 늘어남) | → HostReaction |
| HostReaction | 결과 브리핑 (진행자 반응) | → DeathCheck |
| BothFailPenalty | 공포 단계 +1 | → DeathCheck |
| DeathCheck | 사망자 확인 | 있음 → GameOver / 없음 → QuizActive |
| GameOver | 승리·사망 연출 | → Lobby |

- **구현 순서:** BothFailPenalty는 나중에 만든다. 그 전까지는 QuizResolve에서 정답자가 없으면 바로 QuizActive(다음 문제)로 간다. 이 임시 경로는 BothFailPenalty를 만들 때 지운다.
- 상대가 접속을 끊으면 어느 상태에서든(Lobby 제외) GameOver로 간다.
- `GameStateMachine`(순수 C#, `Core/`)은 현재 상태와 허용된 전이만 관리한다. 타이머와 판정은 하지 않는다.
  - **허용되지 않은 전이**를 요청하면 `false`를 돌려주고 상태는 그대로 둔다. 예외를 던지지 않는다. 시간 초과와 정답이 거의 동시에 들어오는 것처럼 늦게 도착한 요청은 정상적으로 생기기 때문이다. 실패하면 안 되는 곳에서는 부른 쪽이 에러 로그를 남긴다.
  - 상태 변경 알림을 처리하는 도중에 또 전이를 요청하면, 지금 알림이 끝난 뒤에 **순서대로** 처리한다.
- 현재 상태는 `NetworkVariable<GameState>` 하나로 동기화한다. 각 시스템(UI, 사운드, 조명)은 이 값이 바뀌는 이벤트를 구독해서 반응한다.
- 이렇게 하면 사운드·연출 담당은 **상태 변경 이벤트에 반응하는 코드만** 작성하면 된다. 메인 로직을 건드릴 필요가 없다.
- 상태 안에서 일어나는 일(오답, 사격 결과 등)은 정보가 함께 오는 **게임 이벤트**로 보낸다. 전체 목록은 [07_event_list.md](07_event_list.md)에 있다.

### 코드 구조 (제안)

```
Assets/_Project/
  Scripts/
    Core/        GameStateMachine, QuizDeck, QuizRound, Revolver, DamageModel   ← 순수 C#, 네트워크 모름
    Net/         LanSession, LanDiscovery, NetworkPlayer, NetworkGameController, RPC 정의   ← NGO 의존
    Gameplay/    GameFlowController, QuizButton, QuizTerminal, Gun, Seat, SeatedPlayer, SeatRegistry, Hitbox, Damageable, PlayerBody   ← MonoBehaviour
    Presentation/ QuizBoard, QuizScreen, InteractionPrompt, GunEffects, HostCharacter, DreadLevelController, VoiceLinePlayer
    Data/        QuizQuestion, QuizCsvParser, VoiceLineData, TuningConfig (ScriptableObject)
    Editor/      RoomNetworkSetup (씬 구성 메뉴)
  Art/  Audio/  Prefabs/  Scenes/
```

### 네트워크 구성 (1주차)

템플릿의 네트워크 코드(`XRINetworkGameManager`, `SessionManager`, `XRINetworkPlayer`)는 **쓰지 않는다**.
- 템플릿은 세션을 **Distributed Authority**로 만든다. 각 클라이언트가 자기 오브젝트를 결정하는 방식이라 위의 호스트 권한 모델과 맞지 않는다.
- `XRINetworkPlayer`는 템플릿의 게임 매니저, Vivox 매니저, 이름표 UI를 전부 전제로 해서 따로 떼어 쓸 수 없다.

대신 `Net/`에 작은 LAN 전용 구성을 두었다. RoomScene에는 `BombDefuse > Set Up Room Network` 메뉴로 한 번에 넣는다(여러 번 실행해도 된다).

| 구성 | 하는 일 |
|---|---|
| `NetworkManager` | **Client-Server** 토폴로지, 접속 승인 켬, Player Prefab = `Prefabs/NetworkPlayer` |
| `LanSession` | 호스트 열기·접속·나가기. 호스트는 UDP 47777로 게임 포트(7777)를 1초마다 알린다. 접속하는 쪽은 3초 동안 알림을 듣고, 못 들으면 `127.0.0.1`로 붙는다. 정원 2명이 차면 다음 접속은 "Room is full"로 거절 |
| `NetworkPlayer` | 머리·양손 상자. 주인이 자기 XR 리그 위치를 복사하고, 자식 `NetworkTransform`(주인 권한)이 보낸다. 자기 화면에서는 숨긴다 |
| 좌석 배정 | 호스트가 접속 순서대로 빈 좌석 번호(`SeatRegistry`, 0 = 좌석 1)를 `NetworkVariable`로 준다. 주인은 그 의자 뒤로 옮겨지고 그 의자에만 앉을 수 있다 |
| `LanSessionPanel` | 시작 위치 오른쪽 앞의 임시 HOST / JOIN / LEAVE / START 버튼과 상태 문구. 지금 누를 수 없는 버튼은 글자가 흐리다. 에디터·개발 빌드에서는 화면 왼쪽 위에도 같은 버튼이 뜬다 |
| 퀴즈 동기화 | `LanQuizSync`가 호스트의 출제·퀴즈 종료·버튼 판정 색을 Named Message로 보낸다. 클라이언트는 받은 문제를 퀴즈 화면에 띄우고 남은 시간 줄을 자기 시계로 줄인다. 클라이언트가 자기 좌석 단말기를 누르면 답을 호스트에 보내고, 호스트가 받은 순간을 누른 시각으로 판정한다 |
| 총 동기화 | `LanGunSync`. 약실과 판정은 호스트의 `Gun`만 믿는다. 호스트가 테이블 가운데에 총을 꺼내거나 치우면 클라이언트도 똑같이 한다. 사수 기기의 총만 잡을 수 있고, 사수 기기가 총 위치를 초당 30번 보내 다른 기기의 총이 따라간다. 클라이언트 사수가 쏘면 레이(총구 위치·방향)만 보내고 호스트가 약실을 당겨 판정한다. 호스트는 모든 발사 결과를 클라이언트에 보내 이펙트를 보여 준다 |
| 좌석의 몸 | START 때 `LanGameStarter`가 마네킹을 숨기고, 진행 컨트롤러의 좌석 몸을 실제 플레이어로 바꾼다. 이 기기 사람은 XR Origin의 `PlayerBody`, 상대는 아바타(`NetworkPlayer` 프리팹)의 `AvatarBody`가 맞는다. 자기 아바타의 히트박스는 끈다 |
| 게임 시작 | 정원 2명이 차면 호스트의 START가 켜진다. 누르면 `LanSession`이 클라이언트에게 시작 메시지(Named Message `BombDefuse.StartGame`)를 보내고, 호스트의 `LanGameStarter`만 `GameFlowController.StartGame()`을 부른다. 게임 진행은 호스트에서만 돈다. RoomScene의 `GameFlowController`는 `Start On Play`를 꺼 둔다(혼자 테스트할 때는 켠다) |

- 보이스챗(Vivox)은 2주차에 붙인다. 템플릿의 `VoiceChatManager`를 그대로 쓸 수 있는지는 그때 정한다.
- 게임 진행은 호스트에서만 돈다. 클라이언트는 퀴즈 화면과 총을 호스트에 맞춰 보여 준다. NGO의 소유권 이전 대신 Named Message로 맞췄다(총을 껐다 켜는 방식이라 NetworkObject로 만들기 어렵다).
- 아직 맞추지 않은 것: 클라이언트 화면의 피해·사망·게임 종료 표시, 상대 손에 들린 총이 손을 따라 움직이는 모습(위치만 따라간다).
- 게임 중에 한 명이 나가도 진행은 멈추지 않는다. 다시 접속하면 START가 다시 켜지지만, 이미 시작한 쪽은 새로 시작하지 않는다.

- **asmdef와 단위 테스트는 지금 두지 않는다.** 스크립트는 전부 기본 묶음(Assembly-CSharp)으로 컴파일한다. "`Core`는 다른 폴더의 코드를 모른다"는 원칙은 코드 리뷰로 지킨다.
- 나중에 테스트가 필요해지면 asmdef 없이 `Assets/_Project/Tests/Editor/`에 두면 된다(NUnit이 자동 참조라 Test Runner에서 실행된다).
- `QuizRound`는 시각을 직접 읽지 않고 밖에서 받는다(`Submit(좌석, 선택지, 누른 시각)`, `Tick(현재 시각)`). 멀티에서는 누른 순간의 네트워크 시각을 넘긴다.
- `Gameplay/GameFlowController`가 `GameStateMachine`으로 한 판을 진행한다. 퀴즈 → 총 지급 → 사수 턴 → 사격 결과 → 결과 브리핑 → 사망 확인을 반복한다. 착석 단계·규칙 설명은 아직 바로 지나가고, 진행자 대사 대신 정해진 시간만 기다린다. 총은 이 컨트롤러가 `Gun.Present`로 꺼내고 `Gun.Withdraw`로 치운다.
- 자신 쏘기 판정: `Gun`은 실탄·공포탄 모두 레이캐스트해서 맞은 `Hitbox`를 알리고, 진행 컨트롤러가 그 몸이 사수의 몸(`PlayerBody`의 `Damageable`)인지로 구분한다. 레이는 총구보다 조금 뒤에서 쏴서, 총구를 머리에 댔을 때도 맞는다. 쏜 사람 리그의 히트박스가 아닌 콜라이더(이동용 캡슐 등)는 건너뛴다.
- `PlayerBody`는 XR Origin에 붙어 시작할 때 머리(카메라를 따라감)·몸통 히트박스를 만든다. 팔 히트박스와 아바타 연결은 멀티 아바타가 생기면 한다.
- `Gun`이 `Revolver`를 직접 들고 있다. 멀티에서는 호스트의 총만 약실을 쓰고, 클라이언트의 총은 `ResolvesShotsRemotely`로 판정을 호스트에 맡긴다(약실 상태는 호스트만 안다).
- `Presentation/GunEffects`(발사 궤적·총구 섬광·명중 표시)와 버튼 눌림·색 표시는 임시 이펙트다. 효과음이 오면 바꾼다.
- 총 모델은 에셋 스토어의 **Reichsrevolver M-1879**(`Assets/Reichsrevolver_M1879`, 쓰는 FBX·1024/256 텍스처만 저장소에 포함)다. `BombDefuse > Set Up Revolver Model` 메뉴가 `Prefabs/Pistol`의 임시 모양을 이 모델로 바꾸고, 패키지의 Standard 재질 대신 URP Lit 재질(`Art/Material/Revolver_*.mat`, 1024 텍스처)을 만든다. 패키지의 Animator와 데모 스크립트는 뺀다.
- `Presentation/RevolverModel`이 모델의 뼈를 직접 움직인다: 실린더 옆으로 열기(크레인 축으로 45도), 실린더 돌리기(반대 손으로 실린더 손잡이 `CylinderHandle`을 그립으로 잡고 실린더 축 둘레로 돌린 각도만큼. 방아쇠마다 60도. 모습만 바뀌고 약실은 그대로), 공이치기, 총알 수만큼 탄피 보이기. 판정과는 관계없는 모습이다. 탄피가 보이는 칸은 실제 약실 위치와 관계없다.
- 손목 스냅은 `Gun`이 총열 축 회전 속도로 잡는다(540도/초 이상이 두 프레임 이어지면). 왼쪽이면 열고 오른쪽이면 닫는다.
- 멀티: 사수 기기가 보내는 총 위치에 실린더 모습(열림, 돈 각도, 보이는 총알 수)을 함께 실어 상대 화면도 같은 모습을 보인다.
- `Gameplay/TestMannequin`은 상대 자리의 테스트용 마네킹이다. 혼자 테스트할 때만 쓰고, 멀티에서 START를 누르면 숨긴다.
- **수치 읽기**: `TuningConfig` 에셋은 하나만 두고, 게임 진행 컨트롤러가 Inspector로 연결해서 필요한 값만 각 로직에 넘긴다. `Core` 클래스는 설정 파일을 모르고 숫자만 받는다(예: `new Revolver(약실 수, 총알 수)`).
- ScriptableObject는 Play 중에 Inspector에서 바꾼 값이 멈춘 뒤에도 남는다. 값을 바꿨으면 규칙서 수치 표도 같이 고친다.

## 4. 어려운 문제와 해결 방법

| 문제 | 위험 | 해결 |
|---|---|---|
| **먼저 누르기 판정** | 호스트는 지연이 0이라 항상 유리하다 | 클라이언트가 누른 순간의 **네트워크 시간**(NGO `ServerTime`)을 함께 보낸다. 호스트는 그 시간으로 비교한다. 아주 근소한 차이의 처리는 테스트 후 정한다 ([확장 E10](02_rules_extended.md#e10-동시-판정-)) |
| **머리 피하기와 명중 판정** | 사수가 보는 상대 머리 위치가 과거 위치다 | 판정은 **호스트가 자기 시점의 대상 위치로** 한다. 공이치기 0.6초 지연으로 대상에게 반응할 시간을 준다. 발표 환경(LAN)에서는 지연이 작아서 체감 문제가 거의 없다 |
| **총 잡기 동기화** | 물리 오브젝트를 네트워크로 동기화하면 떨림, 소유권 충돌이 생긴다 | 총은 **사수만 잡을 수 있게** 하고 그때 소유권을 넘긴다. 던지거나 떨어뜨리면 일정 시간(규칙서 수치 표) 뒤 테이블로 돌아온다. 물리 던지기는 지원하지 않는다 |
| **구속 상태** | 플레이어가 의자를 떠나면 판정이 깨진다 | 착석 이후 게임이 끝날 때까지 **두 사람 모두 일어날 수 없게** 한다. 구속은 대상을 알리는 연출이고, 손 입력은 막지 않는다. 총은 사수만 잡을 수 있다. 머리는 HMD를 따라간다 |
| **아바타** | 전신 IK는 시간이 많이 든다 | 필수 버전: **머리 + 손 + 머리를 따라가는 몸통**(목 아래로 오프셋). 앉아 있는 게임이라 이 정도로 충분하다 |
| **부위 판정** | 히트박스가 아바타와 어긋난다 | 머리(구)·몸통(캡슐)·양팔(캡슐) 히트박스를 아바타 본에 붙인다. 디버그 모드에서 히트박스가 보이게 한다 |
| **멀미** | 죽거나 맞을 때 카메라를 강제로 움직이면 멀미가 난다 | **플레이어 카메라는 절대 강제로 움직이지 않는다.** 사망은 페이드아웃 + 사운드로 연출한다. 화면 흔들림 대신 비네팅과 색수차를 쓴다 |
| **진행자 목소리** | 실시간 변조는 튜닝이 어렵다 | 녹음 후 **DAW에서 미리 변조**해서 파일로 넣는다. 일관성이 생기고 런타임 비용도 없다 |
| **성능** | VR은 72~90fps를 유지해야 한다. 공포 조명은 실시간 그림자 때문에 무겁다 | 정적 조명은 베이크하고, 실시간 라이트는 2~3개로 제한한다. 깜빡임은 라이트 강도만 바꾼다. 매주 프로파일러로 확인한다 |

## 5. 협업 기술 규칙

- **씬 충돌 방지**: Unity 씬 파일은 병합이 거의 불가능하다. 메인 씬은 메인 개발자만 수정하고, 다른 사람은 **프리팹이나 개인 테스트 씬**에서 작업한다.
- **UnityYAMLMerge**: `.gitattributes`에 설정은 되어 있지만, **각자 로컬 git config에 merge 드라이버를 등록해야** 동작한다. 1주차에 전원 설정한다.
- **Git LFS**: `.gitattributes`에 모델·오디오·이미지가 LFS로 설정되어 있다. 전원 `git lfs install`을 해야 한다. 안 하면 파일이 깨진다.
- **브랜치**: 기능별 브랜치 → main 병합. main은 항상 빌드가 되는 상태를 유지한다.
- **모델 규격**: 1 unit = 1m, Y-up, 피벗 위치 사전 합의 (총은 손잡이, 버튼은 바닥면).

## 6. 위험 요소 순위

| 순위 | 위험 | 대응 |
|---|---|---|
| 1 | 네트워크 통합이 3주차에 터진다 | 1주차에 "두 명이 접속해서 서로 머리와 손이 보이는" 상태를 반드시 만든다 |
| 2 | 폴리싱 기간에 확장 기능을 너무 많이 넣는다 | 5주차 말에 **기능 동결**. 6주차는 버그 수정과 발표 준비만 한다 |
| 3 | 에셋이 늦게 들어온다 | 1주차부터 **회색 박스(임시 도형)** 로 개발한다. 에셋은 나오는 대로 교체한다 |
| 4 | 발표 현장 네트워크 | LAN과 백업 빌드, 시연 영상까지 준비한다 |
| 5 | 보이스챗 품질·에코 | 2주차에 Vivox를 붙여서 일찍 테스트한다 |

## 7. Claude 활용 팁

- 이 `docs/` 폴더를 Claude에게 먼저 읽게 하면 규칙과 구조를 알고 시작한다.
- 요청은 **작게, 구체적으로** 한다. "게임 만들어줘"가 아니라 "`Revolver` 클래스: 6칸 약실, 방아쇠 당기면 한 칸 진행, 실탄 여부 반환".
- Claude가 만든 코드는 **반드시 직접 읽고 이해한 뒤** 병합한다. 발표에서 질문받았을 때 설명할 수 있어야 한다.
