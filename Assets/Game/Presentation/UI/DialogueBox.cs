using System.Collections.Generic;
using System.Threading;
using Edgewise.Game.App;
using Edgewise.Game.Audio;
using Edgewise.Game.Campaign;
using Edgewise.Game.Tweening;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 会話（DESIGN §12-7 / §12-11）: 画面下の帯に、立ち絵・名前の札・本文。本文は 1 文字ずつ出し、タップで全文 → もう一度タップで次へ。
    /// 「スキップ」で残りを全部飛ばす。キーボードは Enter / Space で進む、Esc でスキップ。
    /// </summary>
    public sealed class DialogueBox : MonoBehaviour, IResponsiveUi
    {
        [SerializeField] Button tapArea;
        [SerializeField] Image band;
        [SerializeField] PortraitView portrait;
        [SerializeField] Image namePlate;
        [SerializeField] TextMeshProUGUI nameText;
        [SerializeField] TextMeshProUGUI body;
        [SerializeField] TextMeshProUGUI next;
        [SerializeField] Button skipButton;

        readonly Typewriter _typer = new Typewriter();
        IReadOnlyList<DialogueLine> _lines;
        int _index;
        bool _skip;
        bool _tapped;
        int _blip;

        public bool IsOpen => gameObject.activeSelf;
        public int LineIndex => _index;
        public string BodyText => body.text;
        public int VisibleCharacters => _typer.Visible;
        public bool LineComplete => _typer.IsComplete;
        public string SpeakerName => nameText.text;
        public PortraitView Portrait => portrait;
        public Button SkipButton => skipButton;
        public Button TapArea => tapArea;

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            var tapImg = UiKit.Fill(root, "DialogueTap", new Color(0f, 0f, 0.02f, 0.25f));
            tapArea = tapImg.gameObject.AddComponent<Button>();
            tapArea.targetGraphic = tapImg;
            tapArea.transition = Selectable.Transition.None;
            var nav = tapArea.navigation;
            nav.mode = Navigation.Mode.None;
            tapArea.navigation = nav;
            band = UiKit.Panel(root, "Band", a, new Color(0.04f, 0.05f, 0.12f, 0.95f), false);
            portrait = PortraitView.Create(band.transform, "Portrait", a);
            namePlate = UiKit.Panel(band.transform, "NamePlate", a, new Color(0.16f, 0.13f, 0.06f, 0.98f), false);
            nameText = UiKit.Label(namePlate.transform, "Name", a, "", 24, Theme.Gold, TextAlignmentOptions.Center, true);
            UiKit.Stretch(nameText.rectTransform, 12, 2, 12, 2);
            nameText.textWrappingMode = TextWrappingModes.NoWrap;
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 12;
            body = UiKit.Label(band.transform, "Body", a, "", 28, Theme.Ink, TextAlignmentOptions.TopLeft);
            next = UiKit.Label(band.transform, "Next", a, UiText.DialogueNext, 24, Theme.Gold);
            skipButton = UiKit.Button(band.transform, "SkipButton", a, UiText.DialogueSkip, 20, out _);
            ApplyLayout(false);
            gameObject.SetActive(false);
        }

        void Awake()
        {
            tapArea.onClick.AddListener(() => _tapped = true);
            skipButton.onClick.AddListener(Skip);
        }

        public void ApplyLayout(bool portraitMode)
        {
            float w = portraitMode ? 700f : 1220f;
            float h = portraitMode ? 330f : 210f;
            UiKit.Place((RectTransform)band.transform, new Vector2(0.5f, 0f), new Vector2(0f, portraitMode ? 20f : 18f), new Vector2(w, h));
            var tl = new Vector2(0f, 1f);
            float ps = portraitMode ? 132f : 150f;
            if (portraitMode)
            {
                UiKit.Place((RectTransform)portrait.transform, tl, tl, new Vector2(20f, -46f), new Vector2(ps, ps));
                UiKit.Place((RectTransform)namePlate.transform, tl, new Vector2(0f, 0.5f), new Vector2(22f, 4f), new Vector2(330f, 52f));
                UiKit.Place(body.rectTransform, tl, tl, new Vector2(172f, -44f), new Vector2(w - 196f, h - 120f));
                body.fontSize = 32;
                nameText.fontSizeMax = 28;
                UiKit.Place((RectTransform)skipButton.transform, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-18f, 4f), new Vector2(170f, 64f));
                skipButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 26;
                UiKit.Place(next.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-26f, 16f), new Vector2(40f, 40f));
                next.fontSize = 30;
            }
            else
            {
                UiKit.Place((RectTransform)portrait.transform, tl, tl, new Vector2(26f, -30f), new Vector2(ps, ps));
                UiKit.Place((RectTransform)namePlate.transform, tl, new Vector2(0f, 0.5f), new Vector2(200f, 2f), new Vector2(360f, 46f));
                UiKit.Place(body.rectTransform, tl, tl, new Vector2(204f, -40f), new Vector2(w - 330f, h - 64f));
                body.fontSize = 28;
                nameText.fontSizeMax = 24;
                UiKit.Place((RectTransform)skipButton.transform, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-20f, 2f), new Vector2(150f, 50f));
                skipButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 20;
                UiKit.Place(next.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-30f, 16f), new Vector2(36f, 36f));
                next.fontSize = 26;
            }
        }

        /// <summary>残りを全部飛ばす（スキップボタン・Esc・テスト）。</summary>
        public void Skip() => _skip = true;

        /// <summary>タップと同じ（全文 → 次の行）。</summary>
        public void Advance() => _tapped = true;

        /// <summary>行を順に出し、最後の行を読み終えるか、スキップされたら返る。</summary>
        public async Awaitable PlayAsync(IReadOnlyList<DialogueLine> lines, float timeFactor, CancellationToken ct)
        {
            _lines = lines;
            _skip = false;
            if (lines == null || lines.Count == 0) return;
            gameObject.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(null);
            var bandRt = (RectTransform)band.transform;
            try
            {
                // 帯が下から少しせり上がって出る
                await Tweens.Run(0.2f * timeFactor, k =>
                {
                    bandRt.localScale = new Vector3(1f, Mathf.Lerp(0.6f, 1f, k), 1f);
                    band.color = new Color(0.04f, 0.05f, 0.12f, 0.95f * k);
                }, ct, Ease.OutCubic);
                for (_index = 0; _index < lines.Count && !_skip; _index++)
                {
                    var line = lines[_index];
                    portrait.Show(line.Speaker);
                    nameText.text = line.Speaker != null ? line.Speaker.Title + "  " + line.Speaker.Name : "";
                    body.text = line.Text;
                    _typer.Reset(line.Text);
                    body.maxVisibleCharacters = 0;
                    next.gameObject.SetActive(false);
                    _tapped = false;
                    _blip = 0;
                    if (AutomationLog.Enabled)
                    {
                        AutomationLog.Write("dialogue " + _index + "/" + lines.Count + " " + (line.Speaker?.Id ?? "-"));
                        if (_index == 0) AutomationLog.Buttons("Dialogue", transform);
                    }
                    // 1 文字ずつ
                    while (!_typer.IsComplete && !_skip)
                    {
                        if (ReadKeys() || _tapped)
                        {
                            _tapped = false;
                            _typer.Complete();
                        }
                        else
                        {
                            int added = _typer.Advance(Time.unscaledDeltaTime, timeFactor);
                            _blip += added;
                            if (_blip >= 3 && timeFactor > 0f)
                            {
                                _blip = 0;
                                SfxBank.Instance?.Play(SfxKind.Talk, Random.Range(-1f, 1f));
                            }
                        }
                        body.maxVisibleCharacters = _typer.Visible;
                        if (_typer.IsComplete) break;
                        await Awaitable.NextFrameAsync(ct);
                    }
                    body.maxVisibleCharacters = _typer.Length;
                    if (_skip) break;
                    // 次へを待つ（▼ が弾む）
                    next.gameObject.SetActive(true);
                    _tapped = false;
                    await Awaitable.NextFrameAsync(ct);
                    while (!_tapped && !_skip && !ReadKeys())
                    {
                        next.rectTransform.anchoredPosition = new Vector2(next.rectTransform.anchoredPosition.x, 16f + Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f)) * 6f);
                        await Awaitable.NextFrameAsync(ct);
                    }
                    SfxBank.Instance?.Play(SfxKind.Cursor);
                }
            }
            finally
            {
                if (this != null) gameObject.SetActive(false);
                if (AutomationLog.Enabled) AutomationLog.Write("dialogue end" + (_skip ? " skipped" : ""));
            }
        }

        bool ReadKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || SettingsPanel.AnyOpen) return false;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                _skip = true;
                return true;
            }
            return kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.zKey.wasPressedThisFrame;
        }
    }
}
