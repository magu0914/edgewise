using System;
using System.Collections.Generic;
using System.Threading;
using Edgewise.Core;
using Edgewise.Core.Data;
using Edgewise.Game.AI;
using Edgewise.Game.App;
using Edgewise.Game.Controls;
using Edgewise.Game.Juice;
using Edgewise.Game.Tweening;
using Edgewise.Game.UI;
using Edgewise.Game.View;
using UnityEngine;

namespace Edgewise.Game.Match
{
    /// <summary>対戦の結果（席 0 から見た勝敗）。</summary>
    public sealed class MatchOutcome
    {
        public (int P0, int P1) Score;
        public GameResult Result;
        public bool Resigned;
        public int Moves;
    }

    /// <summary>
    /// 対戦の進行役（DESIGN §8-1 アーキテクチャ）。GameState を持つ唯一の場所。
    /// 手番ごとにプレイヤー（PlayerController）か CPU（AiDriver）から Move を受け取り、GameEngine.ApplyMove を呼んで、
    /// 返ったイベント列を EventPlayer に渡して再生させる。
    /// </summary>
    public sealed class MatchController : MonoBehaviour
    {
        [SerializeField] GameAssets assets;
        [SerializeField] BoardView board;
        [SerializeField] HandView hand0;
        [SerializeField] HandView hand1;
        [SerializeField] HudView hud;
        [SerializeField] BannerView banner;
        [SerializeField] TableLayout layout;
        [SerializeField] PlayerController player;
        [SerializeField] Transform cardRoot;

        readonly List<CardView> _pool = new List<CardView>();
        readonly AiDriver _ai = new AiDriver();
        EventPlayer _eventPlayer;
        uint _rng;
        bool _resignRequested;

        public GameState State { get; private set; }
        public MatchSettings Settings { get; private set; }
        public EventPlayer EventPlayer => _eventPlayer;
        public PlayerController Controls => player;
        public BoardView Board => board;
        public HandView Hand(Player p) => p == Player.P0 ? hand0 : hand1;
        public bool IsRunning { get; private set; }
        public bool CpuThinking { get; private set; }
        public int MovesPlayed { get; private set; }

        public void Wire(GameAssets a, BoardView b, HandView h0, HandView h1, HudView h, BannerView bn, TableLayout l, PlayerController p, Transform cards)
        {
            assets = a;
            board = b;
            hand0 = h0;
            hand1 = h1;
            hud = h;
            banner = bn;
            layout = l;
            player = p;
            cardRoot = cards;
        }

        JuiceSettings Juice => assets.juice != null ? assets.juice : (_fallbackJuice ??= JuiceSettings.CreateDefault());
        JuiceSettings _fallbackJuice;

        static float Tf => UserPrefs.TimeFactor;

        void Awake()
        {
            player.Configure(board, hand0, layout, Juice, NullMatchFx.Instance);
            _eventPlayer = new EventPlayer(board, new[] { hand0, hand1 }, hud, banner, Juice, () => UserPrefs.TimeFactor);
        }

        /// <summary>降参する（CPU の手番なら、その手の再生後に終える）。</summary>
        public void Resign()
        {
            _resignRequested = true;
            player.Resign();
        }

