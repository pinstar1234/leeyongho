# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트

InfraTown — 접근성(주민이 병원·소방서·경찰·편의시설에 얼마나 쉽게 닿는가)을 핵심으로 한 3D 도시 건설 시뮬레이션. **Unity 2022.3.45f1** (다른 버전으로 열거나 업그레이드하지 말 것). 응답과 주석은 한국어.

## 팀 구성과 작업 범위 (반드시 지킬 것)

| 담당 | 역할 | 소유 영역 |
|---|---|---|
| 사용자 (Claude가 돕는 사람) | 기능 구현, 기획 | 게임 로직 스크립트 |
| 팀원 1 | UI 제작 | `*UI.cs`, `BuildingUIManager.cs`, `BuildingSlotUI.cs`, `Assets/Script/UI/`, Canvas/UI 프리팹 |
| 팀원 2 | 에셋 적용, 이펙트 등 디자인 | 씬(`.unity`), 프리팹, 머티리얼, 폰트, 스프라이트, `Assets/Plugins/`(DOTween) |

- **기능(게임 로직) 코드만 수정한다.** 매니저, 데이터, 인스턴스, 그리드, 시간, 테스트용 스크립트(`SystemTestManager`, `GridTestObserver`)가 여기에 해당한다.
- 그 외 파일(UI 스크립트, 씬, 프리팹, 에셋, 프로젝트 설정)의 변경이 꼭 필요하면 **최소 2번 이상 사용자에게 확인을 받은 뒤** 변경한다. 한 번의 동의로 진행하지 않는다.
- 기능 코드는 팀원이 UI/연출에서 가져다 쓸 수 있게 **열어둔다**: 상태는 `public` getter 프로퍼티, 상태 변화는 `public event`, 조정 수치는 `[SerializeField]`로 인스펙터에 노출. 기존 public API(메서드 이름·시그니처·이벤트)는 UI 스크립트가 의존하므로 바꾸면 사용처를 먼저 확인하고 사용자에게 알린다.

## 테스트 후 커밋

커밋 전에 항상 테스트를 진행한다. 자동화 테스트(Test Runner/asmdef)는 아직 없으므로 최소한 컴파일 확인을 거친다.

- **에디터가 열려 있을 때** (`Temp/UnityLockfile` 존재): 배치 모드는 실행되지 않는다. 사용자에게 에디터로 포커스를 옮겨 재컴파일하게 한 뒤 로그에서 에러를 확인한다.
  ```bash
  grep -n "error CS" "$LOCALAPPDATA/Unity/Editor/Editor.log"
  ```
- **에디터가 닫혀 있을 때**: 배치 모드로 컴파일 확인.
  ```bash
  "/c/Program Files/Unity/Hub/Editor/2022.3.45f1/Editor/Unity.exe" -batchmode -quit -nographics -projectPath . -logFile - | grep -E "error CS|Exiting batchmode"
  ```
- `dotnet build`는 이 PC에 .NET SDK가 없어 사용할 수 없다.
- 동작 확인은 플레이 모드에서 `SystemTestManager` 단축키(1: 종합 현황 UI, 3: 강제 정전, 4: 전력 복구, 5: 월 정산 강제 실행)로 한다. 플레이 테스트는 사용자가 해야 하므로 무엇을 확인할지 구체적으로 안내한다.
- 빌드 씬 순서: `Title` → `2park` (`ProjectSettings/EditorBuildSettings.asset`). 나머지 씬(`1park`, `1park2`, `1Park 2`, `2Park 1`, `SampleScene`)은 팀원별 작업/테스트 씬.

## 아키텍처

모든 스크립트는 `Assets/Script/` 아래 `InfraTown.*` 네임스페이스(`Building`, `Grid`, `Economy`, `Population`, `Power`, `Satisfaction`, `Services`, `TimeSystem`, `Core`, `UI`, `EventSystem`, `Testing`).

