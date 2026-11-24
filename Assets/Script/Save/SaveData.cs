using System;
using System.Collections.Generic;

namespace InfraTown.Save
{
    /// <summary>
    /// 저장 파일 한 개의 내용 (JSON). 카메라 위치·줌을 뺀 게임 상태 전부 (사용자 기획 2026-11-24).
    /// 각 매니저가 자기 상태를 GetSaveState()로 내보내고 ApplySaveState()로 되돌린다.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;

        /// <summary>저장한 씬 이름 (이어하기 때 이 씬을 연다)</summary>
        public string sceneName;

        /// <summary>저장한 시각 (현지 시각, "yyyy-MM-dd HH:mm")</summary>
        public string savedAt;

        public string playerName;

        public TimeSaveState time = new TimeSaveState();
        public EconomySaveState economy = new EconomySaveState();
        public PopulationSaveState population = new PopulationSaveState();
        public PowerSaveState power = new PowerSaveState();
        public SatisfactionSaveState satisfaction = new SatisfactionSaveState();
        public OutcomeSaveState outcome = new OutcomeSaveState();
        public List<BuildingSaveState> buildings = new List<BuildingSaveState>();
        public DialogueSaveState dialogue = new DialogueSaveState();
        public DialogueTriggerSaveState dialogueTriggers = new DialogueTriggerSaveState();
        public TutorialSaveState tutorial = new TutorialSaveState();
    }

    [Serializable]
    public class TimeSaveState
    {
        public int year = 1, month = 1, day = 1;
        public int totalDaysPassed;
        public float dayTimer;
        public int speed = 1; // GameSpeed
    }

    [Serializable]
    public class EconomySaveState
    {
        public int money;
        public int requestedSubsidy;
        public int lastMonthlyIncome;
        public int lastMaintenancePaid;
        public int lastUnpaidMaintenance;
    }

    [Serializable]
    public class PopulationSaveState
    {
        public int currentPopulation;
    }

    [Serializable]
    public class PowerSaveState
    {
        public int targetPurchasedPower;
        public int currentAvailablePower;
    }

    [Serializable]
    public class SatisfactionSaveState
    {
        /// <summary>필수 시설 미설치 누적 감점 (ServiceType 정수값과 감점을 같은 순서로)</summary>
        public List<int> missingPenaltyServices = new List<int>();
        public List<float> missingPenaltyValues = new List<float>();
    }

    [Serializable]
    public class OutcomeSaveState
    {
        public int daysElapsed;
        public int goodStreakDays;
        public int badStreakDays;
        public int result; // GameResult
    }

    [Serializable]
    public class BuildingSaveState
    {
        public string buildingID;
        public string buildingName; // ID가 없거나 바뀌었을 때 대신 찾는 용도
        public int x, y;
        public float rotationY;
    }

    [Serializable]
    public class DialogueSaveState
    {
        public List<string> playedDialogueIds = new List<string>();
        public List<string> playedCutsceneIds = new List<string>();
        public bool useEasyText;
        public bool autoMode;
    }

    [Serializable]
    public class EssentialFacilitySaveState
    {
        public int service; // ServiceType
        public int missingDays;
        public int threshold;
        public bool firstInstallPlayed;
    }

    [Serializable]
    public class DialogueTriggerSaveState
    {
        public bool storyStarted;
        public List<string> knownUnlockedBuildingIds = new List<string>();
        public List<EssentialFacilitySaveState> essentials = new List<EssentialFacilitySaveState>();
    }

    [Serializable]
    public class TutorialSaveState
    {
        public bool active;
        public bool completed;
        public int currentDay;
        public List<string> triggeredStepIds = new List<string>();
    }

    /// <summary>타이틀 '이어하기' 버튼 옆에 보여줄 저장 요약</summary>
    [Serializable]
    public class SaveSummary
    {
        public string savedAt;
        public string sceneName;
        public string playerName;
        public int year, month, day;
        public int money;
        public int population;
        public int buildingCount;
    }
}
