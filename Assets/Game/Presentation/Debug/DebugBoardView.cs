using System.Text;
using Edgewise.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Edgewise.Game.Debugging
{
    /// <summary>
    /// Core が動いていることを確かめるための最小のデバッグ表示（Phase 0/1）。
    /// ランダム手同士の 1 局を流し、最終盤面とイベント列を IMGUI（OnGUI）で表示する。
    /// Space キー / 画面のボタンで次の 1 局、R キーでルールを切り替える。本番の UI は Phase 3 で作り直す。
    /// </summary>
    public sealed class DebugBoardView : MonoBehaviour
    {
        static readonly (string Name, RuleSet Rules)[] Presets =
        {
            ("Basic", new RuleSet()),
            ("Same + Plus + Combo", new RuleSet(same: true, sameWall: true, plus: true, combo: true)),
            ("Elemental + Pivot", new RuleSet(elemental: true, pivot: true)),
            ("All", new RuleSet(open: true, random: true, same: true, sameWall: true, plus: true, combo: true, elemental: true, suddenDeath: true, pivot: true)),
        };

        [SerializeField] long firstSeed = 1;

        DebugMatch _match;
        long _seed;
        int _preset;
        Vector2 _scroll;
        GUIStyle _cellStyle;
        GUIStyle _logStyle;

        public DebugMatch Match => _match;

        void Start()
        {
            _seed = firstSeed;
            Play();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.spaceKey.wasPressedThisFrame) Next();
            if (kb.rKey.wasPressedThisFrame) NextPreset();
        }

        void Next()
        {
            _seed++;
            Play();
        }

        void NextPreset()
        {
            _preset = (_preset + 1) % Presets.Length;
            Play();
        }

        void Play()
        {
            _match = new DebugMatch(Presets[_preset].Rules, _seed);
            _scroll = Vector2.zero;
        }

        void EnsureStyles()
        {
            if (_cellStyle != null) return;
            _cellStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 14, wordWrap = true };
            _logStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false };
        }

        void OnGUI()
        {
            if (_match == null) return;
            EnsureStyles();

            // 720p 基準で拡大縮小（スマホでも読めるように）
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;

            var final = _match.Final;
            var score = GameEngine.Score(final);
            GUI.Label(new Rect(16, 8, w - 32, 24),
                "EDGEWISE debug — rules: " + Presets[_preset].Name + "   seed: " + _seed + "   moves: " + _match.MoveCount
                + "   result: " + GameEngine.Result(final) + " (" + score.P0 + " - " + score.P1 + ")"
                + (final.SuddenDeathRound > 0 ? "   sudden death rounds: " + final.SuddenDeathRound : ""));

            // 盤面（3×3）
            const float cell = 120f;
            float ox = 16, oy = 40;
            for (int i = 0; i < GameConstants.BoardSize; i++)
            {
                var rect = new Rect(ox + (i % 3) * (cell + 6), oy + (i / 3) * (cell + 6), cell, cell);
                var pc = final.Board[i];
                var element = final.CellElements[i];
                var prevBg = GUI.backgroundColor;
                var prevText = GUI.contentColor;
                var tint = !pc.HasValue ? Color.gray : pc.Value.Owner == Player.P0 ? new Color(0.45f, 0.7f, 1f) : new Color(1f, 0.55f, 0.5f);
                GUI.backgroundColor = tint;
                GUI.contentColor = tint;
                GUI.Box(rect, CellText(pc, element), _cellStyle);
                GUI.backgroundColor = prevBg;
                GUI.contentColor = prevText;
            }

            if (GUI.Button(new Rect(ox, oy + 3 * (cell + 6) + 8, 180, 36), "Next game (Space)")) Next();
            if (GUI.Button(new Rect(ox + 190, oy + 3 * (cell + 6) + 8, 180, 36), "Rules (R)")) NextPreset();

            // イベント列
            float lx = ox + 3 * (cell + 6) + 24;
            var area = new Rect(lx, oy, w - lx - 16, h - oy - 16);
            float lineH = 16f;
            var content = new Rect(0, 0, 1400, _match.Log.Count * lineH + 8);
            _scroll = GUI.BeginScrollView(area, _scroll, content);
            for (int i = 0; i < _match.Log.Count; i++) GUI.Label(new Rect(0, i * lineH, content.width, lineH + 2), _match.Log[i], _logStyle);
            GUI.EndScrollView();
        }

        static string CellText(PlacedCard? pc, Element? element)
        {
            var sb = new StringBuilder();
            if (pc.HasValue)
            {
                var p = pc.Value;
                var sides = Board.RotateSides(p.Card.Sides, p.Rotation);
                // 所有者は色だけでなく記号でも区別する（既定フォントにない記号は使わない）
                sb.Append(p.Owner == Player.P0 ? "[P0]" : "(P1)").Append('\n');
                sb.Append(p.CardId).Append('\n');
                sb.Append("  ").Append(Rank(sides.Top)).Append('\n');
                sb.Append(Rank(sides.Left)).Append("   ").Append(Rank(sides.Right)).Append('\n');
                sb.Append("  ").Append(Rank(sides.Bottom));
            }
            if (element.HasValue) sb.Append(pc.HasValue ? "\n" : "").Append('[').Append(element.Value.ToId()).Append(']');
            return sb.ToString();
        }

        static string Rank(int v) => v == 10 ? "A" : v.ToString();
    }
}
