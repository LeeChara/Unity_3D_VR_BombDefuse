# 변경 기록

## 문제 종류(순발력 · 사고력 · 괴상) · 3단계 벌 없음

> 아래 미커밋 작업들 위에서 한 작업입니다.

### 정한 것
- 문제는 **순발력**(읽자마자 답이 보임)과 **사고력**(지식 없이 고민하면 풀림) 두 종류로 나눈다. 공포 3단계에서만 **괴상한 문제**를 가끔 낸다.
- 누구나 아는 상식이 아닌 지식 문제는 출제 보류한다.
- 사고력 문제는 순발력 문제보다 제한시간이 길다.
- 공포 3단계에서 아무도 못 맞히면 벌 대사("자, 벌을 내릴 시간이다.")와 단계 상승 이벤트가 나오지 않는다.

### 추가·변경한 것

| 파일 | 변경 |
|---|---|
| `Data/TuningConfig` | CSV `difficulty`를 문제 종류로 읽는다 (1 = 순발력, 2 = 사고력, 3 = 괴상한 문제). 사고력·괴상 제한시간 `Thinking Time Limits` 22/20/18/16초 추가 (순발력은 기존 15/12/10/7초). 종류 비율을 70/30/0, 50/50/0, 40/60/0, 35/45/20%로 |
| `Gameplay/GameFlowController` | 제한시간을 문제 종류로 고른다. 공포 단계가 최대면 모두 오답·시간 초과 대사 뒤 벌 대사와 `dread_changed` 없이 다음 문제로 (상태는 BothFailPenalty를 그대로 지나감) |
| `Data/Quiz/questions.csv` | 800문제를 새 종류로 나눔 → 537문제 (순발력 407: 순발력 분야 전체와 누구나 아는 상식 / 사고력 130: 넌센스·함정·논리·추리, q317, q672). 괴상한 문제는 아직 없음 |
| `Data/Quiz/questions_hold.csv` (New) | 출제 보류 263문제: 누구나 아는 상식이 아닌 지식 문제(수도, 연도, 화가, 영단어, 교과 과학 등)와 어색하거나 공식이 필요한 문제(q170, q490, q535, q564, q658). 게임은 읽지 않는다. `difficulty`는 예전 값 그대로 |
| 문서 | 01 퀴즈 규칙·7절 표·수치 표, 03 양측 패널티, 04 `difficulty` 뜻·출제 보류 기준·보류 파일, 07 `penalty_announce` |

### 확인한 것 / 못 한 것

| 항목 | 상태 |
|---|---|
| 에디터 혼자 Play: 공포 3단계에서 시간 초과 → "답을 고르지 못했군." 뒤 벌 대사·`dread_changed` 없이 다음 문제. 제한시간이 난이도 1은 7초, 난이도 3은 16초 | 확인 |
| 게임 파서로 두 CSV 읽기: 537 / 263문제 모두 읽힘, 경고는 글자 수 권장 초과뿐 | 확인 |
| 상식/보류 판정은 한 사람(Claude) 기준이라 팀 확인 필요 | 못 함 |

## 귀 먹먹함 · 진행자 목소리 효과 창

> 아래 "테스트 피드백 반영"과 같은 미커밋 작업 위에서 한 작업입니다.

### 추가·변경한 것

| 파일 | 변경 |
|---|---|
| `Presentation/ScreenEffects` | 실눈이 더 서서히 (잡을 때 2초, 놓을 때 0.8초). 실눈과 같은 속도로 AudioListener에 저역 필터를 걸어 귀가 먹먹해진다 (가장 먹먹할 때 700Hz). 값은 인스펙터 칸 |
| `Data/HostVoiceSettings` (New), `Data/HostVoiceSettings.asset` (New) | 진행자 목소리 효과: 높이, 저역·고역, 찌그러짐, 울렁임, 메아리, 잔향. 처음 값은 원래 목소리 그대로 |
| `Editor/HostVoiceWindow` (New) | 메뉴 BombDefuse > Host Voice. 값 고치기, 설정 만들기·씬 연결, 미리 듣기(대사 고르기, 원래 목소리·효과 비교, 바뀐 길이 표시). Play 없이 들을 수 있고, 들으면서 고치면 바로 바뀐다 |
| `Presentation/HostDialogue` | 목소리 재생기에 효과를 건다 (에디터 Play 중에는 매 프레임 다시 걸어 창에서 고친 값을 바로 들음). 높이를 낮추면 느려진 녹음 길이로 대사 대기 시간을 잡는다 |
| `RoomScene` | HostDialogue에 목소리 설정 연결, ScreenEffects 실눈·먹먹함 칸 |

