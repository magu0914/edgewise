using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Edgewise.Core;
using Edgewise.Core.AI;
using Edgewise.Core.Data;
using Edgewise.Game.AI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Edgewise.Game.Debugging
{
    /// <summary>
    /// Core と CPU が動いていることを確かめるデバッグ表示（Phase 2）。本番の UI は Phase 3 で作り直す。
    /// CPU 対 CPU を 1 手ずつ進め、各手の思考時間・ノード数・秒間ノード数・FPS を IMGUI（OnGUI）で出す。
    /// 思考は AiDriver が 1 フレームあたりのミリ秒予算で少しずつ進める（WebGL ではスレッドが使えないため）。
    ///
    /// 操作: Space = 次の 1 手 / A = 自動で進める / N = 次の対局 / R = ルール / 1・2 = P0・P1 の難易度 /
    ///       [ ] = 1 フレームの思考予算 / B = 計測（URL に ?bench を付けて開いても自動で始まる）
    /// </summary>
    public sealed class DebugBoardView : MonoBehaviour
    {
        static readonly (string Name, RuleSet Rules)[] Presets =
        {
            ("Basic (Open)", new RuleSet(open: true)),
            ("Same+Plus+Combo (Open)", new RuleSet(open: true, same: true, sameWall: true, plus: true, combo: true)),
            ("All incl. Pivot (Open)", new RuleSet(open: true, random: true, same: true, sameWall: true, plus: true, combo: true, elemental: true, suddenDeath: true, pivot: true)),
            ("Basic (Open OFF)", new RuleSet()),
            ("All incl. Pivot (Open OFF)", new RuleSet(random: true, same: true, sameWall: true, plus: true, combo: true, elemental: true, suddenDeath: true, pivot: true)),
        };

        static readonly AiProfile[] Profiles = { AiProfile.Easy, AiProfile.Normal, AiProfile.Hard };

        [SerializeField] long firstSeed = 1;

        readonly AiDriver _driver = new AiDriver();
        readonly List<string> _benchLog = new List<string>();
        CpuMatch _match;
        long _seed;
        int _preset;
        readonly int[] _profileIndex = { 2, 0 };
        bool _auto;
        bool _stepRequested;
        double _thinkMaxDt;
        bool _thinking;
        bool _prevFrameTicked;

        /// <summary>思考を進めたフレームのうち最も長かったフレーム時間（秒）。計測で使う（外からリセットする）。</summary>
        internal double WorstThinkingFrameDt;

        /// <summary>思考を進めたフレームの数と、そのうち 30 FPS を下回った（33.3 ms を超えた）フレームの数。</summary>
        internal int ThinkingFrames, SlowThinkingFrames;
        float _fpsSmoothed = 60f;
        Vector2 _scroll;
        GUIStyle _cellStyle;
        GUIStyle _logStyle;
        DebugBench _bench;

        public CpuMatch Match => _match;

        void Start()
        {
            _seed = firstSeed;
            NewMatch();
            if (Application.absoluteURL != null && Application.absoluteURL.Contains("bench")) StartBench();
        }

        void StartBench()
        {
            _bench = new DebugBench(this);
            _auto = false;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0) _fpsSmoothed = Mathf.Lerp(_fpsSmoothed, 1f / dt, 0.1f);

            var kb = Keyboard.current;
            if (kb != null && _bench == null)
            {
                if (kb.spaceKey.wasPressedThisFrame) _stepRequested = true;
                if (kb.aKey.wasPressedThisFrame) _auto = !_auto;
                if (kb.nKey.wasPressedThisFrame) NextMatch();
                if (kb.rKey.wasPressedThisFrame) NextPreset();
                if (kb.digit1Key.wasPressedThisFrame) CycleProfile(0);
                if (kb.digit2Key.wasPressedThisFrame) CycleProfile(1);
                if (kb.leftBracketKey.wasPressedThisFrame) ChangeBudget(-1);
                if (kb.rightBracketKey.wasPressedThisFrame) ChangeBudget(+1);
                if (kb.bKey.wasPressedThisFrame) StartBench();
            }

            if (_bench != null)
            {
                if (_bench.Update()) _bench = null;
                return;
            }
            Advance(dt);
        }

        /// <summary>1 フレーム分進める（思考中なら思考、思考が終わったら手を指す）。計測からも使う。</summary>
        internal MoveRecord Advance(float frameDt)
        {
            // 直前のフレームで思考を進めていたなら、そのフレームの長さ（今フレームの deltaTime）が思考の影響を受けたフレーム時間
            if (_prevFrameTicked)
            {
                if (frameDt > _thinkMaxDt) _thinkMaxDt = frameDt;
                if (frameDt > WorstThinkingFrameDt) WorstThinkingFrameDt = frameDt;
                ThinkingFrames++;
                if (frameDt > 1.0 / 30.0) SlowThinkingFrames++;
            }
            _prevFrameTicked = false;
            if (_match == null || _match.IsOver) return null;
            if (_thinking)
            {
                _prevFrameTicked = true;
                if (_driver.Tick())
                {
                    _thinking = false;
                    double minFps = _thinkMaxDt > 0 ? 1.0 / _thinkMaxDt : 0;
                    var rec = _match.ApplyThought(_driver, minFps);
                    rec.Rules = Presets[_preset].Name;
                    Debug.Log("[Edgewise] " + rec);
                    _scroll.y = float.MaxValue;
                    return rec;
                }
                return null;
            }
            if (_auto || _stepRequested)
            {
                _stepRequested = false;
                _thinkMaxDt = 0;
                _match.BeginThinking(_driver);
                _thinking = true;
            }
            return null;
        }

        internal void NewMatch()
        {
            _match = new CpuMatch(Presets[_preset].Rules, _seed, Profiles[_profileIndex[0]], Profiles[_profileIndex[1]]);
            _stepRequested = false;
            _thinking = false;
            _scroll = Vector2.zero;
        }

        internal void SetUp(int preset, int profile0, int profile1, long seed, bool auto)
        {
            _preset = preset;
            _profileIndex[0] = profile0;
            _profileIndex[1] = profile1;
            _seed = seed;
            _auto = auto;
            NewMatch();
        }

        internal AiDriver Driver => _driver;

        internal void AddBenchLine(string line)
        {
            _benchLog.Add(line);
            Debug.Log("[Edgewise] " + line);
        }

        internal static int PresetCount => Presets.Length;

        internal static string PresetName(int i) => Presets[i].Name;

        void NextMatch()
        {
            _seed++;
            NewMatch();
        }

        void NextPreset()
        {
            _preset = (_preset + 1) % Presets.Length;
            NewMatch();
        }

        void CycleProfile(int player)
        {
            _profileIndex[player] = (_profileIndex[player] + 1) % Profiles.Length;
            _match.Profiles[player] = Profiles[_profileIndex[player]];
        }

        void ChangeBudget(int delta)
        {
            double b = _driver.FrameBudgetMs + delta;
            _driver.FrameBudgetMs = b < 1 ? 1 : b > 30 ? 30 : b;
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

            var state = _match.State;
            var score = GameEngine.Score(state);
            string status = _match.IsOver
                ? "result: " + GameEngine.Result(state) + " (" + score.P0 + " - " + score.P1 + ")"
                : _thinking
                    ? "P" + (int)state.Turn + " thinking... " + _driver.ThinkMs.ToString("0") + " ms, " + _driver.Nodes + " nodes, world "
                      + (_driver.Session.WorldsDone + 1) + "/" + _driver.Session.WorldCount + ", depth " + _driver.Session.CurrentDepth
                    : "P" + (int)state.Turn + " to move (" + score.P0 + " - " + score.P1 + ")";
            GUI.Label(new Rect(16, 6, w - 32, 22),
                "EDGEWISE CPU debug — " + Presets[_preset].Name + "   seed " + _seed
                + "   P0 " + _match.Profiles[0].Name + " vs P1 " + _match.Profiles[1].Name
                + (state.SuddenDeathRound > 0 ? "   sudden death " + state.SuddenDeathRound : ""));
            GUI.Label(new Rect(16, 26, w - 32, 22),
                status + "   |   FPS " + _fpsSmoothed.ToString("0") + "   budget " + _driver.FrameBudgetMs.ToString("0") + " ms/frame"
                + "   calib " + (_driver.NodesPerMs * 1000 / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + " M nodes/s"
                + (_auto ? "   [AUTO]" : "") + (_bench != null ? "   [BENCH]" : ""));

            // 盤面（3×3）
            const float cell = 112f;
            float ox = 16, oy = 54;
            for (int i = 0; i < GameConstants.BoardSize; i++)
            {
                var rect = new Rect(ox + (i % 3) * (cell + 6), oy + (i / 3) * (cell + 6), cell, cell);
                var pc = state.Board[i];
                var element = state.CellElements[i];
                var prevBg = GUI.backgroundColor;
                var prevText = GUI.contentColor;
                var tint = !pc.HasValue ? Color.gray : pc.Value.Owner == Player.P0 ? new Color(0.45f, 0.7f, 1f) : new Color(1f, 0.55f, 0.5f);
                GUI.backgroundColor = tint;
                GUI.contentColor = tint;
                GUI.Box(rect, CellText(pc, element), _cellStyle);
                GUI.backgroundColor = prevBg;
                GUI.contentColor = prevText;
            }

            float by = oy + 3 * (cell + 6) + 6;
            GUI.Label(new Rect(ox, by, 3 * (cell + 6), 40),
                "P0 hand: " + string.Join(" ", GameEngine.VisibleHand(state, Player.P0, Player.P0)) + "  tokens " + state.GetPivotTokens(Player.P0)
                + "\nP1 hand: " + string.Join(" ", GameEngine.VisibleHand(state, Player.P1, Player.P1)) + "  tokens " + state.GetPivotTokens(Player.P1),
                _logStyle);
            by += 42;
            float bw = 172, bh = 30;
            if (GUI.Button(new Rect(ox, by, bw, bh), "Next move (Space)")) _stepRequested = true;
            if (GUI.Button(new Rect(ox + bw + 6, by, bw, bh), _auto ? "Auto: ON (A)" : "Auto: OFF (A)")) _auto = !_auto;
            by += bh + 6;
            if (GUI.Button(new Rect(ox, by, bw, bh), "New game (N)")) NextMatch();
            if (GUI.Button(new Rect(ox + bw + 6, by, bw, bh), "Rules (R)")) NextPreset();
            by += bh + 6;
            if (GUI.Button(new Rect(ox, by, bw, bh), "P0: " + _match.Profiles[0].Name + " (1)")) CycleProfile(0);
            if (GUI.Button(new Rect(ox + bw + 6, by, bw, bh), "P1: " + _match.Profiles[1].Name + " (2)")) CycleProfile(1);
            by += bh + 6;
            if (GUI.Button(new Rect(ox, by, bw / 2 - 3, bh), "- ms ([)")) ChangeBudget(-1);
            if (GUI.Button(new Rect(ox + bw / 2 + 3, by, bw / 2 - 3, bh), "+ ms (])")) ChangeBudget(+1);
            if (GUI.Button(new Rect(ox + bw + 6, by, bw, bh), "Benchmark (B)") && _bench == null) StartBench();

            // 各手の記録・計測ログ
            float lx = ox + 3 * (cell + 6) + 20;
            var area = new Rect(lx, oy, w - lx - 12, h - oy - 12);
            var lines = new List<string>();
            foreach (var r in _match.Records) lines.Add(r.ToString());
            if (_benchLog.Count > 0)
            {
                lines.Add("");
                lines.Add("---- benchmark ----");
                lines.AddRange(_benchLog);
            }
            float lineH = 16f;
            var content = new Rect(0, 0, 1500, lines.Count * lineH + 8);
            _scroll = GUI.BeginScrollView(area, _scroll, content);
            for (int i = 0; i < lines.Count; i++) GUI.Label(new Rect(0, i * lineH, content.width, lineH + 2), lines[i], _logStyle);
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
