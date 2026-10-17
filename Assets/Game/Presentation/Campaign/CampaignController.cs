using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Edgewise.Core;
using Edgewise.Core.Campaign;
using Edgewise.Core.Data;
using Edgewise.Game.App;
using Edgewise.Game.Audio;
using Edgewise.Game.Juice;
using Edgewise.Game.Match;
using Edgewise.Game.UI;
using Edgewise.Game.View;
using UnityEngine;
using UnityEngine.EventSystems;
using CampaignOutcome = Edgewise.Core.Campaign.MatchOutcome;

namespace Edgewise.Game.Campaign
{
    /// <summary>
    /// 星図巡り（DESIGN §12）の画面をつなぐ役。段階の判断は CampaignFlow（純粋な状態遷移）が持ち、ここは段階ごとに画面を出して
    /// 入力（ボタン・演出の終わり）を CampaignFlow に伝えるだけ。セーブの読み書きは CampaignSession（置き場は ICampaignStore）。
    /// 対戦は既存の MatchController（星域のルール・相手の AI・CardPoolModel.Shared）と結果画面（「次へ」だけの星図巡りの形）を使う。
    /// </summary>
    public sealed class CampaignController : MonoBehaviour, ICampaignSettings
    {
        [SerializeField] AppFlow app;
        [SerializeField] StarMapScreen map;
        [SerializeField] RegionScreen region;
        [SerializeField] DeckEditScreen deck;
        [SerializeField] DialogueBox dialogue;
        [SerializeField] RewardScreen reward;
        [SerializeField] ClearedBand cleared;
        [SerializeField] EndingScreen ending;
        [SerializeField] EndRollScreen endRoll;

        CampaignSession _session;
        CampaignFlow _flow;
        CancellationTokenSource _cts;
        Edgewise.Game.Match.MatchOutcome _matchOutcome;
        string _notice;
        string[] _grants;
        bool _unlockAll;
        bool _initialized;
        Coroutine _prewarm;

        /// <summary>
        /// テスト用: 対戦をせずに結果を決める（null なら実際に対戦する）。勝敗の注入に使う。
        /// </summary>
        public Func<CampaignFlow, CampaignOutcome?> MatchOverride { get; set; }

        /// <summary>テスト用: 新しく始めるときの seed（0 なら URL の ?seed か時刻）。</summary>
        public static long TestSeed;

        public CampaignSession Session => _session;
        public CampaignFlow Flow => _flow;
        public CampaignStep Step => _flow != null ? _flow.Step : CampaignStep.Closed;
        public StarMapScreen Map => map;
        public RegionScreen Region => region;
        public DeckEditScreen Deck => deck;
        public DialogueBox Dialogue => dialogue;
        public RewardScreen Reward => reward;
        public ClearedBand Cleared => cleared;
        public EndingScreen Ending => ending;

        /// <summary>エンディングの後のエンドロール（8a）。</summary>
        public EndRollScreen EndRoll => endRoll;

        /// <summary>結果画面の「次へ」を待っているか（テスト用）。</summary>
        public bool ResultWaiting { get; private set; }

        /// <summary>今の段階の演出（会話・対戦・報酬など）が動いているか。</summary>
        public bool Busy { get; private set; }

        public void Wire(AppFlow flow, StarMapScreen m, RegionScreen r, DeckEditScreen d, DialogueBox dlg, RewardScreen rw, ClearedBand band,
            EndingScreen end, EndRollScreen roll)
        {
            app = flow;
            map = m;
            region = r;
            deck = d;
            dialogue = dlg;
            reward = rw;
            cleared = band;
            ending = end;
            endRoll = roll;
        }

        // ------------------------------------------------------------------
        // 準備（AppFlow.Awake から URL を読んだ後に呼ぶ）
        // ------------------------------------------------------------------

