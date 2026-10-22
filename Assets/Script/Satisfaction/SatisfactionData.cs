using UnityEngine;

namespace InfraTown.Satisfaction
{
    public enum SatisfactionTier
    {
        Desolate,   // 황폐화 (0~20%)
        Stagnant,   // 정체 (21~40%)
        Developing, // 발전 중 (41~60%)
        Stable,     // 안정을 찾은 마을 (61~80%)
        Prosperous  // 살기 좋은 활기찬 마을 (81~100%)
    }

    [System.Serializable]
    public class SatisfactionFactor
    {
        public string factorName;
        public float scoreChange; // + 또는 - 점수 영향

        public SatisfactionFactor(string name, float change)
        {
            factorName = name;
            scoreChange = change;
        }
    }
}