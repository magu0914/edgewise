using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Edgewise.Core;
using Edgewise.Game.Controls;
using Edgewise.Game.Match;
using Edgewise.Game.View;
using UnityEngine;
using UnityEngine.UI;

namespace Edgewise.Game.App
{
    /// <summary>
    /// 自動操作（ヘッドレスブラウザの E2E）向けのログ。URL に ?log を付けたときだけ出す。
    /// 画面上の位置はブラウザの座標（左上原点・CSS ピクセル。devicePixelRatio = 1 を想定）で出す。
    /// 例: "[EW] ui Title/StartButton 490 400 300 78"、"[EW] hand 0 220 180"、"[EW] cell 4 640 380 empty"
    /// </summary>
    public static class AutomationLog
    {
        public static bool Enabled;

        public static void Write(string line) => Debug.Log("[EW] " + line);

        static string F(float v) => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);

        /// <summary>root 以下の有効なボタンの位置を出す。</summary>
        public static void Buttons(string screen, Transform root)
        {
            if (!Enabled) return;
            Write("screen " + screen + " " + Screen.width + "x" + Screen.height);
            var corners = new Vector3[4];
            foreach (var b in root.GetComponentsInChildren<Button>(false))
            {
                var rt = (RectTransform)b.transform;
                rt.GetWorldCorners(corners);
                var min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
                var max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
                Write("ui " + screen + "/" + b.name + " " + F(min.x) + " " + F(Screen.height - max.y) + " " + F(max.x - min.x) + " "
                    + F(max.y - min.y));
            }
        }

        public static void AwaitingMove(PlayerController pc, HandView hand, BoardView board, TableLayout layout)
        {
            var state = pc.State;
            var sb = new StringBuilder();
            Write("await turn=" + (int)state.Turn + " hand=" + hand.Cards.Count + " pivot=" + (state.Rules.Pivot ? state.GetPivotTokens(state.Turn) : 0));
            for (int i = 0; i < hand.Cards.Count; i++)
            {
                var s = layout.TableToScreen(hand.SlotPosition(i));
                // 重なった列では上の方（数字の見える側）をつかむ
                var top = layout.TableToScreen(hand.SlotPosition(i) + new Vector3(0f, CardView.Size.y * hand.CardScale * 0.3f, 0f));
                var p = hand.Vertical ? top : s;
                Write("hand " + i + " " + F(p.x) + " " + F(Screen.height - p.y) + " " + hand.Cards[i].CardId);
            }
            for (int c = 0; c < 9; c++)
            {
                var s = layout.TableToScreen(board.CellPosition(c));
                Write("cell " + c + " " + F(s.x) + " " + F(Screen.height - s.y) + (state.Board[c].HasValue ? " full" : " empty"));
            }
            // 各手の予告（rotation 0）。E2E で Same / Plus が出る手を探すのに使う
            var opts = new List<string>();
            foreach (var m in GameEngine.LegalMoves(state))
            {
                if (m.Rotation != 0) continue;
                var pv = MovePreview.Compute(state, m);
                opts.Add(m.HandIndex + ":" + m.Cell + ":" + pv.FlipCells.Count + (pv.Same || pv.SameWall ? "S" : "") + (pv.Plus ? "P" : ""));
            }
            Write("options " + string.Join(" ", opts));
        }
    }

    /// <summary>対戦中の FPS を計測してログに出す（?log のとき）。</summary>
    public sealed class FpsMeter : MonoBehaviour
    {
        float _acc;
        int _frames;
        float _worst;
        float _totalTime;
        int _totalFrames;
        float _totalWorst;

        public bool Measuring { get; set; }

        public float AverageFps => _totalTime > 0 ? _totalFrames / _totalTime : 0f;
        public float WorstFrameMs => _totalWorst * 1000f;

        void Update()
        {
            if (!Measuring) return;
            float dt = Time.unscaledDeltaTime;
            _acc += dt;
            _frames++;
            _totalTime += dt;
            _totalFrames++;
            if (dt > _worst) _worst = dt;
            if (dt > _totalWorst && _totalFrames > 10) _totalWorst = dt;
            if (_acc >= 2f)
            {
                if (AutomationLog.Enabled)
                    AutomationLog.Write("fps " + (_frames / _acc).ToString("0.0", CultureInfo.InvariantCulture) + " worst "
                        + (_worst * 1000f).ToString("0", CultureInfo.InvariantCulture) + "ms avg " + AverageFps.ToString("0.0", CultureInfo.InvariantCulture));
                _acc = 0;
                _frames = 0;
                _worst = 0;
            }
        }
    }
}
