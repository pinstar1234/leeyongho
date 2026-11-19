using System;
using System.Collections.Generic;
using UnityEngine;

namespace InfraTown.EventSystem
{
    /// <summary>
    /// 대화창에서 말하는 사람이 서는 자리.
    /// 주인공은 항상 왼쪽, 주민·비서 등 다른 인물은 오른쪽 (사용자 기획 2026-11-19)
    /// </summary>
    public enum DialogueSide
    {
        /// <summary>자동: 주인공 이름이면 왼쪽, 다른 이름이면 오른쪽, 이름이 비면 해설</summary>
        Auto,
        Left,
        Right,
        /// <summary>해설 (어느 쪽도 강조하지 않음)</summary>
        Narration
    }

    /// <summary>
    /// 대화에 나오는 인물 (이름 → 기본 초상화). DialogueEventManager의 characters에 등록
    /// </summary>
    [Serializable]
    public class DialogueCharacter
    {
        [Tooltip("대화 줄의 speaker와 같은 이름 (예: 주민, 비서, 이장님)")]
        public string name;

        [Tooltip("기본 초상화 (줄마다 portrait를 넣으면 그 줄은 그걸 사용 — 표정 바꾸기용)")]
        public Sprite portrait;

        public DialogueCharacter() { }
        public DialogueCharacter(string name, Sprite portrait = null) { this.name = name; this.portrait = portrait; }
    }

    /// <summary>
    /// 대화 한 줄 (말하는 사람 + 초상화 + 문장)
    /// </summary>
    [Serializable]
    public class DialogueLine
    {
        [Tooltip("말하는 사람 이름 (예: 주인공, 주민, 비서, 이장님). 비우면 해설")]
        public string speaker;

        [Tooltip("이 줄에서만 쓸 초상화 (비우면 등록된 인물의 기본 초상화)")]
        public Sprite portrait;

        [Tooltip("서는 자리 (자동이면 주인공은 왼쪽, 나머지는 오른쪽)")]
        public DialogueSide side = DialogueSide.Auto;

        [TextArea(2, 5)]
        [Tooltip("일반 모드 문장")]
        public string text;

        [TextArea(2, 5)]
        [Tooltip("어르신·어린이 모드용 쉬운 문장 (비워두면 일반 문장 사용, 기획서 2장)")]
        public string easyText;

        public DialogueLine() { }

        public DialogueLine(string speaker, string text, string easyText = null, Sprite portrait = null)
        {
            this.speaker = speaker;
            this.text = text;
            this.easyText = easyText;
            this.portrait = portrait;
        }

        /// <summary>모드에 맞는 문장</summary>
        public string GetText(bool easyMode) =>
            easyMode && !string.IsNullOrEmpty(easyText) ? easyText : text;
    }

    /// <summary>
    /// 스토리·해금·경고 등 대화 한 묶음. 에셋으로 만들려면 Create > InfraTown > Dialogue
    /// </summary>
    [CreateAssetMenu(fileName = "NewDialogue", menuName = "InfraTown/Dialogue")]
    public class DialogueData : ScriptableObject
    {
        [Tooltip("대화 고유 ID (한 번만 재생 판정·저장에 사용)")]
        public string dialogueId;

        [Tooltip("대화 전에 먼저 보여줄 컷씬 (카툰 컷·오프닝 연출). 비우면 바로 대화")]
        public CutsceneData cutsceneBefore;

        public List<DialogueLine> lines = new List<DialogueLine>();

        [Tooltip("대화 중 게임 시간 정지 (대화를 보여줄 UI가 없으면 멈추지 않음)")]
        public bool pauseGame = true;

        [Tooltip("한 번 본 대화는 다시 재생하지 않음")]
        public bool playOnce = true;

        /// <summary>코드에서 즉석 대화를 만들 때 (에셋으로 저장되지 않음)</summary>
        public static DialogueData Create(string id, bool pauseGame, bool playOnce, params DialogueLine[] lines)
        {
            var data = CreateInstance<DialogueData>();
            data.name = id;
            data.dialogueId = id;
            data.pauseGame = pauseGame;
            data.playOnce = playOnce;
            data.lines = new List<DialogueLine>(lines);
            return data;
        }
    }
}
