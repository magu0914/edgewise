using System;
using System.Collections.Generic;
using Edgewise.Core;
using Edgewise.Core.Campaign;
using CampaignOutcome = Edgewise.Core.Campaign.MatchOutcome;

namespace Edgewise.Game.Campaign
{
    /// <summary>星図巡りの画面（CampaignFlow の段階）。</summary>
    public enum CampaignStep
    {
        /// <summary>星図巡りの外（タイトルなど）。</summary>
        Closed = 0,

        /// <summary>はじめて遊ぶときの導入（はじめの 7 枚を受け取る）。</summary>
        Intro,

        /// <summary>星図（星域を選ぶ）。</summary>
        Map,

        /// <summary>星域の画面（相手を選ぶ）。</summary>
        Region,

        /// <summary>デッキ編集（対戦前に必ず通る）。</summary>
        Deck,

        /// <summary>対戦前の会話（登場）。</summary>
        TalkBefore,

        Match,

        /// <summary>結果画面（スコア → WIN / LOSE / DRAW）。</summary>
        Result,

        /// <summary>対戦後の会話（勝ち: 負けた台詞＋渡す台詞 / 負け: 勝った台詞＋取る台詞）。</summary>
        TalkAfter,

        /// <summary>報酬（裏向きの 5 枚から選ぶ）。</summary>
        Reward,

        /// <summary>原作モードで負けたときの「取られる」演出。</summary>
        ClassicTake,

        /// <summary>主に初めて勝ったときの会話（星域を抜けた）。</summary>
        TalkClear,

        /// <summary>「〈星域名〉を巡り終えた」の帯。この後の星図で次の星域の鍵が外れる。</summary>
        Cleared,

        /// <summary>エンディングの文（最後の星域の主に初めて勝ったとき。CampaignData.Ending）。</summary>
        Ending,

        /// <summary>エンディングの後、クレジットを自動で流す（スキップ可）。終わったら星図へ。</summary>
        EndCredits,
    }

    /// <summary>会話の 1 行（話す相手と本文）。</summary>
    public readonly struct DialogueLine
    {
        public readonly Opponent Speaker;
        public readonly string Text;

        public DialogueLine(Opponent speaker, string text)
        {
            Speaker = speaker;
            Text = text;
        }
    }

