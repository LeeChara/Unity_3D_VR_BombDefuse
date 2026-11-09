# 변경 기록

## 멀티플레이 작업

> GitHub의 `feature/vr-multiplayer-base` (마지막 커밋 `667cbdc`) 이후 로컬에서 한 작업을 정리한 문서입니다.

---

## 1. 한눈에

| 구분 | 처음 상태 (GitHub) | 지금 상태 (로컬) |
|---|---|---|
| 네트워크 | 없음. 혼자 플레이만 가능, 상대 자리는 마네킹 | LAN 2인 접속, 좌석 배정, 아바타 동기화, 호스트 진행 |
| 게임 시작 | Play하면 바로 시작 | 2명이 붙으면 호스트가 START를 눌러 함께 시작 |
| 퀴즈 | 각자 화면에서 따로 진행 | 호스트가 출제·판정, 클라이언트는 같은 문제를 보고 자기 단말기로 답함 |
| 총 | 회색 상자. 사수 앞에 나옴 | Reichsrevolver M-1879 모델. 테이블 가운데에 나오고 사수만 잡을 수 있음. 위치·실린더 모습 동기화 |
| 실린더 | 총알을 집으면 열림, 흔들거나 반대 손을 대면 닫힘 | 손목 스냅으로 45도 열고 닫기, 반대 손으로 잡아서 돌리기 (연출) |
| 여분 총알 / 넣고 돌리기 | 있음 (사수 턴 3번째 행동) | **삭제**. 총알은 진행자만 넣고 사수는 쏘는 방법만 고름 |
| 에디터 테스트 | 시뮬레이터 설정을 켜고 끄며 수동 전환 | 메인 에디터는 헤드셋, Player 2는 키보드·마우스로 자동 분리 |

- 모두 로컬에만 있고 **푸시하지 않았습니다**.
- 병합 전 상태는 `backup/before-merge-lead` 브랜치에 남겨 두었습니다.

---

## 2. 추가·변경한 것

### 2-1. 네트워크 (`Scripts/Net/`, 전부 새 파일)

| 파일 | 하는 일 |
|---|---|
| `LanSession` | HOST/JOIN/LEAVE/START. 호스트가 UDP 47777로 게임 포트(7777)를 알리고, JOIN은 3초 듣다가 붙는다(못 들으면 127.0.0.1). 정원 2명. START는 2명이 찼을 때 호스트만 누를 수 있고 Named Message로 양쪽에 알린다 |
| `LanDiscovery` | 위 알림을 보내고 받는 UDP 소켓 |
| `NetworkPlayer` | 플레이어 프리팹. 머리·양손을 주인 권한 NetworkTransform으로 보낸다. 호스트가 좌석 번호를 NetworkVariable로 준다. 좌석별 몸(`BodyAtSeat`) 조회 |
| `LanGameStarter` | START를 받으면 마네킹을 숨기고, 좌석 몸을 실제 플레이어로 바꾼 뒤 호스트만 `GameFlowController.StartGame()` |
| `LanQuizSync` | 호스트 → 클라이언트: 문제·제한시간, 퀴즈 종료, 버튼 판정 색. 클라이언트 → 호스트: 자기 좌석 단말기 답 |
| `LanGunSync` | 총 지급·치우기, 사수 기기만 잡기, 총 위치와 실린더 모습(열림·각도·총알 수) 전송, 클라이언트 사격은 레이만 보내 호스트 약실로 판정, 발사 결과를 클라이언트 이펙트로 |
| `LanSessionButton`, `LanSessionStatusText` | 방 오른쪽 앞 패널의 HOST/JOIN/LEAVE/START 버튼과 상태 글자 |
| `VirtualPlayerInput` | **에디터 전용.** Player 2 창은 XR을 끄고 시뮬레이터로, 메인 에디터는 헤드셋이 있으면 시뮬레이터를 만들지 않는다 |

- 템플릿의 네트워크 코드(`XRINetworkGameManager` 등)는 쓰지 않습니다. 호스트 권한 모델과 맞지 않는 Distributed Authority를 쓰기 때문입니다(03 문서 참고).
- 동기화는 전부 Named Message입니다. 상태가 더 늘면 `NetworkVariable`로 옮기는 편이 낫습니다.

### 2-2. 게임플레이 (`Scripts/Gameplay/`)