### 확인한 것 / 못 한 것

| 항목 | 상태 |
|---|---|
| 컴파일, 설정 만들기·씬 연결, Play 없이 미리 듣기 재생 | 확인 |
| Play: 총을 쥐면 2초에 걸쳐 실눈과 먹먹함 700Hz, 놓으면 돌아옴. 목소리 높이를 Play 중에 바꾸면 바로 바뀜 | 확인 |
| 귀로 듣는 효과의 느낌 (필터 소리는 직접 들어 봐야 함), 헤드셋, 보이스챗도 먹먹해지는지 | 못 함 |

## 테스트 피드백 반영 (오답 소리 · 벽 소리 자리 · 사격 실눈)

### 추가·변경한 것

| 파일 | 변경 |
|---|---|
| `Audio/SFX/Temp/answer_wrong.wav` | 받은 오답 소리(`SFX/answer_wrong.wav`)를 지우고 예전 임시 삐- 소리를 되살림. `SoundBank`의 오답 칸도 임시 소리로 |
| `Core/GameEventCatalog`, `Presentation/DreadAtmosphere` | 새 이벤트 `walls_closing`: 공포 단계가 올라 암전이 끝나고 벽이 다가오기 시작할 때 기기마다 직접 낸다 (보내지 않음, `Count` = 새 단계). 소리 파일은 아직 없음 |
| `Presentation/ScreenEffects` | 사격 실눈: 총을 손에 쥔 순간(쥔 기기에서만) 시야 위아래에서 검은 그라데이션 덮개가 서서히 내려와 가운데(위아래 9°)만 맑게 남고, 놓거나 떨어뜨리거나 총이 치워지면 다시 뜬다 (시간은 위 "효과 조정 창"의 설정). 총은 지급 전에 꺼져 있어 꺼진 것까지 찾는다 |
| 문서 | 05 피드백 항목, 07 `walls_closing`, 08 오답 `임시`·벽 소리 `필요` |

### 확인한 것 / 못 한 것

| 항목 | 상태 |
|---|---|
| 컴파일, 에디터 Play: 단계를 올리면 암전 뒤 `walls_closing count=1`, 총을 쥐면 실눈 덮개가 내려오고 놓으면 걷힘 (덮개 바깥 밝기 0, 가운데 그대로) | 확인 |
| 헤드셋에서 실눈 느낌·세기, 실제 사격 흐름, 2인 (상대 기기에는 안 나오는지) | 못 함 |

## 진행자 녹음 · 효과음 연결

> 방 축소 강화와 같은 미커밋 작업 위에서 한 작업입니다.

### 추가·변경한 것

| 파일 | 변경 |
|---|---|
| `Audio/Voice` | 사운드 담당이 대사 문장 이름으로 다시 준 녹음 77개(기존 A1~A77과 내용이 같음)를 대사 id 이름으로 옮기고 A1~A77을 지움. 총알 수별 36개(`gh01`·`gh02`·`cs01`·`cs02`·`rl01`·`rl02` `_1`~`_6`), 문제 순서별 10개(`qi01_1`~`_10`), 나머지 31개 |
| `Presentation/HostDialogue`, `Gameplay/GameFlowController` | `{order}` 대사도 순서별 녹음을 고른다 (`Say(..., orderNumber)`. "N번째 문제다"가 `qi01_N` 녹음). 멀티는 기존 숫자 칸으로 그대로 전달 |
| `Data/VoiceBank`, `Editor/SoundHookupWindow` | 숫자별 녹음 설명에 순서별 추가. 연결 창에 "순서별 N개" 표시, 녹음 목록 내보내기에 `qi01_1`~`_10` |
| `Data/Dialogue/host_lines.csv` | `gh02`를 녹음대로 "들어 있는 건 {bullets}발."로. `sbo03` "아무 일도 없었다." 추가 (받은 녹음에 있음) |
| `Audio/SFX` | 받은 효과음 16개를 이벤트 이름으로: 앞뒤 무음 자름, 모노, 앉는 소리 +13dB·실린더 회전 +4dB. 같은 이름의 `SFX/Temp` 임시 소리 14개 지움. `Reload1`과 `리볼버 장전`은 같은 파일이라 하나만 씀 |
| `Audio/QuizTick.wav` (New) | 받은 `Tick`(3초에 째깍 3번)에서 첫 째깍만 잘라 퀴즈 화면 째깍 소리로 |
| `SoundBank`, `VoiceBank`, `RoomScene`(QuizBoard, Pistol의 GunEffects) | 자동 연결 결과 |
| `Audio/효과음 wav` | 받은 원본 그대로 둠 |
| 문서 | 05 녹음·효과음 체크, 08 효과음 상태·진행자 목소리, `Audio/Voice/README` 순서별 녹음 |

