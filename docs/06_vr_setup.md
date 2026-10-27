# 06. VR 프로젝트 설정

> `feature/vr-test` 브랜치의 VR 기본 설정을 반영한 상태다. **에디터에서 한 번 해야 하는 작업**이 남아 있다.

## 이미 되어 있는 것

| 항목 | 상태 |
|---|---|
| XR Plug-in Management | 4.6.1 설치됨 |
| OpenXR | 1.18.0 설치됨 |
| XR Interaction Toolkit | 3.6.1 설치됨, Starter Assets 샘플 포함 |
| OpenXR 컨트롤러 프로필 | Oculus Touch, Meta Quest Touch Plus, Meta Quest Touch Pro 활성화 (PC 빌드 타깃) |
| 테스트 씬 | `Assets/Scenes/VRGrabTest.unity` (XR Origin + 잡을 수 있는 박스 3개). 메뉴 `Tools > VR Test > Build Grab Test Scene`으로 다시 만들 수 있다 |

## 에디터에서 해야 하는 것 (한 명만, 한 번)

XR 로더 설정 파일(`Assets/XR/XRGeneralSettingsPerBuildTarget.asset`)이 저장소에 없다. 이 파일이 없으면 **Play를 눌러도 헤드셋으로 화면이 나오지 않는다.** 이 파일은 에디터가 만들어야 해서 직접 작성할 수 없다.

1. `Edit > Project Settings > XR Plug-in Management`
2. **PC(Windows) 탭**에서 `OpenXR` 체크, `Initialize XR on Startup` 체크
3. 경고 아이콘(⚠)이 보이면 클릭해서 `Fix All`
4. `XR Plug-in Management > OpenXR > Project Validation`에 남은 문제가 없는지 확인
5. Quest 단독 빌드도 할 거라면 **Android 탭**에서도 같은 설정을 하고, OpenXR 기능에서 `Meta Quest Support`를 켠다
6. 생성된 `Assets/XR/` 아래 파일들과 `ProjectSettings/` 변경을 **커밋한다**

## 헤드셋으로 확인하기 (PC VR)

1. Quest를 Link 케이블 또는 Air Link로 PC에 연결
2. Meta Quest Link 앱 설정에서 **OpenXR 런타임을 Meta Quest Link로 지정**
3. `VRGrabTest` 씬을 열고 Play
4. 통과 기준: 고개를 돌리면 시점이 따라오고, 컨트롤러로 박스를 잡을 수 있다

## 헤드셋 없이 확인하기

- XR Interaction Toolkit 패키지의 `XR Device Simulator` 샘플을 가져와 씬에 넣으면 키보드와 마우스로 테스트할 수 있다.
