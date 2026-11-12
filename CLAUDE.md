# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트

InfraTown — 접근성(주민이 병원·소방서·경찰·편의시설에 얼마나 쉽게 닿는가)을 핵심으로 한 3D 도시 건설 시뮬레이션. **Unity 2022.3.45f1** (다른 버전으로 열거나 업그레이드하지 말 것). 응답과 주석은 한국어.

**기능 구현의 기준은 [Docs/기획서.md](Docs/기획서.md)** (원본 docx v5.0, 문서 내 표기 v5.1). 기능을 수정·추가할 때 먼저 기획서를 확인하고, 기획서에 없는 수치(예: 타일당 이동 시간)를 정하면 사용자에게 알린다. 기획서 안에서 서로 다른 내용은 임의로 고르지 말고 사용자에게 묻는다. 기획서가 갱신되면 이 파일을 새 버전으로 교체한다.
- 기획서 내부 불일치(확인 필요): 쉬운 모드 패배 유예가 2장 표는 "90일", 6장은 "60일" / 건물 "13종"인데 표는 12행(도로·전신주를 2종으로 센 것인지) / 5장의 "풍력 발전소"가 건물 목록에 없음.

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

커밋 전에 항상 테스트를 실행하고, 실패하면 커밋하지 않는다.

```bash
powershell -ExecutionPolicy Bypass -File Tools/run-tests.ps1                       # EditMode 전체
powershell -ExecutionPolicy Bypass -File Tools/run-tests.ps1 -Filter EconomyTests  # 클래스/테스트 하나
```

- Unity Test Framework EditMode 테스트. 결과는 `Logs/TestResults-EditMode.xml`, 로그는 `Logs/TestRun-EditMode.log`. 종료 코드 0 = 통과, 2 = 실패, 그 외 = 실행 불가(컴파일 에러 등).
- 에디터가 프로젝트를 열고 있으면 스크립트가 `%LOCALAPPDATA%\InfraTownTestProject`에 Assets/Packages/ProjectSettings를 미러링해서 거기서 돌린다(첫 실행만 전체 임포트로 느림). 에디터 안에서는 Window > General > Test Runner로 같은 테스트를 돌릴 수 있다.
- 선택 사항인 pre-commit 훅: `git config core.hooksPath Tools/hooks` 설정 시 `.cs`가 스테이징된 커밋마다 테스트를 자동 실행한다.
- 테스트는 `Assets/Tests/Editor/`에 있고 asmdef 없이 `Assembly-CSharp-Editor`로 컴파일된다(게임 스크립트가 asmdef 없는 `Assembly-CSharp`이고 DOTween이 firstpass에 있어서 asmdef를 도입하면 참조가 깨진다). EditMode에서는 Awake/Start가 자동 호출되지 않으므로 `TestWorld`(`TestHelpers.cs`)로 매니저를 만들고 정리한다. 기능을 추가·수정하면 해당 테스트도 함께 작성한다.
- `[Ignore("미연결: ...")]` 테스트는 아직 연결되지 않은 기능의 목표 동작이다. 해당 기능을 연결하면 `[Ignore]`를 지워 통과시킨다.
- 실행 결과가 `error CS2011 ... .rsp 지시 파일을 여는 동안 오류`(코드와 무관, 바뀌지 않은 firstpass까지 실패)면, 이전 배치 실행이 남긴 C# 컴파일러 서버(`VBCSCompiler.dll`, 부모 프로세스가 이미 종료된 것) 때문이다. `run-tests.ps1`이 실행 전에 이런 서버를 자동 종료하므로 보통은 다시 실행하면 된다.
- `dotnet build`는 이 PC에 .NET SDK가 없어 사용할 수 없다.
- 동작 확인은 플레이 모드에서 `SystemTestManager` 단축키(1: 종합 현황 UI, 3: 강제 정전, 4: 전력 복구, 5: 월 정산 강제 실행)로 한다. 플레이 테스트는 사용자가 해야 하므로 무엇을 확인할지 구체적으로 안내한다.
- 빌드 씬 순서: `Title` → `2park` (`ProjectSettings/EditorBuildSettings.asset`). 나머지 씬(`1park`, `1park2`, `1Park 2`, `2Park 1`, `SampleScene`)은 팀원별 작업/테스트 씬.

## 아키텍처