### 확인한 것 / 못 한 것

| 항목 | 상태 |
|---|---|
| 연결: 녹음 77개, 효과음 20개(새 14 + 남은 임시 6), 총 소리 2개 | 확인 |
| 에디터 Play: "세 번째 문제다"→`qi01_3`, "열 번째"→`qi01_10`, 총알 4발→`gh02_4`, 6발→`cs01_6`, `sbo03` 무작위 선택, 효과음 이벤트 재생 시 오류 없음 | 확인 |
| 실제로 들어 본 음량·타이밍 (특히 공이치기, 버튼), 헤드셋, 2인 | 못 함 |
| 아직 없는 소리: `su02` "쏴라.", 총 잡기, 실린더 열림, 총알 끼우기, 피격, 승리, 배경음류 | 사운드 담당 요청 필요 |

## 방 축소 강화 (소품 무너짐)

> `main` (`82a3d5e`) 위에서 한 작업입니다.

### 추가·변경한 것

| 파일 | 변경 |
|---|---|
| `Presentation/RoomProps` (New) | 방 소품 묶음. 공포 단계가 오를 때 소품을 조금씩 흔들고, 새 게임·로비에서 처음 자리로 돌린다. 벽이 판판하게 밀면 키 큰 소품은 미끄러지기만 해서, 다가오는 벽에 닿은 키 큰 소품·위에 올린 소품은 윗부분을 방 쪽으로 밀어 넘어뜨린다(기울기 15° 넘으면 멈춤). 쓰러진 키 큰 소품은 회전 감쇠를 높여 더 구르지 않게 한다. 물리는 기기마다 따로 계산하고 플레이어 몸과는 부딪히지 않는다 |
| `Presentation/DreadAtmosphere` | 벽에 Rigidbody(Is Kinematic)가 있으면 물리로 옮겨 소품을 밀어낸다. 새 게임에서는 바로 제자리 |
| `Gameplay/Gun` | 총알 판정이 방 소품을 지나간다 (기기마다 소품 위치가 달라 결과가 어긋나지 않게) |
| `Assets/school` (New, 받은 패키지) | 교실 소품 모델. 재질 2개(`1`, `floor_color`)를 Standard → URP Lit으로 바꿈 |
| `RoomScene` | 벽 4개에 Rigidbody(Is Kinematic). `RoomProps` 아래 교실 소품 47개: 북·남 벽에 큰 사물함 2, 책장(책을 눕혀 쌓음), 2단 캐비닛과 모니터·본체·스피커. 동쪽 벽에 책상(모니터, 노트북, 거꾸로 올린 의자), 의자 2, 소화전함. 서쪽 벽(진행자 뒤)에 2단 캐비닛 2, 의자. 크기는 0.75배(책장 0.5배, 책 0.55배). 프리팹 재질 칸이 비어 있어 인스턴스에 `1` 재질을 넣고, Static을 끄고(켜져 있으면 Play에서 겉모습이 안 움직임), 원래 콜라이더 대신 상자 콜라이더(책장은 칸 판·기둥별)를 붙임 |
| 문서 | 05 5주차 방 축소 강화, 모델링 소품, 사운드 무너지는 소리 |

### 확인한 것 / 못 한 것