        public async Awaitable<MatchOutcome> RunAsync(MatchSettings settings, CancellationToken ct)
        {
            Settings = settings;
            IsRunning = true;
            _resignRequested = false;
            MovesPlayed = 0;
            try
            {
                long seed = settings.seed != 0 ? settings.seed : (DateTime.Now.Ticks & 0x7fffffffL) | 1L;
                var rules = settings.ToRuleSet();
                var (d0, d1) = Dealer.Deal(settings.band, settings.random, seed);
                State = GameEngine.CreateGame(new GameConfig(rules, d0, d1, seed, CardCatalog.ById));
                _rng = Rng.SeedToState(seed ^ 0x2545F491L);
                if (AutomationLog.Enabled) AutomationLog.Write("match seed=" + seed + " rules=" + rules.ToBitString() + " first=" + (int)State.FirstPlayer);

                await SetupViewsAsync(ct);
                bool resigned = false;

                while (State.Phase == Phase.Playing)
                {
                    UpdateHud();
                    Move move;
                    var turn = State.Turn;
                    if (settings.Seat(turn) == SeatKind.Human)
                    {
                        hud.SetTurn(UiText.YourTurn, Theme.OwnerColor(turn));
                        var r = await player.WaitForMoveAsync(State, turn, ct);
                        if (r == null)
                        {
                            resigned = true;
                            break;
                        }
                        move = r.Value;
                    }
                    else
                    {
                        hud.SetTurn(UiText.CpuThinking, Theme.OwnerColor(turn));
                        move = await ThinkAsync(ct);
                        hud.SetTurn(UiText.CpuTurn, Theme.OwnerColor(turn));
                    }

                    var before = State;
                    var result = GameEngine.ApplyMove(before, move);
                    State = result.State;
                    MovesPlayed++;
                    await _eventPlayer.PlayAsync(before, move, result.Events, ct);
                    if (State.SuddenDeathRound != before.SuddenDeathRound) await RebuildForSuddenDeathAsync(ct);
                    SyncScores();
                    if (_resignRequested && State.Phase == Phase.Playing)
                    {
                        resigned = true;
                        break;
                    }
                }

                UpdateHud();
                var score = GameEngine.Score(State);
                var outcome = new MatchOutcome
                {
                    Score = score,
                    Result = resigned ? (Settings.seat0 == SeatKind.Human ? GameResult.P1 : GameResult.P0) : GameEngine.ResultOfScore(score),
                    Resigned = resigned,
                    Moves = MovesPlayed,
                };
                _eventPlayer.Fx.OnEnd(outcome.Result);
                if (AutomationLog.Enabled) AutomationLog.Write("end " + score.P0 + "-" + score.P1 + " " + outcome.Result.ToId() + (resigned ? " resigned" : ""));
                return outcome;
            }
            finally
            {
                IsRunning = false;
                CpuThinking = false;
            }
        }

        async Awaitable<Move> ThinkAsync(CancellationToken ct)
        {
            CpuThinking = true;
            try
            {
                long seed = Rng.NextUInt32(_rng, out _rng);
                _ai.Begin(State, State.Turn, Settings.ProfileFor(State.Turn), seed, CardCatalog.All);
                float start = Time.unscaledTime;
                while (!_ai.Tick()) await Awaitable.NextFrameAsync(ct);
                float min = Juice.cpuMinThink * Tf;
                while (Time.unscaledTime - start < min) await Awaitable.NextFrameAsync(ct);
                return _ai.BestMove;
            }
            finally
            {
                CpuThinking = false;
            }
        }

        bool FaceDown(Player p) =>
            !State.Rules.Open && Settings.Seat(p) == SeatKind.Cpu && Settings.Seat(p.Opponent()) == SeatKind.Human;

        void EnsurePool(int n)
        {
            while (_pool.Count < n)
            {
                var card = Instantiate(assets.cardPrefab, cardRoot);
                card.name = "Card" + _pool.Count;
                _pool.Add(card);
            }
        }

