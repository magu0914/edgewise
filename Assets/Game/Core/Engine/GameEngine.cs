using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Edgewise.Core
{
    /// <summary>
    /// ルールエンジンの公開 API（DESIGN.md §4-2）。すべて純関数で、入力の state を変更しない。
    /// 1 手の解決は GameEngine.Apply.cs（参照実装）と GameEngine.Fast.cs（探索用の高速版）にある。
    /// </summary>
    public static partial class GameEngine
    {
        /// <summary>配列から rng で n 枚を非復元抽出（部分 Fisher–Yates）。TS 版 drawRandom と同じ消費順。</summary>
        static string[] DrawRandom(ReadOnlyArray<string> deck, int n, ref uint rng)
        {
            var pool = deck.ToArray();
            for (int i = 0; i < n; i++)
            {
                int j = Rng.NextInt(rng, pool.Length - i, out rng);
                int k = i + j;
                var tmp = pool[i];
                pool[i] = pool[k];
                pool[k] = tmp;
            }
            var hand = new string[n];
            for (int i = 0; i < n; i++) hand[i] = pool[i];
            return hand;
        }

        /// <summary>対戦を開始する。先攻・Random の手札・Elemental のマス属性はすべて seed から決まる。</summary>
        public static GameState CreateGame(GameConfig config)
        {
            var rules = config.Rules;

            var allCards = config.Cards;
            uint rng = Rng.SeedToState(config.Seed);

            // 乱数の消費順: 先攻 → p0 の手札 → p1 の手札 → マス属性
            var firstPlayer = Rng.NextChance(rng, 1, 2, out rng) ? Player.P0 : Player.P1;

            var hands = new string[2][];
            for (int p = 0; p < 2; p++)
            {
                var deck = config.Deck((Player)p);
                foreach (var id in deck)
                {
                    if (id == null || !allCards.ContainsKey(id))
                        throw new InvalidConfigException("Unknown card id in deck " + p + ": " + id);
                }
                if (rules.Random)
                {
                    if (deck.Count < GameConstants.HandSize)
                        throw new InvalidConfigException(
                            "Deck " + p + " needs at least " + GameConstants.HandSize + " cards for Random (got " + deck.Count + ")");
                    hands[p] = DrawRandom(deck, GameConstants.HandSize, ref rng);
                }
                else
                {
                    if (deck.Count != GameConstants.HandSize)
                        throw new InvalidConfigException(
                            "Deck " + p + " must have exactly " + GameConstants.HandSize + " cards (got " + deck.Count + ")");
                    hands[p] = deck.ToArray();
                }
            }

            var cellElements = new Element?[GameConstants.BoardSize];
            if (rules.Elemental)
            {
                for (int i = 0; i < GameConstants.BoardSize; i++)
                {
                    if (Rng.NextChance(rng, GameConstants.ElementalCellProbabilityNumerator,
                            GameConstants.ElementalCellProbabilityDenominator, out rng))
                    {
                        cellElements[i] = (Element)Rng.NextInt(rng, GameConstants.ElementCount, out rng);
                    }
                }
            }

            // state には手札に入ったカードの定義だけを持たせる（自己完結・軽量）
            var cards = new Dictionary<string, CardDef>();
            foreach (var hand in hands)
                foreach (var id in hand)
                    cards[id] = allCards[id];

            // トークンは先攻・後攻で別々に配る（Phase 3c）。既定 2/2 なら TS 版と同じ
            int first = rules.Pivot ? config.PivotTokensFirst : 0;
            int second = rules.Pivot ? config.PivotTokensSecond : 0;
            int tokens0 = firstPlayer == Player.P0 ? first : second;
            int tokens1 = firstPlayer == Player.P1 ? first : second;
            return new GameState(
                rules,
                new ReadOnlyDictionary<string, CardDef>(cards),
                new PlacedCard?[GameConstants.BoardSize],
                cellElements,
                hands[0],
                hands[1],
                firstPlayer,
                firstPlayer,
                tokens0,
                tokens1,
                rng,
                Phase.Playing,
                0);
        }

        /// <summary>合法手の一覧（手札順 → マス順 → 回転順）。終局後は空。</summary>
        public static List<Move> LegalMoves(GameState state)
        {
            var moves = new List<Move>();
            AddLegalMoves(state, moves);
            return moves;
        }

        /// <summary>合法手を buffer に追加する（探索用。buffer は呼び出し側で Clear しておく）。</summary>
        public static void AddLegalMoves(GameState state, List<Move> buffer)
        {
            if (state.Phase != Phase.Playing) return;
            int handCount = state.HandArray(state.Turn).Length;
            int maxRotation = state.Rules.Pivot && state.GetPivotTokens(state.Turn) > 0 ? 3 : 0;
            var board = state.BoardArray;
            for (int handIndex = 0; handIndex < handCount; handIndex++)
            {
                for (int cell = 0; cell < GameConstants.BoardSize; cell++)
                {
                    if (board[cell].HasValue) continue;
                    for (int r = 0; r <= maxRotation; r++) buffer.Add(new Move(handIndex, cell, r));
                }
            }
        }

        /// <summary>不正な手なら IllegalMoveException を投げる。判定順とメッセージは TS 版と同じ。</summary>
        public static void AssertLegalMove(GameState state, Move move)
        {
            if (state.Phase != Phase.Playing) throw new IllegalMoveException("Game has already ended");
            int handCount = state.HandArray(state.Turn).Length;
            if (move.HandIndex < 0 || move.HandIndex >= handCount)
                throw new IllegalMoveException("handIndex out of range: " + move.HandIndex);
            if (move.Cell < 0 || move.Cell >= GameConstants.BoardSize)
                throw new IllegalMoveException("cell out of range: " + move.Cell);
            if (state.BoardArray[move.Cell].HasValue) throw new IllegalMoveException("cell " + move.Cell + " is already occupied");
            if (move.Rotation != 0)
            {
                if (move.Rotation != 1 && move.Rotation != 2 && move.Rotation != 3)
                    throw new IllegalMoveException("invalid rotation: " + move.Rotation);
                if (!state.Rules.Pivot) throw new IllegalMoveException("rotation requires the Pivot rule");
                if (state.GetPivotTokens(state.Turn) <= 0) throw new IllegalMoveException("no Pivot tokens left");
            }
        }

        public static bool IsLegalMove(GameState state, Move move)
        {
            try
            {
                AssertLegalMove(state, move);
                return true;
            }
            catch (IllegalMoveException)
            {
                return false;
            }
        }

        /// <summary>盤上の所有枚数 + 手札の残り枚数。</summary>
        public static (int P0, int P1) Score(GameState state)
        {
            int s0 = state.HandArray(Player.P0).Length;
            int s1 = state.HandArray(Player.P1).Length;
            var board = state.BoardArray;
            for (int i = 0; i < board.Length; i++)
            {
                if (!board[i].HasValue) continue;
                if (board[i].Value.Owner == Player.P0) s0++;
                else s1++;
            }
            return (s0, s1);
        }

        public static GameResult ResultOfScore((int P0, int P1) s) =>
            s.P0 > s.P1 ? GameResult.P0 : s.P0 < s.P1 ? GameResult.P1 : GameResult.Draw;

        /// <summary>終局していれば勝敗、対戦中なら null。</summary>
        public static GameResult? Result(GameState state)
        {
            if (state.Phase != Phase.Ended) return null;
            return ResultOfScore(Score(state));
        }

        /// <summary>
        /// viewer から見た player の手札（Open ルール）。
        /// Open OFF で相手の手札は枚数だけわかり、中身は null になる。
        /// </summary>
        public static string[] VisibleHand(GameState state, Player viewer, Player player)
        {
            var hand = state.HandArray(player);
            var result = new string[hand.Length];
            if (state.Rules.Open || viewer == player)
            {
                for (int i = 0; i < hand.Length; i++) result[i] = hand[i];
            }
            return result;
        }

        /// <summary>局面のハッシュ（16 桁 hex）。TS 版 hashState と同じ値。</summary>
        public static string HashState(GameState state) => StateHash.Hash(state);
    }
}