| 항목 | 상태 |
|---|---|
| 에디터 Play 혼자 (메인 메뉴를 게임 중처럼 숨기고): 시작할 때 큰 소품은 가만히 있음(선반 위 책만 1초 안에 몇 cm 자리 잡음). 단계 1에서 사물함·책장·윗단 캐비닛이 앞으로 쓰러지고, 2·3에서 벽에 밀려 쌓임. 날아가거나 바닥을 뚫는 소품 없음 | 확인 |
| 단계 3에서 플레이어 자리·진행자 자리·테이블 위 침범: 바닥 높이(0.5m 아래)의 쓰러진 책장·캐비닛이 의자 뒤·옆까지 옴. 머리·몸 높이 침범 없음 | 확인 |
| 단계 0으로 되돌리면 벽과 소품이 모두 제자리 | 확인 |
| 로비에서 메인 메뉴가 떠 있으면 메뉴 버튼 콜라이더에 남쪽 사물함이 걸린다. 게임 중에는 메뉴가 숨으므로 문제없음 | 확인 |
| 헤드셋에서 보이는 모습, 2인(양쪽 화면), Quest 프레임 | 못 함 |

## 메인 메뉴 · 팔 절단

> `main` (`4774de1`) 위에서 한 작업입니다.

### 추가·변경한 것

| 파일 | 변경 |
|---|---|
| `Net/MainMenu` (New) | 접속 패널을 메인 메뉴로. 로비에서만 보이고 게임 중에는 숨는다. 음량(게임 소리 전체, 10% 단위, 기기에 저장), 게임 종료 |
| `Net/LanSessionButton` | 명령 추가: 음량 -, 음량 +, 게임 종료. 마이크 글자·안내를 한글로 |
| `Net/LanSession` | 상태 문구를 한글로. `ResetForNextGame()`: 한 판이 끝나 로비로 오면 접속은 둔 채 다시 시작할 수 있게 한다 |
| `Net/LanVoiceChat` | 보이스챗 상태 문구를 한글로 (`보이스챗: 켜짐` 등) |
| `Net/LanGameStarter` | 멀티에서는 자동 재시작을 끄고, 게임이 끝나 로비로 오면 `ResetForNextGame` |
| `Gameplay/GameFlowController` | `AutoRestart`: 끄면 게임이 끝난 뒤 바로 새 판을 시작하지 않고 로비에서 기다린다 |
| `Gameplay/ArmHitboxes` | 늘어진 팔 대신 절단: 어깨에서 짧게 남고(`StumpLength`), 끝에 절단면(붉은 구)과 핏방울 |
| `Presentation/BloodDrip` (New) | 절단면에서 피가 튀고 떨어지는 핏방울 (작은 구를 돌려 씀) |
| `Gameplay/PlayerBody`, `AvatarBody` | 절단면을 만든다. 상대 아바타도 팔을 맞으면 절단 모습이 되고 그쪽 손이 사라진다. 새 게임에서 되돌린다 |
| `Editor/RoomNetworkSetup` | `SetUpMainMenu`: 패널을 메뉴 배치로 바꾸고(타이틀, 한글 버튼, 음량, 게임 종료) `MainMenu`를 붙인다 |
| `RoomScene` | 위 메뉴 구성 반영 |
| 문서 | 01 6절 팔(절단), 03 메인 메뉴·한 판 뒤 로비, 05 5주차 체크, 06 2인 테스트 문구 |

### 확인한 것 / 못 한 것

| 항목 | 상태 |
|---|---|
| 에디터 Play: 메뉴가 로비에서 한글로 뜸, 게임 중 숨김, 사망 → GameOver → 로비로 돌아와 메뉴 다시 표시, 자동 재시작 안 함 | 확인 (혼자, 마네킹 머리 사격) |
| 팔 절단: 내 몸 절단면 표시, 상대 아바타는 손 사라짐 + 절단면 + 핏방울 | 확인 (에디터, 아바타 프리팹을 꺼내 직접 맞힘) |
| 2인: 한 판 뒤 클라이언트도 메뉴로 돌아오고, 호스트가 시작을 다시 누르면 새 판 | 확인 |
| 헤드셋에서 메뉴 버튼 누르기, 음량, 게임 종료, 절단 모습(상대 화면 포함) | 확인 |
| 한 판을 처음부터 끝까지 멈춤 없이 | 확인 |

---

## 5주차 시작 정리

> `main` (`67bb3c1`) 위에서 한 작업입니다.

### 추가·변경한 것