        async Awaitable SetupViewsAsync(CancellationToken ct)
        {
            float tf = Tf;
            EnsurePool(GameConstants.HandSize * 2);
            board.ClearPlaced();
            board.HideLinks();
            board.HideBadges();
            board.ShowCursor(-1, Color.white, false);
            board.SetCellElements(State.CellElements);
            banner.Hide();
            hand0.Cards.Clear();
            hand1.Cards.Clear();
            hand0.Held = null;
            hand1.Held = null;
            int k = 0;
            var tweens = new List<Tween>();
            for (int p = 0; p < 2; p++)
            {
                var who = (Player)p;
                var hand = Hand(who);
                var ids = State.GetHand(who);
                for (int i = 0; i < ids.Count; i++)
                {
                    var card = _pool[k++];
                    card.gameObject.SetActive(true);
                    card.Bind(State.Cards[ids[i]], who, 0, FaceDown(who));
                    card.SetModifier(0);
                    card.SetGlow(0f, Color.white);
                    card.SetSelect(0f);
                    card.SetAlpha(1f);
                    card.SetLift(0f);
                    card.SetFlash(0f);
                    hand.Cards.Add(card);
                }
            }
            for (; k < _pool.Count; k++) _pool[k].gameObject.SetActive(false);

            layout.Apply();
            for (int p = 0; p < 2; p++)
            {
                var hand = Hand((Player)p);
                for (int i = 0; i < hand.Cards.Count; i++)
                {
                    var card = hand.Cards[i];
                    var target = hand.SlotPosition(i);
                    var from = target + (layout.Portrait ? new Vector3(0f, p == 0 ? -4f : 4f, 0f) : new Vector3(p == 0 ? -4f : 4f, 0f, 0f));
                    card.transform.SetPositionAndRotation(from, Quaternion.identity);
                    card.transform.localScale = Vector3.one * hand.CardScale;
                    card.SetOrder(20 + i);
                    var c = card;
                    tweens.Add(Tweens.Play(Juice.dealTime * tf, t => c.transform.position = Vector3.LerpUnclamped(from, target, t), Ease.OutCubic,
                        (i + p * 2) * Juice.dealStagger * tf, c, "move"));
                }
            }
            hud.SetNames(Settings.seat0 == SeatKind.Human ? UiText.You : UiText.Cpu, UiText.Cpu);
            SyncScores();
            UpdateHud();
            await Tweens.AwaitAll(tweens, ct);
            hand0.Layout(false);
            hand1.Layout(false);
            bool youFirst = State.FirstPlayer == Player.P0;
            string first = Settings.seat0 == SeatKind.Human ? (youFirst ? UiText.YouFirst : UiText.CpuFirst) : "P" + (int)State.FirstPlayer + " FIRST";
            await banner.Show(first, Theme.OwnerColor(State.FirstPlayer), Juice.infoBannerTime * tf, ct, null, 0.6f);
        }

        async Awaitable RebuildForSuddenDeathAsync(CancellationToken ct)
        {
            float tf = Tf;
            var available = new List<CardView>();
            for (int c = 0; c < board.Placed.Length; c++)
            {
                if (board.Placed[c] != null) available.Add(board.Placed[c]);
            }
            available.AddRange(hand0.Cards);
            available.AddRange(hand1.Cards);
            board.ClearPlaced();
            hand0.Cards.Clear();
            hand1.Cards.Clear();
            for (int p = 0; p < 2; p++)
            {
                var who = (Player)p;
                var ids = State.GetHand(who);
                for (int i = 0; i < ids.Count; i++)
                {
                    int idx = available.FindIndex(cv => cv.CardId == ids[i]);
                    if (idx < 0) idx = 0;
                    var card = available[idx];
                    available.RemoveAt(idx);
                    card.Bind(State.Cards[ids[i]], who, 0, FaceDown(who));
                    card.SetModifier(0);
                    card.transform.rotation = Quaternion.identity;
                    Hand(who).Cards.Add(card);
                }
            }
            foreach (var extra in available) extra.gameObject.SetActive(false);
            hand0.Layout(true, tf);
            hand1.Layout(true, tf);
            await Tweens.Delay(Juice.dealTime * tf, ct);
        }

        void SyncScores()
        {
            var s = GameEngine.Score(State);
            hud.SetScores(s.P0, s.P1);
        }

        void UpdateHud()
        {
            hud.SetTokens(State.Rules.Pivot, State.GetPivotTokens(Player.P0), State.GetPivotTokens(Player.P1));
        }
    }
}
