using System;
using System.Text;
using Edgewise.Game.Online;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// オンライン対戦の部屋の画面（DESIGN §9-1）。フリー対戦の設定画面の「オンライン対戦」から開く。
    /// - メニュー: 「部屋を作る」／ 部屋コードを入力して「入る」。コードはキーボードで入力する（スマホは招待 URL から入る）。
    /// - 部屋: 部屋コード（大きく）と招待 URL、それぞれのコピーボタン、相手を待つ表示。
    /// - 状態の 1 行（つなぎ中・エラーなど）と、接続できない環境があることの注記（常に表示）。
    /// 対戦中の「再接続を待っています」は別の部品（OnlineNotice）。
    /// </summary>
    public sealed class OnlineScreen : MonoBehaviour, IResponsiveUi
    {
        public enum Mode
        {
            Menu = 0,
            Room = 1,
            Error = 2,
        }

        [SerializeField] Image panel;
        [SerializeField] TextMeshProUGUI title;
        [SerializeField] TextMeshProUGUI status;
        [SerializeField] Button createButton;
        [SerializeField] TextMeshProUGUI joinLabel;
        [SerializeField] Image codeBox;
        [SerializeField] TextMeshProUGUI codeText;
        [SerializeField] Button joinButton;
        [SerializeField] TextMeshProUGUI roomLabel;
        [SerializeField] TextMeshProUGUI roomCode;
        [SerializeField] Button copyCodeButton;
        [SerializeField] Button copyUrlButton;
        [SerializeField] TextMeshProUGUI urlText;
        [SerializeField] TextMeshProUGUI shareNote;
        [SerializeField] TextMeshProUGUI networkNote;
        [SerializeField] TextMeshProUGUI openNote;
        [SerializeField] Button backButton;

        readonly StringBuilder _typed = new StringBuilder();
        Keyboard _kb;
        bool _portrait;
        string _inviteUrl = "";
        string _code = "";

        public event Action CreatePressed;
        public event Action<string> JoinPressed;
        public event Action BackPressed;

        public Mode CurrentMode { get; private set; } = Mode.Menu;
        public bool IsOpen => gameObject.activeSelf;
        public Button CreateButton => createButton;
        public Button JoinButton => joinButton;
        public Button BackButton => backButton;
        public Button CopyCodeButton => copyCodeButton;
        public Button CopyUrlButton => copyUrlButton;
        public string StatusText => status != null ? status.text : "";
        public string NetworkNoteText => networkNote != null ? networkNote.text : "";
        public string Typed => _typed.ToString();
        public string RoomCodeText => _code;
        public string InviteUrl => _inviteUrl;

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            UiKit.Fill(root, "Dim", Theme.Dim);
            panel = UiKit.Panel(root, "Panel", a, Theme.Panel);
            var p = panel.transform;
            title = UiKit.Label(p, "Title", a, UiText.OnlineTitle, 38, Theme.Gold, TextAlignmentOptions.Center, true);
            status = UiKit.Label(p, "Status", a, "", 24, Theme.Ink);
            createButton = UiKit.Button(p, "OnlineCreateButton", a, UiText.OnlineCreate, 30, out _, new Color(0.25f, 0.42f, 0.40f));
            joinLabel = UiKit.Label(p, "JoinLabel", a, UiText.OnlineJoinLabel, 22, Theme.InkDim);
            codeBox = UiKit.Panel(p, "CodeBox", a, new Color(0.05f, 0.06f, 0.14f, 1f), false);
            codeText = UiKit.Label(codeBox.transform, "CodeText", a, "", 34, Theme.Ink);
            UiKit.Stretch(codeText.rectTransform, 6, 2, 6, 2);
            codeText.textWrappingMode = TextWrappingModes.NoWrap;
            joinButton = UiKit.Button(p, "OnlineJoinButton", a, UiText.OnlineJoin, 28, out _);
            roomLabel = UiKit.Label(p, "RoomLabel", a, UiText.OnlineCodeLabel, 22, Theme.InkDim);
            roomCode = UiKit.Label(p, "RoomCode", a, "", 80, Theme.Gold, TextAlignmentOptions.Center, true);
            roomCode.fontStyle = FontStyles.Bold;
            roomCode.characterSpacing = 12f;
            roomCode.textWrappingMode = TextWrappingModes.NoWrap;
            copyCodeButton = UiKit.Button(p, "CopyCodeButton", a, UiText.OnlineCopyCode, 24, out _);
            copyUrlButton = UiKit.Button(p, "CopyUrlButton", a, UiText.OnlineCopyUrl, 24, out _);
            urlText = UiKit.Label(p, "InviteUrl", a, "", 18, Theme.InkDim);
            urlText.textWrappingMode = TextWrappingModes.Normal;
            shareNote = UiKit.Label(p, "ShareNote", a, UiText.OnlineShareNote, 20, Theme.Ink);
            openNote = UiKit.Label(p, "OpenNote", a, UiText.OnlineOpenNote, 16, Theme.InkDim);
            networkNote = UiKit.Label(p, "NetworkNote", a, UiText.OnlineNetworkNote, 16, new Color(0.9f, 0.78f, 0.5f));
            backButton = UiKit.Button(p, "OnlineBackButton", a, UiText.Back, 26, out _);
            ApplyLayout(false);
            ShowMode(Mode.Menu);
        }

        void Awake()
        {
            createButton.onClick.AddListener(() => CreatePressed?.Invoke());
            joinButton.onClick.AddListener(SubmitJoin);
            backButton.onClick.AddListener(() => BackPressed?.Invoke());
            copyCodeButton.onClick.AddListener(() => Copy(_code));
            copyUrlButton.onClick.AddListener(() => Copy(_inviteUrl));
            // 欄そのものは表示だけなので、タップ・クリックでブラウザの入力ダイアログを開けるようにする（スマホにはキー入力がない）
            codeBox.raycastTarget = true;
            var tap = codeBox.GetComponent<Button>();
            if (tap == null) tap = codeBox.gameObject.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(AskCode);
        }

        void AskCode()
        {
            if (CurrentMode != Mode.Menu) return;
            string entered = PromptUtil.Ask(UiText.OnlinePromptCode, _typed.ToString());
            if (string.IsNullOrEmpty(entered)) return;
            SetTyped(entered);
        }

        void OnDisable()
        {
            if (_kb != null) _kb.onTextInput -= OnChar;
            _kb = null;
        }

        void Copy(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            bool ok = ClipboardUtil.Copy(text);
            Notice(ok ? UiText.OnlineCopied : UiText.OnlineCopyFailed);
            if (Edgewise.Game.App.AutomationLog.Enabled) Edgewise.Game.App.AutomationLog.Write("online copy " + (ok ? "ok " : "fail ") + text);
        }

        void SubmitJoin() => JoinPressed?.Invoke(_typed.ToString());

        // ------------------------------------------------------------------
        // コードの入力（キーボードの文字入力）
        // ------------------------------------------------------------------

        void Update()
        {
            if (CurrentMode != Mode.Menu) return;
            var kb = Keyboard.current;
            if (kb != _kb)
            {
                if (_kb != null) _kb.onTextInput -= OnChar;
                _kb = kb;
                if (_kb != null) _kb.onTextInput += OnChar;
            }
            if (kb == null) return;
            if (kb.backspaceKey.wasPressedThisFrame && _typed.Length > 0)
            {
                _typed.Length--;
                RefreshCode();
            }
            if ((kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) && _typed.Length > 0) SubmitJoin();
        }

        void OnChar(char ch)
        {
            if (CurrentMode != Mode.Menu || !gameObject.activeInHierarchy) return;
            TypeChar(ch);
        }

        /// <summary>1 文字入力する（小文字は大文字にし、使えない文字は無視。Ctrl+V などで複数文字が来ても受ける）。</summary>
        public void TypeChar(char ch)
        {
            char u = char.ToUpperInvariant(ch);
            if (RoomCode.Alphabet.IndexOf(u) < 0 || _typed.Length >= RoomCode.Length) return;
            _typed.Append(u);
            RefreshCode();
        }

        public void SetTyped(string text)
        {
            _typed.Clear();
            foreach (char c in text ?? "") TypeChar(c);
            RefreshCode();
        }

        void RefreshCode()
        {
            if (_typed.Length == 0)
            {
                codeText.text = UiText.OnlineCodeHint;
                codeText.color = Theme.InkDim;
                return;
            }
            var sb = new StringBuilder();
            for (int i = 0; i < RoomCode.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(i < _typed.Length ? _typed[i] : '_');
            }
            codeText.text = sb.ToString();
            codeText.color = Theme.Ink;
        }

        // ------------------------------------------------------------------
        // 表示
        // ------------------------------------------------------------------

        public void ShowMenu(string message = null)
        {
            gameObject.SetActive(true);
            _code = "";
            _inviteUrl = "";
            ShowMode(Mode.Menu);
            status.text = message ?? UiText.OnlineMenuStatus;
            status.color = message != null ? new Color(1f, 0.7f, 0.6f) : Theme.InkDim;
            RefreshCode();
            EventSystem.current?.SetSelectedGameObject(createButton.gameObject);
            Log();
        }

        /// <summary>部屋の表示（ホスト）。code と招待 URL を出し、statusText に状態を出す。</summary>
        public void ShowRoom(string code, string inviteUrl, string statusText)
        {
            gameObject.SetActive(true);
            _code = code ?? "";
            _inviteUrl = inviteUrl ?? "";
            roomCode.text = _code;
            urlText.text = _inviteUrl;
            ShowMode(Mode.Room);
            SetStatus(statusText);
            EventSystem.current?.SetSelectedGameObject(copyUrlButton.gameObject);
            Log();
        }

        /// <summary>つなぎ中など、部屋のコードを出さない状態の表示（ゲスト）。</summary>
        public void ShowProgress(string statusText)
        {
            gameObject.SetActive(true);
            _code = "";
            _inviteUrl = "";
            ShowMode(Mode.Error);
            SetStatus(statusText);
            backButton.GetComponentInChildren<TextMeshProUGUI>().text = UiText.OnlineLeave;
            EventSystem.current?.SetSelectedGameObject(backButton.gameObject);
            Log();
        }

        public void ShowError(string message)
        {
            gameObject.SetActive(true);
            _code = "";
            _inviteUrl = "";
            ShowMode(Mode.Error);
            SetStatus(message, true);
            backButton.GetComponentInChildren<TextMeshProUGUI>().text = UiText.Back;
            EventSystem.current?.SetSelectedGameObject(backButton.gameObject);
            Log();
        }

        public void SetStatus(string text, bool error = false)
        {
            status.text = text ?? "";
            status.color = error ? new Color(1f, 0.7f, 0.6f) : Theme.Ink;
        }

        void Notice(string text)
        {
            shareNote.text = text;
        }

        public void Hide() => gameObject.SetActive(false);

        void Log()
        {
            if (Edgewise.Game.App.AutomationLog.Enabled)
            {
                Edgewise.Game.App.AutomationLog.Buttons("Online", transform);
                Edgewise.Game.App.AutomationLog.Write("online screen " + CurrentMode + " code=" + _code + " status=" + status.text);
            }
        }

        void ShowMode(Mode mode)
        {
            CurrentMode = mode;
            bool menu = mode == Mode.Menu, room = mode == Mode.Room;
            createButton.gameObject.SetActive(menu);
            joinLabel.gameObject.SetActive(menu);
            codeBox.gameObject.SetActive(menu);
            joinButton.gameObject.SetActive(menu);
            roomLabel.gameObject.SetActive(room);
            roomCode.gameObject.SetActive(room);
            copyCodeButton.gameObject.SetActive(room);
            copyUrlButton.gameObject.SetActive(room);
            urlText.gameObject.SetActive(room);
            shareNote.gameObject.SetActive(room);
            shareNote.text = UiText.OnlineShareNote;
            backButton.GetComponentInChildren<TextMeshProUGUI>().text = menu ? UiText.Back : UiText.OnlineLeave;
            if (menu) _typed.Clear();
        }

        public void ApplyLayout(bool portrait)
        {
            _portrait = portrait;
            var c = new Vector2(0.5f, 0.5f);
            if (!portrait)
            {
                UiKit.Place((RectTransform)panel.transform, c, Vector2.zero, new Vector2(820f, 640f));
                UiKit.Place(title.rectTransform, c, new Vector2(0f, 270f), new Vector2(700f, 56f));
                UiKit.Place(status.rectTransform, c, new Vector2(0f, 205f), new Vector2(760f, 40f));
                // メニュー
                UiKit.Place((RectTransform)createButton.transform, c, new Vector2(0f, 110f), new Vector2(440f, 84f));
                UiKit.Place(joinLabel.rectTransform, c, new Vector2(0f, 20f), new Vector2(700f, 34f));
                UiKit.Place((RectTransform)codeBox.transform, c, new Vector2(-110f, -50f), new Vector2(400f, 76f));
                UiKit.Place((RectTransform)joinButton.transform, c, new Vector2(190f, -50f), new Vector2(150f, 76f));
                // 部屋
                UiKit.Place(roomLabel.rectTransform, c, new Vector2(0f, 150f), new Vector2(400f, 32f));
                UiKit.Place(roomCode.rectTransform, c, new Vector2(0f, 88f), new Vector2(700f, 100f));
                UiKit.Place((RectTransform)copyCodeButton.transform, c, new Vector2(-190f, 10f), new Vector2(340f, 62f));
                UiKit.Place((RectTransform)copyUrlButton.transform, c, new Vector2(190f, 10f), new Vector2(340f, 62f));
                UiKit.Place(urlText.rectTransform, c, new Vector2(0f, -62f), new Vector2(740f, 54f));
                UiKit.Place(shareNote.rectTransform, c, new Vector2(0f, -122f), new Vector2(760f, 56f));
                UiKit.Place(openNote.rectTransform, c, new Vector2(0f, -190f), new Vector2(780f, 28f));
                UiKit.Place(networkNote.rectTransform, c, new Vector2(0f, -222f), new Vector2(780f, 28f));
                UiKit.Place((RectTransform)backButton.transform, c, new Vector2(0f, -274f), new Vector2(260f, 60f));
                openNote.fontSize = 16;
                networkNote.fontSize = 16;
            }
            else
            {
                // 縦（360×640 では 0.5 倍で表示）: 文字を大きく、上から積む
                UiKit.Place((RectTransform)panel.transform, c, Vector2.zero, new Vector2(680f, 1120f));
                UiKit.Place(title.rectTransform, c, new Vector2(0f, 500f), new Vector2(620f, 56f));
                UiKit.Place(status.rectTransform, c, new Vector2(0f, 430f), new Vector2(640f, 80f));
                UiKit.Place((RectTransform)createButton.transform, c, new Vector2(0f, 300f), new Vector2(520f, 110f));
                UiKit.Place(joinLabel.rectTransform, c, new Vector2(0f, 190f), new Vector2(640f, 60f));
                UiKit.Place((RectTransform)codeBox.transform, c, new Vector2(0f, 110f), new Vector2(560f, 92f));
                UiKit.Place((RectTransform)joinButton.transform, c, new Vector2(0f, 0f), new Vector2(300f, 92f));
                UiKit.Place(roomLabel.rectTransform, c, new Vector2(0f, 340f), new Vector2(400f, 36f));
                UiKit.Place(roomCode.rectTransform, c, new Vector2(0f, 270f), new Vector2(640f, 110f));
                UiKit.Place((RectTransform)copyCodeButton.transform, c, new Vector2(0f, 160f), new Vector2(460f, 84f));
                UiKit.Place((RectTransform)copyUrlButton.transform, c, new Vector2(0f, 60f), new Vector2(460f, 84f));
                UiKit.Place(urlText.rectTransform, c, new Vector2(0f, -50f), new Vector2(620f, 80f));
                UiKit.Place(shareNote.rectTransform, c, new Vector2(0f, -150f), new Vector2(620f, 110f));
                UiKit.Place(openNote.rectTransform, c, new Vector2(0f, -280f), new Vector2(620f, 80f));
                UiKit.Place(networkNote.rectTransform, c, new Vector2(0f, -380f), new Vector2(620f, 80f));
                UiKit.Place((RectTransform)backButton.transform, c, new Vector2(0f, -480f), new Vector2(340f, 84f));
                openNote.fontSize = 22;
                networkNote.fontSize = 22;
            }
            urlText.fontSize = portrait ? 22 : 18;
            shareNote.fontSize = portrait ? 24 : 20;
            status.fontSize = portrait ? 28 : 24;
            joinLabel.fontSize = portrait ? 26 : 22;
        }
    }
}