| 파일 | 변경 |
|---|---|
| `RoomScene` | 공포 단계 0~3의 배경음(`DreadAtmosphere` → Levels의 `Music`)을 비움. 임시 배경음이 계속 들리는 노이즈처럼 들려서 뺐다. 사격 배경음·심장박동은 그대로 |
| `Audio/Voice/A1~A77.wav` (New) | 사운드 담당의 진행자 녹음 77개. A번호와 대사의 대응 목록이 없어 **아직 연결하지 않았다** (A3는 `3.wav`로 들어 있음). 들어 보고 대사 id 이름으로 바꾼 뒤 연결한다 |
| `ProjectSettings/EditorBuildSettings` | 빌드에서 템플릿 예제 `SampleScene`을 뺌. 빌드에는 `RoomScene`만 들어간다 |
| `ProjectSettings/UnityConnectSettings` | Unity 서비스 연결 켜짐 (Android 빌드 때) |
| 문서 | 05: Quest 2대 테스트 결과 체크, 5주차를 메인 메뉴 + 팔 절단으로 변경, 문제 세트 교체 추가. 04: 문제 방향, 문제 수. 02: E1 팔 절단을 5주차에 넣는다는 메모. 06·README: 이전 정리(게임 이름 등). 08: 공포 단계 배경음 상태 |

### 확인한 것

| 항목 | 상태 |
|---|---|
| Quest 3·3S APK로 2인: 접속·착석, 퀴즈·총·사격 결과 동기화, 보이스챗 양방향·에코, 끊김 | 확인 (문제 없음) |
| 에디터에서 Player 2 창이 앉은 뒤에도 움직임 | 키보드 시뮬레이터가 가짜 헤드셋을 옮기는 것. 테스트 창에서만 생기고 게임 버그는 아님 |

---

## 보이스챗 작업

> `feature/core-loop` (조장 커밋 `583e50c`) 위에서 한 작업입니다.

### 추가·변경한 것

| 파일 | 변경 |
|---|---|
| `Net/LanVoiceChat` (New) | Vivox 보이스챗. Unity 서비스 초기화·익명 로그인·Vivox 초기화 → LAN 접속 시 로그인·그룹 채널 `bombdefuse-room` 참가 → 나가면 퇴장. 실패해도 게임은 진행. Android 마이크 권한 요청. 에디터 가상 플레이어는 다른 인증 프로필 사용. 참가자 목소리 크기(AudioEnergy) 제공 |
| `Presentation/VoiceIndicator`, `VoiceIndicatorController` (New) | 상대가 말하는 동안 상대 머리 위에 선으로 그린 스피커 아이콘. 소리 줄은 목소리 크기에 따라 0·1·2개. 내 표시는 없음 |
| `Net/LanSessionPanelFollower` (New) | 접속 패널을 이 기기 플레이어 옆, 진행자(`Host`) 반대쪽에 두고 좌석 배정 때 따라 옮김. 방향은 카메라(머리) 기준 |
| `Net/LanSession` | 게임 포트가 막혀 있으면 7778~7786을 차례로 시도. 알림에 실제 포트를 실음 |
| `Net/LanSessionButton` | `ToggleMute` 명령 추가. 글자가 `MIC OFF`/`MIC ON`으로 바뀜 (채널에 있을 때만 활성) |
| `Net/LanSessionStatusText` | 보이스 상태를 둘째 줄에 표시 |
| `Net/NetworkPlayer` | `Head` 접근자 (머리 위 표시용) |
| `Editor/RoomNetworkSetup` | NetworkManager에 `LanVoiceChat`·`VoiceIndicatorController`, 패널에 MIC 버튼과 `LanSessionPanelFollower`(Host 연결) 추가 |
| `RoomScene` | 위 구성 반영 |
| 문서 | CHANGELOG만. 03·05·06은 원래 파일이라 손대지 않았습니다. 보이스챗 설정·확인 절차는 아래 "보이스챗 확인 방법"에 적었습니다 |

### 확인한 것 / 못 한 것

| 항목 | 상태 |
|---|---|
| 에디터 2인: 양쪽 서비스 초기화·익명 로그인(서로 다른 계정), 채널 참가, 상대 참가 로그, 음소거, 나가기 | 확인 |
| 스피커 아이콘: 목소리 크기 0 / 0.15 / 0.6에서 줄 0 / 1 / 2개, 말이 끝나면 사라짐 | 확인 (목소리 크기를 흉내 내서) |
| 접속 패널: 호스트·접속자 모두 자기 옆 진행자 반대쪽, 호스트가 고개를 돌린 채 열어도 같음 | 확인 |
| 7777이 막힌 상태에서 7778로 호스트 열고 접속 | 확인 |
| 기기 두 대 양방향 통화, 에코 | 확인 (5주차, Quest 3·3S APK) |
| 실제 목소리로 스피커 줄이 움직이는지 | **미확인** |
| 인터넷 없을 때 게임이 그대로 진행되는지 | **미확인** (코드상 예외는 전부 잡음) |

