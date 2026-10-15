using System;
using System.Threading;
using Edgewise.Core;
using Edgewise.Game.Juice;
using Edgewise.Game.Tweening;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>結果: スコアのカウントアップ → WIN / LOSE / DRAW →「もう一度」「設定を変える」。</summary>
    public sealed class ResultScreen : MonoBehaviour, IResponsiveUi
    {
        [SerializeField] Image dim;
        [SerializeField] Image panel;
        [SerializeField] TextMeshProUGUI resultText;
        [SerializeField] TextMeshProUGUI score0;
        [SerializeField] TextMeshProUGUI score1;
        [SerializeField] TextMeshProUGUI dash;
        [SerializeField] TextMeshProUGUI note;
        [SerializeField] Button againButton;
        [SerializeField] Button settingsButton;

        public event Action AgainPressed;
        public event Action SettingsPressed;

        public bool IsShown => gameObject.activeSelf;
        public string ResultLabel => resultText != null ? resultText.text : "";
        public Button AgainButton => againButton;
        public Button SettingsButton => settingsButton;

        public void Build(GameAssets a)
        {
            var root = (RectTransform)transform;
            UiKit.Stretch(root);
            for (int i = root.childCount - 1; i >= 0; i--) DestroyImmediate(root.GetChild(i).gameObject);
            dim = UiKit.Fill(root, "Dim", new Color(0f, 0f, 0.02f, 0.5f));
            panel = UiKit.Panel(root, "Panel", a, Theme.Panel);
            resultText = UiKit.Label(panel.transform, "Result", a, "", 110, Theme.Gold, TextAlignmentOptions.Center, true);
            resultText.fontStyle = FontStyles.Bold;
            resultText.textWrappingMode = TextWrappingModes.NoWrap;
            score0 = UiKit.Label(panel.transform, "Score0", a, "0", 72, Theme.P0, TextAlignmentOptions.Center, true);
            score1 = UiKit.Label(panel.transform, "Score1", a, "0", 72, Theme.P1, TextAlignmentOptions.Center, true);
            dash = UiKit.Label(panel.transform, "Dash", a, "−", 56, Theme.InkDim);
            note = UiKit.Label(panel.transform, "Note", a, "", 22, Theme.InkDim);
            againButton = UiKit.Button(panel.transform, "AgainButton", a, UiText.Again, 28, out _, new Color(0.25f, 0.42f, 0.40f));
            settingsButton = UiKit.Button(panel.transform, "SettingsButton", a, UiText.ChangeSettings, 24, out _);
            ApplyLayout(false);
        }

        void Awake()
        {
            againButton.onClick.AddListener(() => AgainPressed?.Invoke());
            settingsButton.onClick.AddListener(() => SettingsPressed?.Invoke());
        }

        public void ApplyLayout(bool portrait)
        {
            var c = new Vector2(0.5f, 0.5f);
            UiKit.Place((RectTransform)panel.transform, c, new Vector2(0f, portrait ? 0f : 0f), portrait ? new Vector2(620f, 560f) : new Vector2(620f, 470f));
            float top = portrait ? 200f : 150f;
            UiKit.Place(resultText.rectTransform, c, new Vector2(0f, top), new Vector2(600f, 140f));
            UiKit.Place(score0.rectTransform, c, new Vector2(-120f, top - 130f), new Vector2(160f, 100f));
            UiKit.Place(dash.rectTransform, c, new Vector2(0f, top - 130f), new Vector2(60f, 100f));
            UiKit.Place(score1.rectTransform, c, new Vector2(120f, top - 130f), new Vector2(160f, 100f));
            UiKit.Place(note.rectTransform, c, new Vector2(0f, top - 200f), new Vector2(560f, 34f));
            if (portrait)
            {
                UiKit.Place((RectTransform)againButton.transform, c, new Vector2(0f, -110f), new Vector2(360f, 84f));
                UiKit.Place((RectTransform)settingsButton.transform, c, new Vector2(0f, -210f), new Vector2(360f, 70f));
            }
            else
            {
                UiKit.Place((RectTransform)againButton.transform, c, new Vector2(-135f, -170f), new Vector2(250f, 76f));
                UiKit.Place((RectTransform)settingsButton.transform, c, new Vector2(135f, -170f), new Vector2(250f, 76f));
            }
        }

        /// <summary>結果を出す。席 0（プレイヤー）から見た勝敗。</summary>
        public async Awaitable Show((int P0, int P1) score, GameResult result, bool resigned, JuiceSettings juice, float timeFactor,
            CancellationToken ct)
        {
            gameObject.SetActive(true);
            resultText.text = "";
            score0.text = "0";
            score1.text = "0";
            note.text = resigned ? UiText.Resigned : "";
            againButton.gameObject.SetActive(false);
            settingsButton.gameObject.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(null);

            // スコアのカウントアップ
            int max = Mathf.Max(score.P0, score.P1);
            for (int i = 1; i <= max; i++)
            {
                if (i <= score.P0) score0.text = i.ToString();
                if (i <= score.P1) score1.text = i.ToString();
                await Tweens.Delay(juice.resultCountStep * timeFactor, ct);
            }
            score0.text = score.P0.ToString();
            score1.text = score.P1.ToString();

            string label = result == GameResult.P0 ? UiText.Win : result == GameResult.P1 ? UiText.Lose : UiText.Draw;
            resultText.text = label;
            resultText.color = result == GameResult.P0 ? Theme.Gold : result == GameResult.P1 ? new Color(0.75f, 0.55f, 0.65f) : Theme.Ink;
            var rt = resultText.rectTransform;
            await Tweens.Run(juice.resultTextTime * timeFactor, k => rt.localScale = Vector3.one * Mathf.LerpUnclamped(0.3f, 1f, k), ct,
                Ease.OutBack);
            againButton.gameObject.SetActive(true);
            settingsButton.gameObject.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(againButton.gameObject);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