        /// <summary>
        /// セーブを読み込む。grants は URL の ?grant=（自動操作・撮影用。新しく始めたときに手持ちへ足す）。
        /// unlockAll は ?unlock=all（新しく始めたときに全星域を開く）、forceWin は ?forcewin=相手ID（その相手との対戦を勝ちとして注入する）。
        /// どちらも撮影・E2E 用で、?log と一緒のときだけ AppFlow が渡す。
        /// </summary>
        public void Initialize(string[] grants = null, bool unlockAll = false, string forceWin = null)
        {
            if (_initialized) return;
            _initialized = true;
            _grants = grants;
            _unlockAll = unlockAll;
            if (!string.IsNullOrEmpty(forceWin))
            {
                MatchOverride = f => f.Opponent != null && f.Opponent.Id == forceWin ? new CampaignOutcome(MatchResultKind.Win, false) : (CampaignOutcome?)null;
                Debug.LogWarning("[Edgewise] debug force win: " + forceWin);
            }
            _session = new CampaignSession(CampaignStores.Create(), NewSeed);
            var r = _session.Load();
            if (r.Status == SaveLoadStatus.Corrupt) _notice = UiText.SaveCorrupt;
            else if (r.Status == SaveLoadStatus.TooNew) _notice = UiText.SaveTooNew;
            if (r.Status == SaveLoadStatus.Corrupt || r.Status == SaveLoadStatus.TooNew)
                Debug.LogWarning("[Edgewise] campaign save: " + r.Status + " " + r.Message);
            if (AutomationLog.Enabled) AutomationLog.Write("campaign load " + r.Status + (_session.HasSave ? " save" : " nosave"));
            _flow = new CampaignFlow(_session);
            _flow.StepChanged += OnStepChanged;

            map.RegionSelected += reg => _flow.OpenRegion(reg);
            map.BackPressed += () => _flow.Back();
            map.DexPressed += () => app.ShowDex(AppFlow.DexFrom.Campaign);
            region.ChallengePressed += o => _flow.ChooseOpponent(o);
            region.BackPressed += () => _flow.Back();
            deck.BackPressed += () => _flow.Back();
            deck.BeginPressed += () => _flow.ConfirmDeck();
        }

        long NewSeed()
        {
            if (TestSeed != 0) return TestSeed;
            if (app != null && app.SeedOverride != 0) return app.SeedOverride;
            return (DateTime.Now.Ticks & 0x7fffffffL) | 1L;
        }

        void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        CancellationToken Token
        {
            get
            {
                if (_cts == null) _cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
                return _cts.Token;
            }
        }

        void CancelRunning()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        static float Tf => UserPrefs.TimeFactor;

        // ------------------------------------------------------------------
        // タイトルとの出入り
        // ------------------------------------------------------------------

        /// <summary>タイトルの進み具合（続きがなければ null）。</summary>
        public string TitleProgress => _session != null && _session.HasSave ? CampaignTexts.TitleProgress(_session.State) : null;

        /// <summary>タイトルの「星図巡り」。</summary>
        public void Enter()
        {
            if (_flow == null) Initialize();
            if (_flow.Step != CampaignStep.Closed) _flow.Exit();
            _flow.Enter();
        }

        /// <summary>星図巡りの画面をすべて隠す（AppFlow.HideAll から）。</summary>
        public void HideScreens()
        {
            map.Hide();
            region.Hide();
            deck.Hide();
            dialogue.gameObject.SetActive(false);
            reward.Hide();
            cleared.gameObject.SetActive(false);
            if (ending != null) ending.gameObject.SetActive(false);
            if (endRoll != null) endRoll.gameObject.SetActive(false);
        }

        /// <summary>星図から開いた図鑑を閉じた: 星図に戻る。</summary>
        public void ReturnFromDex()
        {
            if (_flow != null && _flow.Step == CampaignStep.Map) ShowMap();
            else app.ShowTitle();
        }

        /// <summary>星図巡りから出る（タイトルへ）。</summary>
        public void ExitToTitle()
        {
            CancelRunning();
            if (_flow != null && _flow.Step != CampaignStep.Closed) _flow.Exit();
            else app.ShowTitle();
        }

        // ------------------------------------------------------------------
        // ICampaignSettings（設定画面）
        // ------------------------------------------------------------------
        public bool HasSave => _session != null && _session.HasSave;