### 보이스챗 확인 방법

1. 에디터에 로그인한 Unity 계정이 Unity Cloud 조직의 멤버여야 서비스 키를 받습니다. 아니면 콘솔에 `403 Forbidden`이 뜨고 Vivox 로그인이 실패합니다. 조직 소유자가 대시보드 Organization → Members에서 초대합니다
2. 양쪽 기기 모두 **인터넷**이 되어야 합니다 (게임은 LAN이지만 Vivox는 Unity 서버를 씁니다). 발표장에서는 핫스팟을 준비합니다
3. 접속 패널 상태 문구 둘째 줄이 Play 직후 `Voice: ready`, HOST/JOIN 뒤 `Voice: on`이 되면 채널에 들어간 것입니다. `Voice: unavailable`/`failed`면 인터넷이나 Unity Cloud 권한 문제입니다
4. 에디터 2인 모드는 마이크가 하나라 두 창을 다 켜면 하울링이 납니다. 채널 참가까지만 보고, 소리는 한쪽을 **MIC OFF**한 채 확인합니다
5. 상대가 말하면 상대 머리 위에 스피커 아이콘이 뜨고 소리 줄이 목소리 크기에 따라 0~2개로 바뀝니다. 줄이 너무 쉽게/어렵게 늘면 `VoiceIndicator`의 Level1·Level2를 조정합니다
6. 기기 두 대로 양방향 대화와 에코 유무를 확인합니다 (QA 기준)

### 확인할 것 (푸시 전)

1. **Unity Cloud 조직:** 작업 계정이 조장 조직 `radiorarian`의 멤버가 아니라서 개발 중에는 작업자 조직(`dddong1`)의 프로젝트에 임시로 연결했습니다. `ProjectSettings/ProjectSettings.asset`, `ProjectSettings/Packages/com.unity.services.vivox/Settings.json`, `ProjectSettings/Packages/com.unity.services.core/`는 **커밋하지 않았습니다.** 조장이 작업 계정을 `radiorarian`에 초대하면 이 파일들을 되돌려 원래 연결로 복귀합니다. 어느 조직을 쓸지는 조장과 정합니다
2. 보이스챗은 인터넷이 필요합니다. 발표장 네트워크 대책(핫스팟)은 03 문서 위험 요소와 같습니다
3. 에디터에서 Play가 비정상으로 끝나면 UDP 7777 소켓이 남아 다음 호스트가 7778로 열립니다. 에디터를 재시작하면 풀립니다

---

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
| `LanQuizSync` | 호스트 → 클라이언트: 문제·제한시간, 퀴즈 종료, 버튼 판정 색. 클라이언트 → 호스트: 자기 좌석 단말기 답 + 누른 순간의 네트워크 시간 |
| `GameStateSync` | 호스트 상태를 `NetworkVariable<GameState>`와 전이 RPC로 클라이언트에 맞춘다. 클라이언트 `GameFlowController`는 상태만 비추고 진행은 하지 않는다(`DrivesGame`) |
| `LanGunSync` | 총 지급·치우기, 사수 기기만 잡기, 총 위치와 실린더 모습(열림·각도·총알 수) 전송, 클라이언트 사격은 레이만 보내 호스트 약실로 판정, 발사 결과를 클라이언트 이펙트로 |
| `LanSessionButton`, `LanSessionStatusText` | 방 오른쪽 앞 패널의 HOST/JOIN/LEAVE/START 버튼과 상태 글자 |
| `VirtualPlayerInput` | **에디터 전용.** Player 2 창은 XR을 끄고 시뮬레이터로, 메인 에디터는 헤드셋이 있으면 시뮬레이터를 만들지 않는다 |

- 템플릿의 네트워크 코드(`XRINetworkGameManager` 등)는 쓰지 않습니다. 호스트 권한 모델과 맞지 않는 Distributed Authority를 쓰기 때문입니다(03 문서 참고).
- 동기화는 전부 Named Message입니다. 상태가 더 늘면 `NetworkVariable`로 옮기는 편이 낫습니다.