모든 스크립트는 `Assets/Script/` 아래 `InfraTown.*` 네임스페이스(`Building`, `Grid`, `Economy`, `Population`, `Power`, `Satisfaction`, `Services`, `TimeSystem`, `Core`, `UI`, `EventSystem`, `Testing`).

**싱글톤 매니저 + 이벤트 구조.** 각 매니저는 `public static X Instance`를 `Awake`에서 설정하고, 상태가 바뀌면 C# `event`를 발생시킨다. UI 스크립트는 `Start`에서 이 이벤트를 구독해 화면을 갱신한다. 매니저끼리는 `X.Instance?.Method()`로 직접 호출한다. 새 시스템도 이 패턴을 따른다.

**데이터.** 건물 정의는 `BuildingData` ScriptableObject(`Assets/Data/Build/*.asset`, 메뉴 `InfraTown/Building Data`). 크기(1x1/2x2/4x4 — 대형은 기획서 3x3에서 사용자 결정으로 4x4로 변경, 2026-11-12), 카테고리(Essential/Facility/Nimbys/Housing/Infrastructure), 전력, 유지비, 건설비, 수용 인구, 만족도 영향·반경, 전력 공급 반경, 해금 인구, 서비스 고정값을 담는다. 수치는 코드가 아니라 이 에셋에서 조정한다. 건물 목록은 `BuildingManager`의 `buildingDatabase`에 등록(씬 인스펙터).
- 기획서 4장 건물 13종은 `Assets/Data/Build/Spec/T{티어}_{이름}.asset`. 기획서에 있는 값(크기, 분류, 티어, 건설/철거 일수, 설명 3종, 필수 시설 반경 12/15)은 기획서 그대로이고, 비용·유지비·전력·수용 인구·만족도 수치·해금 인구(T2 50 / T3 150 / T4 300)는 임시값이다. `BuildingSpecTests`가 기획서 값과의 일치를 검사한다. 프리팹은 기본 도형(Cube/Capsule/Cylinder/Sphere) 임시 연결, 아이콘 없음.
- `Assets/Data/Build/House|Small|Medium.asset`은 초기 테스트용 데이터.
- `1park2.unity`에는 `BuildingManager`가 두 개(GridManager 오브젝트, BuildingManager 오브젝트) 있고 둘 다 13종이 등록되어 있다. 싱글톤 중복 처리가 `Destroy(gameObject)`라서 GridManager 쪽이 중복으로 판정되면 그리드까지 사라질 수 있다(팀원 2와 정리 필요).
- 분류 `Infrastructure`(도로·전신주)는 기획서 4장 '인프라'. `isPowerPole` 데이터는 배치 시 `UtilityPoleInstance`가 자동으로 붙는다.
- 현재 건설 UI(`BuildingUIManager`, 팀원 1)는 `GetBuildingDataByCategoryAndSize`로 분류+크기당 1개만 고를 수 있고 Infrastructure 버튼이 없다. 목록형 UI용 API: `GetBuildingsByCategory`, `GetUnlockedBuildings`, `AllBuildingData`.

