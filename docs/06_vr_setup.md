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

## 헤드셋으로 확인하기 (PC VR)

1. Quest를 Link 케이블 또는 Air Link로 PC에 연결
2. Meta Quest Link 앱 설정에서 **OpenXR 런타임을 Meta Quest Link로 지정**
3. `SampleScene`을 열고 Play
4. 통과 기준: 고개를 돌리면 시점이 따라오고, 컨트롤러로 물체를 잡을 수 있다

## 헤드셋 없이 확인하기

- 템플릿에 포함된 XRI **Interaction Simulator** 샘플로 키보드와 마우스로 조작할 수 있다 (자동으로 켜지는지는 첫 실행 때 확인해서 여기에 적는다)
- 2인 테스트: `Window > Multiplayer > Multiplayer Play Mode`에서 가상 플레이어를 켜면 에디터 하나에서 호스트와 클라이언트를 같이 띄울 수 있다

## 남은 정리 (급하지 않음)

- 빌드 이름(`productName`)과 Unity Cloud 프로젝트 이름이 `VR`로 되어 있다. 게임 이름이 정해지면 바꾼다
- 템플릿에만 필요한 요소(AR Foundation, Android XR 패키지, 에디터 튜토리얼, `Readme.asset`)는 쓰지 않으면 나중에 제거한다