### 2-2. 게임플레이 (`Scripts/Gameplay/`)

| 파일 | 변경 |
|---|---|
| `GameFlowController` (원래 파일) | `Start On Play` 옵션(RoomScene은 꺼 둠), `StartGame()`, `DrivesGame`, `IsLocalSeat`, `SubmitAnswer`(누른 시각 포함), `SetBody`, `PresentGunForSeat`/`WithdrawGun`, 퀴즈·총 이벤트 추가. 총 지급 위치를 좌석별 2곳 → 테이블 가운데 1곳. 여분 총알·넣고 돌리기 제거 |
| `Gun` (원래 파일) | 멀티용: `SetGrabbable`, `FollowRemotePose`, `ResolvesShotsRemotely`/`ShotRequested`, `FireRemote`, `ShowRemoteShot`. 실린더: 손목 스냅 열기·닫기, `CylinderHandle`을 반대 손으로 잡고 돌리기, `RevolverModel` 연동. 집기용 콜라이더와 "Press Grip to pick up" 안내. 총알 넣기 제거, 놓을 때 던지기 끔 |
| `AvatarBody` (New) | 상대 아바타의 머리·몸통 히트박스. 자기 아바타에서는 끔 |
| `SeatRegistry` (New), `Seat`, `SeatedPlayer` | 좌석 목록, 배정된 의자에만 앉기, 의자 뒤로 옮기기 |
| `HoverOutline` | SkinnedMeshRenderer(총 모델)에도 외곽선 |
| `Core/GameStateMachine` (원래 파일) | `Sync(상태)` 추가: 네트워크로 받은 호스트 상태를 규칙 검사 없이 적용 |
| `SpareBullet`, `GunLoadZone` | **삭제** |

### 2-3. 표현 (`Scripts/Presentation/`)

- `RevolverModel` (New): 총 모델 뼈를 직접 움직입니다. 실린더 45도 열기, 돌리기(잡아서/방아쇠마다 60도), 공이치기, 총알 수만큼 탄피 표시. 모두 연출이며 약실(총알 위치)은 바뀌지 않습니다.
- `InteractionPrompt`: `IsOwnedByOther` 추가 (총 안내가 다른 안내를 덮지 않게).

### 2-4. 에디터 메뉴 (`Scripts/Editor/`, New)

- `BombDefuse > Set Up Room Network`: NetworkManager, 플레이어 프리팹(+AvatarBody), 좌석 목록, 접속 패널(+START), GameFlow에 Lan* 컴포넌트, Build Settings. 여러 번 실행해도 됩니다.
- `BombDefuse > Set Up Revolver Model`: `Prefabs/Pistol`의 상자를 리볼버 모델로 바꾸고 URP 재질(`Art/Material/Revolver_*.mat`)을 만듭니다. 콜라이더·집기 범위·실린더 손잡이·총구 위치 설정.

### 2-5. 에셋·씬

- `Prefabs/NetworkPlayer`, `Art/Material/Avatar.mat`, `Revolver_M1879.mat`, `Revolver_Shell.mat` 추가. `Prefabs/Pistol` 모델 교체. `SpareBullet.prefab`, `Bullet.mat` 삭제.
- `RoomScene`: NetworkManager, SeatRegistry, LanSessionPanel, GunSpawn_Center, GameFlow에 NetworkObject+GameStateSync 추가. GunSpawn_1/2 제거. `TuningConfig.SpareBullets` 제거.
- `Assets/Reichsrevolver_M1879/`: 에셋 스토어 패키지에서 **쓰는 것만**(모델 FBX 2개, 1024·256 텍스처 10장, 약 16MB) 남기고 커밋했습니다. 2048 텍스처, 데모 씬, 애니메이션, 원본 재질·프리팹·스크립트는 지웠습니다(필요하면 에셋 스토어에서 다시 받습니다). 비공개 저장소라 팀 공유용으로 넣었습니다.

---

## 3. 확인한 것 / 못 한 것

| 항목 | 상태 |
|---|---|
| 에디터 2인(호스트+클라이언트) 접속, 좌석, START, 같은 문제 표시 | 확인 |
| 클라이언트 상태 머신이 호스트 상태를 같은 순서로 따라감 | 확인 |
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