        public bool ClassicMode
        {
            get => _session != null && _session.State.ClassicMode;
            set
            {
                if (_session == null) return;
                _session.SetClassicMode(value);
                if (AutomationLog.Enabled) AutomationLog.Write("campaign classic " + (value ? 1 : 0));
            }
        }

        /// <summary>星図巡りの外か、星図・星域・デッキの画面にいるときだけ変えられる（対戦の途中で変えて結果と食い違わないように）。</summary>
        public bool CanEdit
        {
            get
            {
                if (_session == null) return false;
                var s = Step;
                return s == CampaignStep.Closed || s == CampaignStep.Map || s == CampaignStep.Region || s == CampaignStep.Deck;
            }
        }

        public void Erase()
        {
            if (_session == null || !CanEdit) return;
            if (Step != CampaignStep.Closed) _flow.Exit();
            _session.Erase();
            _notice = null;
        }

        // ------------------------------------------------------------------
        // 段階ごとの画面
        // ------------------------------------------------------------------
        void OnStepChanged(CampaignStep prev, CampaignStep next)
        {
            if (AutomationLog.Enabled) AutomationLog.Write("campaign " + prev + " -> " + next);
            switch (next)
            {
                case CampaignStep.Closed:
                    CancelRunning();
                    app.ShowTitle();
                    break;
                case CampaignStep.Intro:
                    RunIntro();
                    break;
                case CampaignStep.Map:
                    ShowMap();
                    break;
                case CampaignStep.Region:
                    app.HideAll();
                    app.PlayBgm(BgmCue.Title);
                    region.Show(_flow.State, _flow.Region, prev == CampaignStep.Deck ? _flow.Opponent : null);
                    break;
                case CampaignStep.Deck:
                    app.HideAll();
                    app.PlayBgm(BgmCue.Title);
                    deck.Show(_flow.State, _flow.Region, _flow.Opponent, _flow.Deck);
                    break;
                case CampaignStep.TalkBefore:
                    if (AutomationLog.Enabled) AutomationLog.Write("talk " + (_flow.IsRematch ? "rematch " : "first ") + _flow.Opponent.Id);
                    RunTalk(next, () => _flow.StartMatch());
                    break;
                case CampaignStep.Match:
                    RunMatch();
                    break;
                case CampaignStep.Result:
                    RunResult();
                    break;
                case CampaignStep.TalkAfter:
                    RunTalk(next, () => _flow.FinishTalkAfter());
                    break;
                case CampaignStep.Reward:
                    RunReward();
                    break;
                case CampaignStep.ClassicTake:
                    RunTake();
                    break;
                case CampaignStep.TalkClear:
                    RunTalk(next, () => _flow.FinishClearTalk());
                    break;
                case CampaignStep.Cleared:
                    RunCleared();
                    break;
                case CampaignStep.Ending:
                    RunEnding();
                    break;
                case CampaignStep.EndCredits:
                    RunEndCredits();
                    break;
            }
            if (next == CampaignStep.Map || next == CampaignStep.Region) StartPrewarm();
        }

        void ShowMap()
        {
            app.HideAll();
            app.PlayBgm(BgmCue.Title);
            var unlock = _flow.ConsumePendingUnlock();
            map.Show(_flow.State, _flow.Region, unlock, _notice);
            if (_session.LoadStatus != SaveLoadStatus.TooNew) _notice = null;
            if (unlock != null) RunUnlock(unlock);
        }

        /// <summary>
        /// 星図・星域の画面にいる間に、デッキ編集の小さなカードを 1 フレームに 2 枚ずつ作っておく（5c。開いた直後のフレーム落ちを防ぐ）。
        /// 一覧に要るのは手持ちの種類数まで。
        /// </summary>
        void StartPrewarm()
        {
            if (_prewarm != null || deck == null || _session == null) return;
            _prewarm = StartCoroutine(Prewarm());
        }