**싱글톤 매니저 + 이벤트 구조.** 각 매니저는 `public static X Instance`를 `Awake`에서 설정하고, 상태가 바뀌면 C# `event`를 발생시킨다. UI 스크립트는 `Start`에서 이 이벤트를 구독해 화면을 갱신한다. 매니저끼리는 `X.Instance?.Method()`로 직접 호출한다. 새 시스템도 이 패턴을 따른다.

**데이터.** 건물 정의는 `BuildingData` ScriptableObject(`Assets/Data/Build/*.asset`, 메뉴 `InfraTown/Building Data`). 크기(1x1/2x2/3x3), 카테고리(Essential/Facility/Nimbys/Housing), 전력, 유지비, 건설비, 수용 인구, 만족도 영향·반경, 전력 공급 반경, 해금 인구, 서비스 고정값을 담는다. 수치는 코드가 아니라 이 에셋에서 조정한다. 건물 목록은 `BuildingManager`의 `buildingDatabase`에 등록.

**흐름.**
1. `LightweightGridManager` — 60x40, 타일 2m. 오브젝트 없이 Y=0 평면 레이캐스트로 마우스 좌표를 계산해 `OnGridHoverChanged`/`OnGridClicked` 발생. (`GridManager`는 타일 오브젝트를 생성하는 이전 방식)
2. `BuildingManager` — UI가 `SelectBuildingToBuild`로 건물을 고르면 프리뷰·하이라이트 표시, 클릭 시 해금(인구)·점유·자금 검사 후 생성하고 `BuildingInstance`를 붙여 `_occupiedGrid[60,40]`에 등록.
3. `GameLoopManager` — 배치된 건물 리스트의 소유자. 건설/철거 시 인구 수용량과 만족도를 재계산하고, `TimeManager`의 주간 틱(인구 유입)·월간 틱(정산)을 처리한다.
4. `SatisfactionManager` — 주택마다 반경(체비셰프 거리) 안 건물의 `satisfactionImpact` 합산 → 평균 − 인구 초과 패널티 − 정전 패널티 → 5단계 `SatisfactionTier`.
5. `PopulationManager`(만족도 50% 이상이면 주간 유입), `EconomyManager`(월 지원금 = 15,000 + 인구×50), `PowerManager`(월 전력 구매, 부족 시 무작위 전봇대 정전), `ServiceAccessibilityManager`(시설 유무와 접근 시간·순찰 횟수, 불만 문구).
6. `TimeManager` — 하루 90초, 0~3배속(`Time.timeScale`). 팝업/대화는 `RequestEventPause(source)`/`ReleaseEventPause(source)`로 일시정지 잠금을 건다.

건물 철거는 `BuildingInstance.DestroyBuilding()` → 정적 이벤트 `OnBuildingDestroyed`를 `BuildingManager`(점유 해제)와 `GameLoopManager`(재계산)가 구독.

**알려진 미연결 부분 (2026-11-12 기준).**
- `ServiceAccessibilityManager.RecalculateServiceMetrics`, `PowerManager.RecalculatePowerUsage`를 호출하는 곳이 없다 → `GameLoopManager`의 등록/철거 시점에 연결해야 한다.
- `GameLoopManager.HandleMonthlyTick`이 유지비 합계를 `EconomyManager.ProcessMonthlySettlement(population, satisfaction)`의 두 번째 인자로 넘기지만 유지비는 차감되지 않는다.
- 인구가 수용량으로 clamp되어 인구 초과 패널티가 항상 0.
- 접근성은 시설 존재 여부만 보며 거리를 쓰지 않는다(`fixedAccessTimeMinutes` 고정값).

## Git

- 원격: `pinstar1234/leeyongho`, 기본 브랜치 `main`. 각자 브랜치(사용자: `Park1`)에서 작업 → `Merge` 브랜치 → `main`으로 PR.
- 씬·프리팹은 YAML 병합 충돌이 잦으므로 담당자가 아닌 씬/프리팹을 커밋에 포함하지 않는다.
- `.csproj`, `.sln`, `Library/`, `Temp/`, `Logs/`, `UserSettings/`는 Unity가 생성하는 파일로 커밋하지 않는다.