**흐름.**
1. `LightweightGridManager` — 60x40, 타일 2m. 오브젝트 없이 Y=0 평면 레이캐스트로 마우스 좌표를 계산해 `OnGridHoverChanged`/`OnGridClicked` 발생. (`GridManager`는 타일 오브젝트를 생성하는 이전 방식)
2. `BuildingManager` — UI가 `SelectBuildingToBuild`로 건물을 고르면 프리뷰·하이라이트 표시, 클릭 시 해금(인구)·점유·자금 검사 후 생성하고 `BuildingInstance`를 붙여 `_occupiedGrid[60,40]`에 등록.
3. `GameLoopManager` — 배치된 건물 리스트의 소유자. 건설/철거 시 `RecalculateAll()`로 인구 수용량 → 전력 → 접근성 → 만족도 순서로 재계산(순서 중요). 주간 틱(인구), 월간 틱(지원금 → 유지비 → 다음 달 전력 구매 순서 보장)을 처리한다. `PowerManager.OnPowerDataChanged`를 구독해 전력 변화 시 만족도도 다시 계산.
4. `SatisfactionManager` — 주택별 만족도 = 70 + 반경(체비셰프) 안 `satisfactionImpact` 합 + 접근 시간 단축 보너스 − 주택별 정전 감점 − 마을 전체 감점(인구 초과, 정전). 주택별 `SatisfactionState`(좋음 ≥71 / 나쁨 ≤30)와 `GoodHousingRatio`를 내고, 마을 수치는 주택 평균 → 5단계 `SatisfactionTier`와 마을 `CurrentState`.
5. `PopulationManager` — 만족도 50% 이상이면 주간 유입. 주택 철거로 수용량이 줄어도 주민은 '초과 인구'로 남아 감점(초과 1%당 0.5, 최대 50)을 주고 매주 초과분의 절반이 떠난다.
6. `EconomyManager` — 월 지원금(15,000 + 인구×50) 수령 후 유지비 지출. 잔액이 모자라면 잔액 0까지만 내고 `LastUnpaidMaintenance`에 기록.
7. `PowerManager` — 시작 시와 매월 목표량 구매. 전봇대(`UtilityPoleInstance`)가 없으면 부족 비율만큼 마을 전체 감점, 있으면 정상 전봇대 반경 밖 주택만 개별 감점. 전력 0이면 전면 정전 감점 30. (현재 전봇대가 붙은 프리팹은 없음)
8. `ServiceAccessibilityManager` — 주택별로 가장 빠른 시설까지 접근 시간 = 시설 `fixedAccessTimeMinutes`(기본 시간) + 두 건물 영역 사이 빈 칸 수(맨해튼) × `minutesPerTile`(0.5분). 마을 수치는 주택 평균, 기준(15분) 넘는 주택 수는 불만 문구로. 시설 종류는 ID/이름(police·경찰, hospital·병원, fire·소방, school·학교, convenience·store·편의점)으로 판별하고 나머지(마을회관, 버스 정류장 등)는 접근성 계산에서 빠진다. 만족도 보너스 = 서비스마다 (읍내 기준 60분 − 접근 시간)/60 × 6점 (기획서 6장 '단축 비율 보너스').
10. `GameOutcomeManager` — 엔딩 판정(하루마다 `TimeManager.OnDateChanged`). 패배: 마을 평균 '나쁨' 30일 연속(`RequestDefeatGrace/ReleaseDefeatGrace`로 공사 중 유예). 승리: 10년(360일×10) 경과 + 필수 시설(병원·학교·소방·경찰) 설치 + '좋음' 주택 70% 이상 30일 연속 + 인구 기준(기획 미정, 0). 10년에 미달이면 계속 플레이. 씬에 없으면 `GameLoopManager.Start`가 자기 오브젝트에 추가한다(씬 수정 회피). 종료 시 시간 정지 + `OnGameEnded`.
9. `TimeManager` — 하루 90초, 0~3배속(`Time.timeScale`). 팝업/대화는 `RequestEventPause(source)`/`ReleaseEventPause(source)`로 일시정지 잠금을 건다.

건물 철거는 `BuildingInstance.DestroyBuilding()` → 정적 이벤트 `OnBuildingDestroyed`를 `BuildingManager`(점유 해제)와 `GameLoopManager`(재계산)가 구독.

**남은 과제 (2026-11-12 기준).**
- 엔딩·경고를 보여줄 UI(팀원 1)가 없다: `GameOutcomeManager.OnGameEnded`, `OnBadStreakChanged`를 구독하면 된다.
- `OverviewUI`(팀원 1 담당)의 소방서 접근 시간 표시가 `{fireStationTimeText:F1}`로 되어 있어 값이 아닌 텍스트 오브젝트를 출력한다 → `mgr.FireStationAccessTime`이어야 함.
- `Assets/Data/Build/House.asset`(테스트용)의 `housingCapacity`가 0이다. 기획서 데이터는 `Spec/T1_House.asset`(수용 10).
- 건설·철거 기간(`constructionDays`/`demolitionDays`)은 데이터만 있고 로직 없음(즉시 완공). 주민세, 풍력 발전소, 인구 구간별 지원금, 모드 선택도 미구현.

## Git

- 원격: `pinstar1234/leeyongho`, 기본 브랜치 `main`. 각자 브랜치(사용자: `Park1`)에서 작업 → `Merge` 브랜치 → `main`으로 PR.
- 씬·프리팹은 YAML 병합 충돌이 잦으므로 담당자가 아닌 씬/프리팹을 커밋에 포함하지 않는다.
- `.csproj`, `.sln`, `Library/`, `Temp/`, `Logs/`, `UserSettings/`는 Unity가 생성하는 파일로 커밋하지 않는다.
