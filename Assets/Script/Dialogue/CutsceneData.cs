using System;
using System.Collections.Generic;
using UnityEngine;

namespace InfraTown.EventSystem
{
    /// <summary>
    /// 컷씬 보여주는 방식 (사용자 기획 2026-11-19)
    /// </summary>
    public enum CutsceneStyle
    {
        /// <summary>카툰 컷: 만화 페이지에 그림 컷이 하나씩 붙는 방식 (필수 시설 미설치·설치 등). 화면은 UI(팀원 1)가 그림</summary>
        Comic,
        /// <summary>시네머신 연출: 팀원이 씬에 만든 시네머신 시퀀스(CinemachineSequence, 보통 Timeline)를 재생 (오프닝 등)</summary>
        Cinemachine
    }

    /// <summary>
    /// 카툰 컷 한 장면 (그림 + 자막)
    /// </summary>
    [Serializable]
    public class CutsceneShot
    {
        [Tooltip("이 컷의 그림")]
        public Sprite image;

        [Tooltip("새 페이지로 넘김 — 앞 페이지의 컷을 모두 지우고 이 컷부터 다시 붙임 (첫 컷은 항상 새 페이지)")]
        public bool newPage;

        [Tooltip("페이지 안에서 이 컷이 놓일 자리 (0~1 비율: x, y, 너비, 높이 — 왼쪽 아래가 0,0)")]
        public Rect pageRect = new Rect(0f, 0f, 1f, 1f);

        [Header("자막")]
        [Tooltip("자막 앞에 붙일 이름 (비우면 해설, {player} = 주인공 이름)")]
        public string speaker;

        [TextArea(2, 4)]
        public string caption;

        [TextArea(2, 4)]
        [Tooltip("쉬운 모드 자막 (비우면 일반 자막)")]
        public string easyCaption;

        [Tooltip("이 시간(초)이 지나면 자동으로 다음 컷. 0이면 클릭(Next)할 때까지 기다림")]
        public float autoAdvanceSeconds;

        public CutsceneShot() { }

        public CutsceneShot(Sprite image, string caption = null, float autoAdvanceSeconds = 0f, bool newPage = false)
        {
            this.image = image;
            this.caption = caption;
            this.autoAdvanceSeconds = autoAdvanceSeconds;
            this.newPage = newPage;
        }

        public string GetCaption(bool easyMode) =>
            easyMode && !string.IsNullOrEmpty(easyCaption) ? easyCaption : caption;
    }

    /// <summary>
    /// 컷씬 한 편. 에셋으로 만들려면 Create > InfraTown > Cutscene.
    /// 대화(DialogueData)의 cutsceneBefore에 넣으면 대화 전에 먼저 나온다.
    ///  - Comic: shots에 컷을 넣는다
    ///  - Cinemachine: sequenceId에 씬의 CinemachineSequence ID를 쓴다 (shots는 쓰지 않음)
    /// </summary>
    [CreateAssetMenu(fileName = "NewCutscene", menuName = "InfraTown/Cutscene")]
    public class CutsceneData : ScriptableObject
    {
        [Tooltip("컷씬 고유 ID (한 번만 재생 판정에 사용)")]
        public string cutsceneId;

        public CutsceneStyle style = CutsceneStyle.Comic;

        [Tooltip("Comic: 카툰 컷 목록")]
        public List<CutsceneShot> shots = new List<CutsceneShot>();

        [Tooltip("Cinemachine: 재생할 씬의 CinemachineSequence ID")]
        public string sequenceId;

        [Tooltip("컷씬 중 게임 시간 정지 (시네머신 시퀀스는 정지 중에도 실제 시간으로 재생됨)")]
        public bool pauseGame = true;

        [Tooltip("한 번 본 컷씬은 다시 재생하지 않음 (뒤에 붙은 대화는 그대로 나옴). 반복되는 불만 컷씬이면 끄기")]
        public bool playOnce = false;

        [Tooltip("건너뛰기(Skip) 허용")]
        public bool skippable = true;

        /// <summary>재생할 내용이 있는지 (Comic은 컷 1개 이상, Cinemachine은 시퀀스 ID)</summary>
        public bool HasContent => style == CutsceneStyle.Cinemachine
            ? !string.IsNullOrEmpty(sequenceId)
            : shots != null && shots.Count > 0;

        /// <summary>코드에서 카툰 컷씬을 만들 때 (에셋으로 저장되지 않음)</summary>
        public static CutsceneData CreateComic(string id, params CutsceneShot[] shots)
        {
            var data = CreateInstance<CutsceneData>();
            data.name = id;
            data.cutsceneId = id;
            data.style = CutsceneStyle.Comic;
            data.shots = new List<CutsceneShot>(shots);
            return data;
        }

        /// <summary>코드에서 시네머신 컷씬을 만들 때 (에셋으로 저장되지 않음)</summary>
        public static CutsceneData CreateCinemachine(string id, string sequenceId)
        {
            var data = CreateInstance<CutsceneData>();
            data.name = id;
            data.cutsceneId = id;
            data.style = CutsceneStyle.Cinemachine;
            data.sequenceId = sequenceId;
            return data;
        }
    }
}
