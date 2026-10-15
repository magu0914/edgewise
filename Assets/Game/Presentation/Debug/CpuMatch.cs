using System.Collections.Generic;
using Edgewise.Core;
using Edgewise.Core.AI;
using Edgewise.Core.Data;
using Edgewise.Game.AI;

namespace Edgewise.Game.Debugging
{
    /// <summary>1 手分の思考の記録（デバッグ表示・計測用）。</summary>
    public sealed class MoveRecord
    {
        public int Number;
        public Player Player;
        public string Profile;
        public Move Move;
        public double ThinkMs;
        public double ComputeMs;
        public long Nodes;
        public int Frames;
        public bool Solved;
        public int Flips;
        public string Rules;

        /// <summary>計算中の秒間ノード数（Step の中にいた時間で割る）。</summary>
        public double NodesPerSecond => ComputeMs > 0 ? Nodes * 1000.0 / ComputeMs : 0;

        /// <summary>思考中のフレームのうち最も遅いフレームから求めた FPS（表示側で設定）。</summary>
        public double MinFps;

        public override string ToString() =>
            "#" + Number + " P" + (int)Player + " " + Profile + " " + Move
            + "  think " + ThinkMs.ToString("0") + " ms (cpu " + ComputeMs.ToString("0") + " ms, " + Frames + " frames)"
            + "  " + Nodes + " nodes  " + (NodesPerSecond / 1000.0).ToString("0") + " k nodes/s"
            + "  min " + MinFps.ToString("0") + " fps" + (Solved ? "  solved" : "") + (Flips > 0 ? "  flips " + Flips : "");
    }

    /// <summary>
    /// CPU 対 CPU の 1 局を 1 手ずつ進めるモデル（Unity に依存しない部分）。思考は AiDriver に任せる。
    /// 配牌は Sim と同じく 50 枚から重複なしで選び、seed がすべてを決める。
    /// </summary>
    public sealed class CpuMatch
    {
        public RuleSet Rules { get; }
        public long Seed { get; }
        public GameState State { get; private set; }
        public AiProfile[] Profiles { get; } = new AiProfile[2];
        public IReadOnlyList<MoveRecord> Records => _records;
        public IReadOnlyList<GameEvent> LastEvents { get; private set; } = new List<GameEvent>();
        public bool IsOver => State.Phase != Phase.Playing;

        readonly List<MoveRecord> _records = new List<MoveRecord>();
        uint _rng;

        public CpuMatch(RuleSet rules, long seed, AiProfile p0, AiProfile p1)
        {
            Rules = rules;
            Seed = seed;
            Profiles[0] = p0;
            Profiles[1] = p1;

            var all = CardCatalog.All;
            var ids = new string[all.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = all[i].Id;
            _rng = Rng.SeedToState(seed);
            int deckSize = rules.Random ? 8 : GameConstants.HandSize;
            for (int i = 0; i < 2 * deckSize; i++)
            {
                int j = i + Rng.NextInt(_rng, ids.Length - i, out _rng);
                var t = ids[i];
                ids[i] = ids[j];
                ids[j] = t;
            }
            var deck0 = new string[deckSize];
            var deck1 = new string[deckSize];
            System.Array.Copy(ids, 0, deck0, 0, deckSize);
            System.Array.Copy(ids, deckSize, deck1, 0, deckSize);
            State = GameEngine.CreateGame(new GameConfig(rules, deck0, deck1, seed, CardCatalog.ById));
        }

        /// <summary>手番の CPU の思考を driver で始める。</summary>
        public void BeginThinking(AiDriver driver)
        {
            long moveSeed = Rng.NextUInt32(_rng, out _rng);
            driver.Begin(State, State.Turn, Profiles[(int)State.Turn], moveSeed, CardCatalog.All);
        }

        /// <summary>思考が終わった driver の手を指し、記録を返す。</summary>
        public MoveRecord ApplyThought(AiDriver driver, double minFps)
        {
            var move = driver.BestMove;
            var record = new MoveRecord
            {
                Number = _records.Count + 1,
                Player = State.Turn,
                Profile = Profiles[(int)State.Turn].Name,
                Move = move,
                ThinkMs = driver.ThinkMs,
                ComputeMs = driver.ComputeMs,
                Nodes = driver.Nodes,
                Frames = driver.Frames,
                Solved = driver.Session.BestMoveSolved,
                MinFps = minFps,
            };
            var result = GameEngine.ApplyMove(State, move);
            foreach (var e in result.Events)
            {
                if (e is FlipEvent) record.Flips++;
            }
            LastEvents = result.Events;
            State = result.State;
            _records.Add(record);
            return record;
        }
    }
}
