using UnityEngine;

namespace InfraTown.TimeSystem
{
    /// <summary>계절 (사용자 기획 2026-11-24: 봄 3~5월, 여름 6~8월, 가을 9~11월, 겨울 12~2월)</summary>
    public enum Season
    {
        Spring,
        Summer,
        Autumn,
        Winter
    }

    public static class SeasonUtil
    {
        /// <summary>달(1~12) → 계절</summary>
        public static Season FromMonth(int month)
        {
            switch (month)
            {
                case 3: case 4: case 5: return Season.Spring;
                case 6: case 7: case 8: return Season.Summer;
                case 9: case 10: case 11: return Season.Autumn;
                default: return Season.Winter; // 12, 1, 2
            }
        }

        /// <summary>표시용 이름 (봄/여름/가을/겨울)</summary>
        public static string ToKorean(Season season) => season switch
        {
            Season.Spring => "봄",
            Season.Summer => "여름",
            Season.Autumn => "가을",
            _ => "겨울"
        };

        /// <summary>정반대 계절 (여름 ↔ 겨울, 봄 ↔ 가을)</summary>
        public static Season Opposite(Season season) => season switch
        {
            Season.Spring => Season.Autumn,
            Season.Summer => Season.Winter,
            Season.Autumn => Season.Spring,
            _ => Season.Summer
        };

        /// <summary>
        /// 계절 소비 배율 (사용자 기획 2026-11-24). 가장 많이 쓰는 계절 = peakMultiplier(2.5),
        /// 정반대 계절 = 1(최저), 그 사이 계절(봄·가을) = 중간 (1 + peak) / 2.
        /// </summary>
        public static float GetDemandMultiplier(Season season, Season peakSeason, float peakMultiplier)
        {
            if (season == peakSeason) return peakMultiplier;
            if (season == Opposite(peakSeason)) return 1f;
            return (1f + peakMultiplier) * 0.5f;
        }

        /// <summary>
        /// 인구 기준 계절 소비량 = 최저 소비량(인구 100명당 per100) × 계절 배율, 반올림
        /// </summary>
        public static int GetSeasonalDemand(int population, float per100, Season season, Season peakSeason, float peakMultiplier)
        {
            float min = Mathf.Max(0, population) * Mathf.Max(0f, per100) / 100f;
            return Mathf.RoundToInt(min * GetDemandMultiplier(season, peakSeason, peakMultiplier));
        }
    }
}
