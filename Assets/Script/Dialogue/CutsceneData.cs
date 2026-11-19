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
        /// <summary>카툰 컷: 만화 페이지에 그림 컷이 하나씩 붙는 방식 (필수 시설 미설치·설치 등)</summary>
        Comic,
        /// <summary>게임 화면 연출: 카메라가 마을 곳곳을 비추며 자막이 나오는 방식 (오프닝 — 마을에 도착하기까지)</summary>
        Scene
    }

    /// <summary>
    /// 컷씬 한 장면. 카툰 컷이면 그림(image), 게임 화면 연출이면 카메라 위치(cameraPointId)를 주로 쓴다.
    /// 둘 다 자막(caption)을 띄울 수 있다.
    /// </summary>
    [Serializable]
    public class CutsceneShot
    {
        [Header("카툰 컷")]
        [Tooltip("이 장면의 그림 (카툰 컷)")]
        public Sprite image;

        [Tooltip("새 페이지로 넘김 — 앞 페이지의 컷을 모두 지우고 이 컷부터 다시 붙임 (첫 장면은 항상 새 페이지)")]
        public bool newPage;

        [Tooltip("페이지 안에서 이 컷이 놓일 자리 (0~1 비율: x, y, 너비, 높이 — 왼쪽 아래가 0,0)")]
        public Rect pageRect = new Rect(0f, 0f, 1f, 1f);

        [Header("게임 화면 연출")]
        [Tooltip("카메라를 옮길 위치 — 씬에 놓은 CutsceneCameraPoint의 ID (비우면 카메라 그대로)")]
        public string cameraPointId;

        [Tooltip("카메라가 그 위치까지 움직이는 시간(초). 0이면 바로 이동")]
        public float cameraMoveSeconds = 1.5f;

        [Tooltip("이 장면이 시작될 때 씬의 CutsceneSceneEvent(같은 ID)를 실행 — 버스 도착 애니메이션 등")]
        public string sceneEventId;

        [Header("자막")]
        [Tooltip("자막 앞에 붙일 이름 (비우면 해설)")]
        public string speaker;

        [TextArea(2, 4)]
        public string caption;

        [TextArea(2, 4)]
        [Tooltip("쉬운 모드 자막 (비우면 일반 자막)")]
        public string easyCaption;

        [Header("넘기기")]
        [Tooltip("이 시간(초)이 지나면 자동으로 다음 장면. 0이면 클릭(Next)할 때까지 기다림")]
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
    /// </summary>
    [CreateAssetMenu(fileName = "NewCutscene", menuName = "InfraTown/Cutscene")]
    public class CutsceneData : ScriptableObject
    {
        [Tooltip("컷씬 고유 ID (한 번만 재생 판정에 사용)")]
        public string cutsceneId;

        public CutsceneStyle style = CutsceneStyle.Comic;

        public List<CutsceneShot> shots = new List<CutsceneShot>();

        [Tooltip("컷씬 중 게임 시간 정지")]
        public bool pauseGame = true;

        [Tooltip("한 번 본 컷씬은 다시 재생하지 않음 (뒤에 붙은 대화는 그대로 나옴). 반복되는 불만 컷씬이면 끄기")]
        public bool playOnce = false;

        [Tooltip("건너뛰기(Skip) 허용")]
        public bool skippable = true;

        [Tooltip("끝나면 카메라를 컷씬 전 위치로 되돌림 (끄면 마지막 장면 위치에 남음 — 오프닝에서 마을 도착 위치로 끝낼 때)")]
        public bool restoreCamera = true;

        /// <summary>코드에서 즉석 컷씬을 만들 때 (에셋으로 저장되지 않음)</summary>
        public static CutsceneData Create(string id, CutsceneStyle style, params CutsceneShot[] shots)
        {
            var data = CreateInstance<CutsceneData>();
            data.name = id;
            data.cutsceneId = id;
            data.style = style;
            data.shots = new List<CutsceneShot>(shots);
            return data;
        }
    }
}