| 파일 | 변경 |
|---|---|
| `GameFlowController` (원래 파일) | `Start On Play` 옵션(RoomScene은 꺼 둠), `StartGame()`, `IsLocalSeat`, `SubmitAnswer`, `SetBody`, `PresentGunForSeat`/`WithdrawGun`, 퀴즈·총 이벤트 추가. 총 지급 위치를 좌석별 2곳 → 테이블 가운데 1곳. 여분 총알·넣고 돌리기 제거 |
| `Gun` (원래 파일) | 멀티용: `SetGrabbable`, `FollowRemotePose`, `ResolvesShotsRemotely`/`ShotRequested`, `FireRemote`, `ShowRemoteShot`. 실린더: 손목 스냅 열기·닫기, `CylinderHandle`을 반대 손으로 잡고 돌리기, `RevolverModel` 연동. 집기용 콜라이더와 "Press Grip to pick up" 안내. 총알 넣기 제거, 놓을 때 던지기 끔 |
| `AvatarBody` (New) | 상대 아바타의 머리·몸통 히트박스. 자기 아바타에서는 끔 |
| `SeatRegistry` (New), `Seat`, `SeatedPlayer` | 좌석 목록, 배정된 의자에만 앉기, 의자 뒤로 옮기기 |
| `HoverOutline` | SkinnedMeshRenderer(총 모델)에도 외곽선 |
| `SpareBullet`, `GunLoadZone` | **삭제** |

### 2-3. 표현 (`Scripts/Presentation/`)

- `RevolverModel` (New): 총 모델 뼈를 직접 움직입니다. 실린더 45도 열기, 돌리기(잡아서/방아쇠마다 60도), 공이치기, 총알 수만큼 탄피 표시. 모두 연출이며 약실(총알 위치)은 바뀌지 않습니다.
- `InteractionPrompt`: `IsOwnedByOther` 추가 (총 안내가 다른 안내를 덮지 않게).

### 2-4. 에디터 메뉴 (`Scripts/Editor/`, New)

- `BombDefuse > Set Up Room Network`: NetworkManager, 플레이어 프리팹(+AvatarBody), 좌석 목록, 접속 패널(+START), GameFlow에 Lan* 컴포넌트, Build Settings. 여러 번 실행해도 됩니다.
- `BombDefuse > Set Up Revolver Model`: `Prefabs/Pistol`의 상자를 리볼버 모델로 바꾸고 URP 재질(`Art/Material/Revolver_*.mat`)을 만듭니다. 콜라이더·집기 범위·실린더 손잡이·총구 위치 설정.

### 2-5. 에셋·씬

- `Prefabs/NetworkPlayer`, `Art/Material/Avatar.mat`, `Revolver_M1879.mat`, `Revolver_Shell.mat` 추가. `Prefabs/Pistol` 모델 교체. `SpareBullet.prefab`, `Bullet.mat` 삭제.
- `RoomScene`: NetworkManager, SeatRegistry, LanSessionPanel, GunSpawn_Center 추가. GunSpawn_1/2 제거. `TuningConfig.SpareBullets` 제거.
- `Assets/Reichsrevolver_M1879/`: 에셋 스토어 패키지에서 **쓰는 것만**(모델 FBX 2개, 1024·256 텍스처 10장, 약 16MB) 남기고 커밋했습니다. 2048 텍스처, 데모 씬, 애니메이션, 원본 재질·프리팹·스크립트는 지웠습니다(필요하면 에셋 스토어에서 다시 받습니다). 비공개 저장소라 팀 공유용으로 넣었습니다.

---

## 3. 확인한 것 / 못 한 것

| 항목 | 상태 |
|---|---|
| 에디터 2인(호스트+클라이언트) 접속, 좌석, START, 같은 문제 표시 | 확인 |
| 클라이언트 답 → 호스트 판정 → 클라이언트에 총 지급·잡기·위치 동기화 | 확인 |
| 클라이언트 사격(상대 머리, 자기 머리) 호스트 판정, 사망·재시작 | 확인 |
| 헤드셋으로 머리 추적·컨트롤러 | 확인 (시뮬레이터 충돌 문제 해결 후) |
| 헤드셋으로 총 집기, 실린더 잡아 돌리기, 스냅 세기 | 확인 |
| 호스트가 사수일 때 클라이언트 화면 | 확인 |
| 헤드셋 두 대 LAN | **미확인** |

---

## 4. 확인할 것 (푸시 전)

1. **원래 파일 수정:** `GameFlowController.cs`, `Gun.cs`에 멀티용 코드를 많이 넣었습니다.
2. **`Start On Play`:** RoomScene에서는 꺼져 있습니다. 혼자 테스트할 때는 GameFlow 오브젝트에서 켜고, 커밋 전에 끕니다.

---

## 5. 아직 없는 것

- 클라이언트 화면의 피해·사망·게임 종료 표시 (판정은 호스트에서 정상).
- 게임 중 한 명이 나갔을 때 처리 (진행이 멈추지 않습니다).
- 착석 단계·규칙 설명, 양측 패널티, 진행자 대사, 공포 단계, 보이스챗 (처음부터 없었음).
- 탄피 표시 결정: 총알 개수만큼 보이지만 칸 위치는 실제와 무관합니다. 헷갈리면 숨깁니다.
- 콘솔 정리: OpenXR 설정의 Meta 기능 24개(스크립트 없음) 경고, 오디오 드라이버 경고. 해는 없습니다.
