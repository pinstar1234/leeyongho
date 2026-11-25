using System;
using System.Text;

namespace InfraTown.Core
{
    /// <summary>
    /// 플레이어(주인공) 정보. 게임을 시작할 때 정한 이름을 씬이 바뀌어도 유지한다 (게임을 끄면 사라짐 — 저장 기능이 생기면 거기에 넣기).
    ///
    /// 이름 정하기 (사용자 기획 2026-11-25): 타이틀이 아니라 게임을 시작했을 때 정한다 — 게임 씬에서 DialogueTriggers.OnPlayerNameRequested를 받은
    ///   입력창이 DialogueTriggers.SubmitPlayerName(입력값)을 부른다. 타이틀 '새로 시작'(SaveSystem.StartNewGame)은 이전 이름을 지운다.
    /// 대화·컷씬 문장에 {player} 또는 {이름}을 쓰면 정한 이름으로 바뀌어 나온다 (사용자 기획 2026-11-19).
    /// </summary>
    public static class PlayerProfile
    {
        /// <summary>이름을 정하지 않았을 때 쓰는 이름</summary>
        public const string DefaultName = "주인공";

        /// <summary>이름 최대 글자 수</summary>
        public const int MaxNameLength = 10;

        /// <summary>대화 문장에서 이름으로 바뀌는 표시</summary>
        public static readonly string[] NameTokens = { "{player}", "{이름}" };

        private static string _playerName;

        /// <summary>주인공 이름 (정하지 않았으면 DefaultName)</summary>
        public static string PlayerName => string.IsNullOrEmpty(_playerName) ? DefaultName : _playerName;

        /// <summary>이번 게임에서 이름을 정했는지</summary>
        public static bool HasName => !string.IsNullOrEmpty(_playerName);

        /// <summary>이름이 바뀌었을 때 (새 이름)</summary>
        public static event Action<string> OnPlayerNameChanged;

        /// <summary>
        /// 이름 정하기. 앞뒤 공백을 지우고, 태그·치환 기호(&lt; &gt; { })는 빼고 저장한다.
        /// 비었거나 너무 길면 false와 오류 문구(입력창에 그대로 보여주면 됨).
        /// </summary>
        public static bool TrySetPlayerName(string name, out string error)
        {
            string clean = Sanitize(name);
            if (clean.Length == 0) { error = "이름을 입력해 주세요."; return false; }
            if (clean.Length > MaxNameLength) { error = $"이름은 {MaxNameLength}글자까지 쓸 수 있어요."; return false; }

            error = null;
            if (clean == _playerName) return true;
            _playerName = clean;
            OnPlayerNameChanged?.Invoke(_playerName);
            return true;
        }

        /// <summary>새 게임을 시작할 때 이름 지우기</summary>
        public static void ClearPlayerName()
        {
            if (_playerName == null) return;
            _playerName = null;
            OnPlayerNameChanged?.Invoke(PlayerName);
        }

        /// <summary>문장의 {player}·{이름}을 주인공 이름으로 바꿈</summary>
        public static string ApplyTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            foreach (var token in NameTokens)
            {
                if (text.Contains(token)) text = text.Replace(token, PlayerName);
            }
            return text;
        }

        /// <summary>문장에 이름 표시가 있는지</summary>
        public static bool IsNameToken(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var token in NameTokens) if (text == token) return true;
            return false;
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                // 리치 텍스트 태그·치환 기호·줄바꿈 등 제어 문자는 뺌
                if (c == '<' || c == '>' || c == '{' || c == '}' || char.IsControl(c)) continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