    /// <summary>
    /// 星図巡りの画面の流れ（DESIGN §12-1 / §12-11）。表示に依存しない純粋な状態遷移で、EditMode でテストする。
    /// 星図 → 星域 → デッキ → 会話 → 対戦 → 結果 → 会話 → 報酬（勝ち）/ 取られる（原作モードの負け）→（主に初勝利: 会話 → 帯）→ 星図。
/// 最後の星域の主に初めて勝ったときだけ、帯の後にエンディングの文 → クレジット → 星図（5c）。登場の会話は 2 回目以降の対戦で再戦用になる。
    /// セーブ（CampaignSession.Save）はデッキを決めた後・対戦の後・報酬の後（と導入の後）に行う。
    /// - 対戦の後: 勝敗・解放・原作モードで取られたカードを、報酬なし（picks 空）で Core の ApplyMatch に通して保存する
    ///   （報酬を選ぶ前にやめても勝敗は残る）。
    /// - 報酬の後: 対戦前の進行に、選んだカードつきで ApplyMatch をやり直して保存する（純関数なので結果は 1 回で通したのと同じ）。
    /// 各操作は今の段階で受け付けられなければ false を返す（二度押しなどで状態を壊さない）。
    /// </summary>
    public sealed class CampaignFlow
    {
        public CampaignFlow(CampaignSession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public CampaignSession Session { get; }
        public CampaignStep Step { get; private set; } = CampaignStep.Closed;
        public CampaignState State => Session.State;

        public Region Region { get; private set; }
        public Opponent Opponent { get; private set; }
        public DeckEditModel Deck { get; private set; }
        public IReadOnlyList<string> PlayerDeck { get; private set; }
        public IReadOnlyList<string> OpponentDeck { get; private set; }
        public long MatchSeed { get; private set; }

        /// <summary>デッキを決めて保存した直後の進行（報酬の後に ApplyMatch をやり直す元）。</summary>
        public CampaignState PreMatchState { get; private set; }

        public CampaignOutcome Outcome { get; private set; }
        public bool HasOutcome { get; private set; }

        /// <summary>対戦の後に（報酬なしで）反映した結果。取られたカード・解放・初めて主に勝ったか。</summary>
        public MatchApplication Application { get; private set; }

        public RewardModel Reward { get; private set; }

        /// <summary>この対戦がその相手との再戦か（登場の台詞を再戦用にした）。</summary>
        public bool IsRematch { get; private set; }

        /// <summary>今の会話の行（TalkBefore / TalkAfter / TalkClear）。</summary>
        public IReadOnlyList<DialogueLine> Lines { get; private set; } = Array.Empty<DialogueLine>();

        /// <summary>巡り終えた星域（帯に出す）。</summary>
        public Region ClearedRegion { get; private set; }

        /// <summary>次に星図を出すときに鍵を外す演出をする星域（星図が ConsumePendingUnlock で受け取る）。</summary>
        public Region PendingUnlock { get; private set; }

        /// <summary>段階が変わったとき（前の段階、新しい段階）。</summary>
        public event Action<CampaignStep, CampaignStep> StepChanged;

        void Go(CampaignStep next)
        {
            var prev = Step;
            Step = next;
            StepChanged?.Invoke(prev, next);
        }

        // ------------------------------------------------------------------
        // 入口・星図・星域・デッキ
        // ------------------------------------------------------------------

        /// <summary>タイトルの「星図巡り」: 保存済みの進行があれば星図、なければ導入へ。</summary>
        public bool Enter()
        {
            if (Step != CampaignStep.Closed) return false;
            Go(Session.HasSave ? CampaignStep.Map : CampaignStep.Intro);
            return true;
        }

        /// <summary>導入の後: はじめの進行を保存して星図へ。</summary>
        public bool FinishIntro()
        {
            if (Step != CampaignStep.Intro) return false;
            Session.StartNew();
            Go(CampaignStep.Map);
            return true;
        }

        public bool OpenRegion(Region region)
        {
            if (Step != CampaignStep.Map || region == null || !State.IsUnlocked(region.Id)) return false;
            Region = region;
            Go(CampaignStep.Region);
            return true;
        }

        /// <summary>戻る: デッキ → 星域 → 星図 → タイトル（導入からもタイトル）。</summary>
        public bool Back()
        {
            switch (Step)
            {
                case CampaignStep.Deck:
                    Deck = null;
                    Go(CampaignStep.Region);
                    return true;
                case CampaignStep.Region:
                    Go(CampaignStep.Map);
                    return true;
                case CampaignStep.Map:
                case CampaignStep.Intro:
                    Go(CampaignStep.Closed);
                    return true;
                default:
                    return false;
            }
        }

        public bool ChooseOpponent(Opponent opponent)
        {
            if (Step != CampaignStep.Region || opponent == null || opponent.RegionId != Region.Id) return false;
            if (!CampaignRules.CanChallenge(State, opponent)) return false;
            Opponent = opponent;
            Deck = DeckEditModel.For(State, Region);
            Go(CampaignStep.Deck);
            return true;
        }

        /// <summary>「この 5 枚で挑む」: デッキを星域の最後のデッキとして保存し、相手のデッキを選んで登場の会話へ。</summary>
        public bool ConfirmDeck()
        {
            if (Step != CampaignStep.Deck || Deck == null || !Deck.CanBegin) return false;
            PlayerDeck = new List<string>(Deck.Selected).AsReadOnly();
            Session.Save(State.WithLastDeck(Region.Id, PlayerDeck));
            PreMatchState = State;
            MatchSeed = CampaignRules.MatchSeed(State, Opponent);
            var deck = CampaignRules.ChooseOpponentDeck(State, Opponent, MatchSeed)
                ?? throw new InvalidOperationException("the opponent cannot build a deck: " + Opponent.Id);
            OpponentDeck = Array.AsReadOnly(deck);
            HasOutcome = false;
            Application = null;
            Reward = null;
            IsRematch = CampaignRules.IsRematch(State, Opponent);
            Lines = Talk(CampaignRules.IntroLines(State, Opponent));
            Go(CampaignStep.TalkBefore);
            return true;
        }

        // ------------------------------------------------------------------
        // 対戦
        // ------------------------------------------------------------------

        /// <summary>この対戦の設定（星域のルール、主は Sudden Death を足す。プレイヤーが P0）。</summary>
        public GameConfig MatchConfig() => CampaignRules.MatchConfig(Opponent, PlayerDeck, OpponentDeck, MatchSeed);

        public RuleSet MatchRules => Opponent == null ? null : CampaignRules.MatchRules(Opponent);

        public bool StartMatch()
        {
            if (Step != CampaignStep.TalkBefore) return false;
            Go(CampaignStep.Match);
            return true;
        }

        /// <summary>対戦が終わった（降参は負け）。勝敗を反映して保存し、結果画面へ。</summary>
        public bool FinishMatch(GameState final, bool resigned)
        {
            if (Step != CampaignStep.Match) return false;
            var outcome = resigned || final == null ? new CampaignOutcome(MatchResultKind.Loss, false) : CampaignRules.Judge(final, Player.P0);
            return FinishMatch(outcome);
        }

        /// <summary>対戦の結果を直接渡す（テストで勝敗を注入するとき）。</summary>
        public bool FinishMatch(CampaignOutcome outcome)
        {
            if (Step != CampaignStep.Match) return false;
            Outcome = outcome;
            HasOutcome = true;
            Application = CampaignRules.ApplyMatch(PreMatchState, Opponent, PlayerDeck, OpponentDeck, outcome, Array.Empty<string>());
            Session.Save(Application.State);
            Go(CampaignStep.Result);
            return true;
        }

        public bool FinishResult()
        {
            if (Step != CampaignStep.Result) return false;
            Lines = AfterMatchLines();
            if (Lines.Count > 0) Go(CampaignStep.TalkAfter);
            else NextAfterTalk();
            return true;
        }

        /// <summary>対戦後の会話: 勝ち = 相手の「負けた」台詞＋「渡す」台詞、負け = 「勝った」台詞（＋原作モードで取ったら「取る」台詞）。</summary>
        List<DialogueLine> AfterMatchLines()
        {
            var lines = new List<DialogueLine>();
            switch (Outcome.Kind)
            {
                case MatchResultKind.Win:
                    lines.AddRange(Talk(Opponent.Lines.PlayerWins));
                    lines.AddRange(Talk(Opponent.Lines.GiveCard));
                    break;
                case MatchResultKind.Loss:
                    lines.AddRange(Talk(Opponent.Lines.PlayerLoses));
                    if (Application?.TakenCard != null) lines.AddRange(Talk(Opponent.Lines.TakeCard));
                    break;
            }
            return lines;
        }

        public bool FinishTalkAfter()
        {
            if (Step != CampaignStep.TalkAfter) return false;
            NextAfterTalk();
            return true;
        }

        void NextAfterTalk()
        {
            if (Outcome.Kind == MatchResultKind.Win && Outcome.RewardCount > 0)
            {
                Reward = new RewardModel(OpponentDeck, Outcome.RewardCount, State.Collection);
                Go(CampaignStep.Reward);
            }
            else if (Outcome.Kind == MatchResultKind.Loss && Application != null && (Application.TakenCard != null || Application.ProtectedCard != null))
            {
                Go(CampaignStep.ClassicTake);
            }
            else ToMap();
        }

        // ------------------------------------------------------------------
        // 報酬・取られる・巡り終えた
        // ------------------------------------------------------------------

        public RewardPick? Pick(int index) => Step == CampaignStep.Reward && Reward != null ? Reward.Pick(index) : null;

        /// <summary>選び終えた: 選んだカードを手持ちに加えて保存する。初めて主に勝ったなら会話 → 帯、そうでなければ星図へ。</summary>
        public bool FinishReward()
        {
            if (Step != CampaignStep.Reward || Reward == null || !Reward.IsDone) return false;
            var final = CampaignRules.ApplyMatch(PreMatchState, Opponent, PlayerDeck, OpponentDeck, Outcome, Reward.Picks);
            Session.Save(final.State);
            if (Application.ClearedRegion)
            {
                ClearedRegion = Region;
                PendingUnlock = Application.UnlockedRegion;
                Lines = Talk(Opponent.Lines.RegionClear);
                if (Lines.Count > 0) Go(CampaignStep.TalkClear);
                else Go(CampaignStep.Cleared);
            }
            else ToMap();
            return true;
        }

        public bool FinishClassicTake()
        {
            if (Step != CampaignStep.ClassicTake) return false;
            ToMap();
            return true;
        }

        public bool FinishClearTalk()
        {
            if (Step != CampaignStep.TalkClear) return false;
            Go(CampaignStep.Cleared);
            return true;
        }

        /// <summary>帯の後: 最後の星域を初めて巡り終えたならエンディングへ、そうでなければ星図へ。</summary>
        public bool FinishCleared()
        {
            if (Step != CampaignStep.Cleared) return false;
            if (Application != null && Application.CompletedJourney) Go(CampaignStep.Ending);
            else ToMap();
            return true;
        }

        public bool FinishEnding()
        {
            if (Step != CampaignStep.Ending) return false;
            Go(CampaignStep.EndCredits);
            return true;
        }

        /// <summary>クレジットを流し終えた（スキップも）: 星図へ戻る（以後も再戦と収集を続けられる）。</summary>
        public bool FinishEndCredits()
        {
            if (Step != CampaignStep.EndCredits) return false;
            ToMap();
            return true;
        }

        /// <summary>星図で鍵を外す演出をする星域を受け取る（1 回だけ）。</summary>
        public Region ConsumePendingUnlock()
        {
            var r = PendingUnlock;
            PendingUnlock = null;
            return r;
        }

        /// <summary>タイトルへ（どの段階からでも。対戦中は呼ばないこと）。</summary>
        public void Exit()
        {
            Deck = null;
            Go(CampaignStep.Closed);
        }

        void ToMap()
        {
            Deck = null;
            Reward = null;
            Go(CampaignStep.Map);
        }

        IReadOnlyList<DialogueLine> Talk(IReadOnlyList<string> texts)
        {
            var lines = new List<DialogueLine>();
            if (texts != null)
            {
                foreach (var t in texts) lines.Add(new DialogueLine(Opponent, t));
            }
            return lines;
        }
    }
}