        System.Collections.IEnumerator Prewarm()
        {
            while (true)
            {
                var s = Step;
                if (s != CampaignStep.Map && s != CampaignStep.Region) break;
                if (deck.PrewarmStep(_session.State.Collection.Count, 2)) break;
                yield return null;
            }
            _prewarm = null;
        }

        /// <summary>
        /// 段階の演出を 1 つ動かし、終わったら then（CampaignFlow の次の段階へ進める操作）を呼ぶ。
        /// then の中で次の段階の演出が始まるので、Busy は then の前に下ろす。キャンセル（タイトルへ戻るなど）なら then は呼ばない。
        /// </summary>
        async void Run(Func<CancellationToken, Awaitable> body, Action then)
        {
            var ct = Token;
            Busy = true;
            try
            {
                await body(ct);
            }
            catch (OperationCanceledException)
            {
                Busy = false;
                return;
            }
            catch (Exception e)
            {
                Busy = false;
                Debug.LogException(e);
                return;
            }
            Busy = false;
            then?.Invoke();
        }

        void RunUnlock(Region r) => Run(ct => map.PlayUnlockAsync(r, Tf, ct), null);

        void RunIntro()
        {
            app.HideAll();
            app.PlayBgm(BgmCue.Title);
            Run(async ct =>
            {
                await reward.PlayIntroAsync(CampaignData.StarterCards, Tf, ct);
                reward.Hide();
            }, () =>
            {
                _flow.FinishIntro();
                ApplyGrants();
            });
        }

        /// <summary>URL の ?grant=a,b,c（自動操作・撮影用）: 手持ちにカードを足して保存する。</summary>
        void ApplyGrants()
        {
            if ((_grants == null || _grants.Length == 0) && !_unlockAll) return;
            var col = new Dictionary<string, int>();
            foreach (var kv in _session.State.Collection) col[kv.Key] = kv.Value;
            foreach (var id in _grants ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(id) || !CardCatalog.ById.ContainsKey(id)) continue;
                col[id] = (col.TryGetValue(id, out int n) ? n : 0) + 1;
            }
            var state = _session.State.WithCollection(col);
            if (_unlockAll)
            {
                var all = new List<string>();
                foreach (var r in CampaignData.Regions) all.Add(r.Id);
                state = state.WithUnlocked(all);
                Debug.LogWarning("[Edgewise] debug unlock: all regions");
            }
            _session.Save(state);
            if (_grants != null) Debug.LogWarning("[Edgewise] debug grant: " + string.Join(",", _grants));
            _grants = null;
            _unlockAll = false;
            if (_flow.Step == CampaignStep.Map) map.Show(_flow.State, _flow.Region);
        }

        void RunTalk(CampaignStep step, Action done)
        {
            // 対戦前の会話は星空の上、対戦後は盤の上に出す
            if (step == CampaignStep.TalkBefore || step == CampaignStep.TalkClear)
            {
                app.HideAll();
                if (step == CampaignStep.TalkBefore) app.PlayBgm(BgmCue.Title);
            }
            else
            {
                app.Result.Hide();
                app.Hud.SetVisible(false);
            }
            Run(ct => dialogue.PlayAsync(_flow.Lines, Tf, ct, step == CampaignStep.TalkAfter), () =>
            {
                if (_flow.Step == step) done();
            });
        }

