using System;
using System.Collections.Generic;
using UnityEngine;

namespace InfraTown.EventSystem
{
    /// <summary>
    /// 대화 한 줄 (말하는 사람 + 초상화 + 문장)
    /// </summary>
    [Serializable]
    public class DialogueLine
    {
        [Tooltip("말하는 사람 이름 (예: 이장님, 안내원)")]
        public string speaker;

        [Tooltip("말하는 사람 초상화 (없으면 UI에서 숨김)")]
        public Sprite portrait;

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