        void RunMatch()
        {
            if (MatchOverride != null)
            {
                var forced = MatchOverride(_flow);
                if (forced.HasValue)
                {
                    // テスト: 対戦をせずに結果を決める（結果画面は出さない）
                    _matchOutcome = null;
                    Run(ct => Awaitable.NextFrameAsync(ct), () => _flow.FinishMatch(forced.Value));
                    return;
                }
            }
            var settings = new MatchSettings
            {
                seat0 = SeatKind.Human,
                seat1 = SeatKind.Cpu,
                campaign = new CampaignMatchSetup
                {
                    Config = _flow.MatchConfig(),
                    Profile = _flow.Opponent.Profile,
                    Opponent = _flow.Opponent,
                    HintPool = CampaignTexts.HintPool(_flow.PreMatchState, _flow.Opponent),
                },
            };
            app.BeginCampaignMatchView();
            if (AutomationLog.Enabled)
                AutomationLog.Write("campaign match " + _flow.Opponent.Id + " deck " + string.Join(",", _flow.PlayerDeck) + " vs " + string.Join(",", _flow.OpponentDeck));
            Run(async ct =>
            {
                await Awaitable.NextFrameAsync(ct);
                var fps = app.Fps;
                if (fps != null) fps.Measuring = true;
                var outcome = await app.Match.RunAsync(settings, ct);
                if (fps != null)
                {
                    fps.Measuring = false;
                    if (AutomationLog.Enabled)
                        AutomationLog.Write("fps-match avg " + fps.AverageFps.ToString("0.0", CultureInfo.InvariantCulture) + " worst "
                            + fps.WorstFrameMs.ToString("0", CultureInfo.InvariantCulture) + "ms");
                }
                app.Hud.ShowRotateButtons(false);
                await app.Match.PlayFinaleAsync(outcome, ct);
                _matchOutcome = outcome;
            }, () =>
            {
                // 勝敗を反映して保存（報酬を選ぶ前）→ 結果画面へ
                _flow.FinishMatch(app.Match.State, _matchOutcome.Resigned);
            });
        }

        void RunResult()
        {
            if (_matchOutcome == null)
            {
                // 対戦を注入したとき（テスト）は結果画面を出さない
                _flow.FinishResult();
                return;
            }
            var o = _matchOutcome;
            var result = app.Result;
            Run(async ct =>
            {
                result.CampaignMode = true;
                bool next = false;
                void OnNext() => next = true;
                result.NextPressed += OnNext;
                try
                {
                    await result.Show(o.Score, o.Result, o.Resigned, app.Assets.juice, Tf, ct, () => app.OnResultShown(o.Result), _ => app.Match.Fx.OnCursorMove());
                    if (_flow.HasOutcome && _flow.Outcome.Perfect) result.SetNote(UiText.ResultPerfect);
                    ResultWaiting = true;
                    AutomationLog.Buttons("Result", result.transform);
                    while (!next) await Awaitable.NextFrameAsync(ct);
                }
                finally
                {
                    result.NextPressed -= OnNext;
                    result.CampaignMode = false;
                    ResultWaiting = false;
                }
            }, () => _flow.FinishResult());
        }

        /// <summary>結果画面の「次へ」を押す（テスト用）。</summary>
        public void PressResultNext()
        {
            if (ResultWaiting) app.Result.NextButton.onClick.Invoke();
        }

        void RunReward()
        {
            app.HideAll();
            Run(async ct =>
            {
                await reward.PlayRewardAsync(_flow, Tf, ct);
                reward.Hide();
            }, () => _flow.FinishReward());
        }

        void RunTake()
        {
            app.HideAll();
            Run(async ct =>
            {
                await reward.PlayTakeAsync(_flow, Tf, ct);
                reward.Hide();
            }, () => _flow.FinishClassicTake());
        }

        void RunCleared()
        {
            app.HideAll();
            app.PlayBgm(BgmCue.Title);
            Run(ct => cleared.PlayAsync(_flow.ClearedRegion, _flow.PendingUnlock, Tf, ct), () => _flow.FinishCleared());
        }

        /// <summary>エンディングの文（最後の星域の主に初めて勝ったとき。5c）。</summary>
        void RunEnding()
        {
            app.HideAll();
            app.PlayBgm(BgmCue.Title);
            Run(ct => ending.PlayAsync(CampaignData.Ending, Tf, ct), () => _flow.FinishEnding());
        }

        /// <summary>
        /// エンディングの後のエンドロール（自動で流れる。スキップ可）→ 星図。
        /// 背景のカードはエンドロール自身が左右に流すので、タイトルの扇は出さない（本文の上に重なるため。8a）。
        /// </summary>
        void RunEndCredits()
        {
            app.HideAll();
            app.PlayBgm(BgmCue.Title);
            Run(ct => endRoll.PlayAsync(ct), () => _flow.FinishEndCredits());
        }
    }
}
