using System;
using System.Collections.Generic;
using Edgewise.Core;
using Edgewise.Game.Match;
using Edgewise.Game.Online;
using NUnit.Framework;

namespace Edgewise.Tests.EditMode
{
    /// <summary>オンライン対戦（Phase 7。DESIGN §9-1）: OnlineMatchSession を LoopbackTransport の 2 つの端点で動かして確かめる。</summary>
    public class OnlineSessionTests
    {
        const string Code = "ABC234";

        static readonly RuleSet AllRules = new RuleSet(open: false, random: true, same: true, sameWall: true, plus: true, combo: true,
            elemental: true, suddenDeath: true, pivot: true);

        /// <summary>ホストとゲストの 2 つのセッション（同じ LoopbackNetwork）。時計はテストが進める。</summary>
        sealed class Pair
        {
            public readonly LoopbackNetwork Net = new LoopbackNetwork();
            public readonly LoopbackTransport HostT;
            public readonly LoopbackTransport GuestT;
            public readonly OnlineMatchSession Host;
            public readonly OnlineMatchSession Guest;
            public double Now;
            readonly Queue<long> _seeds;

            public Pair(params long[] seeds)
            {
                _seeds = new Queue<long>(seeds.Length > 0 ? seeds : new long[] { 101, 202, 303, 404 });
                HostT = new LoopbackTransport(Net);
                GuestT = new LoopbackTransport(Net);
                Host = new OnlineMatchSession(HostT, OnlineRole.Host, () => Now, () => _seeds.Dequeue());
                Guest = new OnlineMatchSession(GuestT, OnlineRole.Guest, () => Now);
                HostT.Host(Code);
                GuestT.Join(Code);
                Pump();
            }

            public void Pump()
            {
                for (int i = 0; i < 6; i++)
                {
                    Host.Tick();
                    Guest.Tick();
                }
            }

            public void Start(RuleSet rules = null, CardBand band = CardBand.Intermediate)
            {
                Assert.That(Host.GuestPresent, Is.True, "the guest's hello arrived");
                Host.StartMatch(rules ?? AllRules, band);
                Pump();
            }

            public OnlineMatchSession Mover => Host.IsMyTurn ? Host : Guest;
            public OnlineMatchSession Waiter => Host.IsMyTurn ? Guest : Host;

            /// <summary>手番の側が（多少ばらつかせた）合法手を 1 つ指し、相手側に届けて受け取らせる。</summary>
            public (PendingMove Sent, PendingMove Received) PlayOne(int salt)
            {
                var mover = Mover;
                var waiter = Waiter;
                var moves = GameEngine.LegalMoves(mover.State);
                var sent = mover.SubmitMove(moves[(salt * 7 + 3) % moves.Count]);
                Pump();
                Assert.That(waiter.TryTakeOpponentMove(out var received), Is.True, "the move reached the other side");
                return (sent, received);
            }

            public void PlayToEnd()
            {
                int n = 0;
                while (Host.Phase == SessionPhase.Playing)
                {
                    PlayOne(n++);
                    Assert.That(n, Is.LessThan(40), "a game ends");
                }
            }
        }

        [Test]
        public void ARoomCodeHasSixUnambiguousCharacters()
        {
            var rng = new Random(5);
            for (int i = 0; i < 200; i++)
            {
                string code = RoomCode.Generate(rng);
                Assert.That(code, Has.Length.EqualTo(6));
                Assert.That(RoomCode.IsValid(code), Is.True, code);
                foreach (char bad in "01OIL") Assert.That(code.IndexOf(bad), Is.EqualTo(-1), code);
            }
            Assert.That(RoomCode.Normalize(" abc-234 "), Is.EqualTo("ABC234"));
            Assert.That(RoomCode.Normalize("ABC23"), Is.Null, "too short");
            Assert.That(RoomCode.Normalize("ABC230"), Is.Null, "0 is not used");
            Assert.That(RoomCode.Normalize("ABCIL2"), Is.Null);
            Assert.That(RoomCode.Normalize(null), Is.Null);
            Assert.That(RoomCode.PeerId("ABC234"), Is.EqualTo("edgewise-v1-ABC234"));
        }

        [Test]
        public void ARuleSetSurvivesTheWire()
        {
            Assert.That(OnlineSetup.ParseRules(AllRules.ToBitString()), Is.EqualTo(AllRules));
            Assert.That(OnlineSetup.ParseRules("1010"), Is.Null);
            Assert.That(OnlineSetup.ParseRules("0000000000x"), Is.Null);
            Assert.That(OnlineSetup.ParseRules("00000000001"), Is.Null, "resonance is not implemented");
        }

        [Test]
        public void TheMirrorSwapsSeatsAndIsItsOwnInverse()
        {
            var p = new Pair();
            p.Start();
            var s = p.Host.State;
            var mirrored = StateMirror.Mirror(s);
            Assert.That(mirrored.Turn, Is.EqualTo(StateMirror.Swap(s.Turn)));
            Assert.That(mirrored.GetHand(Player.P0).Count, Is.EqualTo(s.GetHand(Player.P1).Count));
            Assert.That(mirrored.GetHand(Player.P0)[0], Is.EqualTo(s.GetHand(Player.P1)[0]));
            Assert.That(StateMirror.Mirror(mirrored), Is.EqualTo(s));
            Assert.That(GameEngine.HashState(StateMirror.Mirror(mirrored)), Is.EqualTo(GameEngine.HashState(s)));

            // 1 手ごとに写しても、スコアは席が入れ替わるだけ
            for (int i = 0; i < 9 && p.Host.Phase == SessionPhase.Playing; i++)
            {
                var (sent, _) = p.PlayOne(i);
                var real = GameEngine.Score(sent.Result.State);
                var view = GameEngine.Score(StateMirror.Mirror(sent.Result.State));
                Assert.That(view, Is.EqualTo((real.P1, real.P0)));
                var ev = StateMirror.Mirror(sent.Result.Events);
                Assert.That(ev.Count, Is.EqualTo(sent.Result.Events.Count));
                Assert.That(((PlaceEvent)ev[0]).Player, Is.EqualTo(StateMirror.Swap(((PlaceEvent)sent.Result.Events[0]).Player)));
            }
        }

        [Test]
        public void TheGuestGetsTheSameGameAsTheHostAndTheSeatsAreOpposite()
        {
            var p = new Pair(777);
            p.Start();
            Assert.That(p.Host.Phase, Is.EqualTo(SessionPhase.Playing));
            Assert.That(p.Guest.Phase, Is.EqualTo(SessionPhase.Playing));
            Assert.That(p.Guest.Seed, Is.EqualTo(777));
            Assert.That(p.Guest.Rules, Is.EqualTo(AllRules));
            Assert.That(p.Guest.Band, Is.EqualTo(CardBand.Intermediate));
            Assert.That(p.Host.LocalSeat, Is.EqualTo(Player.P0), "round 0: the host is P0");
            Assert.That(p.Guest.LocalSeat, Is.EqualTo(Player.P1));
            Assert.That(GameEngine.HashState(p.Guest.State), Is.EqualTo(GameEngine.HashState(p.Host.State)));
            Assert.That(p.Host.State.Equals(p.Guest.State), Is.True);
        }

        [Test]
        public void AFullGameKeepsBothHashesEqualAfterEveryMove()
        {
            var p = new Pair(31337);
            p.Start();
            int moves = 0;
            while (p.Host.Phase == SessionPhase.Playing)
            {
                var (sent, received) = p.PlayOne(moves);
                Assert.That(received.Move, Is.EqualTo(sent.Move));
                Assert.That(received.Seq, Is.EqualTo(moves));
                Assert.That(GameEngine.HashState(p.Host.State), Is.EqualTo(GameEngine.HashState(p.Guest.State)), "after move " + moves);
                Assert.That(received.Result.Events.Count, Is.EqualTo(sent.Result.Events.Count));
                moves++;
                Assert.That(moves, Is.LessThan(40));
            }
            Assert.That(moves, Is.GreaterThanOrEqualTo(9));
            Assert.That(p.Host.Phase, Is.EqualTo(SessionPhase.Finished));
            Assert.That(p.Guest.Phase, Is.EqualTo(SessionPhase.Finished));
            Assert.That(p.Host.Ending, Is.EqualTo(EndReason.Completed));
            Assert.That(p.Guest.Ending, Is.EqualTo(EndReason.Completed));
            Assert.That(GameEngine.Score(p.Host.State), Is.EqualTo(GameEngine.Score(p.Guest.State)));
            Assert.That(p.Host.RejectedCount + p.Guest.RejectedCount, Is.EqualTo(0));
        }

        [Test]
        public void ItIsNotYourTurnOrAnIllegalMoveIsRefusedLocally()
        {
            var p = new Pair(5);
            p.Start();
            var waiter = p.Waiter;
            Assert.Throws<InvalidOperationException>(() => waiter.SubmitMove(new Move(0, 0)));
            Assert.Throws<IllegalMoveException>(() => p.Mover.SubmitMove(new Move(9, 0)));
            Assert.That(p.Mover.MovesApplied, Is.EqualTo(0));
        }

        // ---- 食い違い・重複・順番違い・バージョン違いの検出（相手側は生の端点で好きなメッセージを送る） ----

        /// <summary>ホストのセッションに、生の端点（ゲスト役）をつなぐ。ゲスト役は届いたメッセージを溜める。</summary>
        sealed class RawGuest
        {
            public readonly LoopbackNetwork Net = new LoopbackNetwork();
            public readonly LoopbackTransport HostT;
            public readonly LoopbackTransport Raw;
            public readonly OnlineMatchSession Host;
            public readonly List<OnlineMessage> Received = new List<OnlineMessage>();
            public GameState State;
            public double Now;

            public RawGuest(int protocol = OnlineMatchSession.ProtocolVersion)
            {
                HostT = new LoopbackTransport(Net);
                Raw = new LoopbackTransport(Net);
                Host = new OnlineMatchSession(HostT, OnlineRole.Host, () => Now, () => 4242);
                Raw.MessageReceived += raw => Received.Add(OnlineMessage.Parse(raw));
                HostT.Host(Code);
                Raw.Join(Code);
                Send(new OnlineMessage { type = OnlineMessage.Hello, protocol = protocol });
                Pump();
            }

            public void Pump()
            {
                for (int i = 0; i < 4; i++)
                {
                    Host.Tick();
                    Raw.Pump();
                }
            }

            public void Send(OnlineMessage m)
            {
                Raw.Send(m.ToJson());
                Pump();
            }

            /// <summary>ホストを始めさせ、ゲスト役のエンジンの局面を作る。</summary>
            public void StartMatch()
            {
                Host.StartMatch(AllRules, CardBand.Beginner);
                Pump();
                var start = Received.Find(m => m.type == OnlineMessage.Start);
                Assert.That(start, Is.Not.Null);
                State = OnlineSetup.CreateState(OnlineSetup.ParseRules(start.rules), (CardBand)start.band, start.seed);
            }

            /// <summary>ゲスト役がエンジンで 1 手指したメッセージを作る（hash を差し替えられる）。</summary>
            public OnlineMessage Move(int seq, Move move, string overrideHash = null)
            {
                var next = GameEngine.ApplyMove(State, move).State;
                var m = new OnlineMessage
                {
                    type = OnlineMessage.Move, seq = seq, hand = move.HandIndex, cell = move.Cell, rot = move.Rotation,
                    hash = overrideHash ?? GameEngine.HashState(next),
                };
                State = next;
                return m;
            }
        }

        /// <summary>ホストが先攻になる seed を使うため、先にホストが打つ必要があるときはホストの手を指して受け取らせる。</summary>
        static void MakeItTheGuestsTurn(RawGuest g)
        {
            if (g.Host.State.Turn == g.Host.LocalSeat)
            {
                var moves = GameEngine.LegalMoves(g.Host.State);
                var pm = g.Host.SubmitMove(moves[0]);
                g.State = pm.Result.State;
                g.Pump();
            }
            Assert.That(g.Host.State.Turn, Is.EqualTo(g.Host.OpponentSeat));
        }

        [Test]
        public void ADifferingHashStopsTheMatchAndTellsTheOtherSide()
        {
            var g = new RawGuest();
            g.StartMatch();
            MakeItTheGuestsTurn(g);
            var legal = GameEngine.LegalMoves(g.State)[0];
            g.Send(g.Move(g.Host.MovesApplied, legal, overrideHash: "0000000000000000"));
            Assert.That(g.Host.Phase, Is.EqualTo(SessionPhase.Aborted));
            Assert.That(g.Host.Ending, Is.EqualTo(EndReason.HashMismatch));
            Assert.That(g.Host.LastRejected, Is.EqualTo(RejectKind.Hash));
            Assert.That(g.Received.Exists(m => m.type == OnlineMessage.Abort && m.reason == "hash"), Is.True, "the peer is told");
            Assert.That(g.Host.TryTakeOpponentMove(out _), Is.False, "the bad move is not applied");
        }

        [Test]
        public void ADuplicatedSeqIsIgnoredAndCounted()
        {
            var g = new RawGuest();
            g.StartMatch();
            MakeItTheGuestsTurn(g);
            int seq = g.Host.MovesApplied;
            var legal = GameEngine.LegalMoves(g.State)[0];
            var msg = g.Move(seq, legal);
            g.Send(msg);
            Assert.That(g.Host.MovesApplied, Is.EqualTo(seq + 1));
            Assert.That(g.Host.TryTakeOpponentMove(out var pm), Is.True);
            Assert.That(pm.Seq, Is.EqualTo(seq));
            string hashBefore = GameEngine.HashState(g.Host.State);
            g.Send(msg); // 同じ手がもう一度
            Assert.That(g.Host.RejectedCount, Is.EqualTo(1));
            Assert.That(g.Host.LastRejected, Is.EqualTo(RejectKind.Duplicate));
            Assert.That(g.Host.MovesApplied, Is.EqualTo(seq + 1));
            Assert.That(GameEngine.HashState(g.Host.State), Is.EqualTo(hashBefore));
            Assert.That(g.Host.Phase, Is.EqualTo(SessionPhase.Playing), "a duplicate does not stop the match");
            Assert.That(g.Host.TryTakeOpponentMove(out _), Is.False);
        }

        [Test]
        public void ASkippedSeqIsOutOfOrderAndStopsTheMatch()
        {
            var g = new RawGuest();
            g.StartMatch();
            MakeItTheGuestsTurn(g);
            var legal = GameEngine.LegalMoves(g.State)[0];
            g.Send(g.Move(g.Host.MovesApplied + 2, legal));
            Assert.That(g.Host.Phase, Is.EqualTo(SessionPhase.Aborted));
            Assert.That(g.Host.Ending, Is.EqualTo(EndReason.OutOfOrder));
            Assert.That(g.Host.LastRejected, Is.EqualTo(RejectKind.OutOfOrder));
            Assert.That(g.Received.Exists(m => m.type == OnlineMessage.Abort && m.reason == "order"), Is.True);
        }

        [Test]
        public void AMoveOutOfTurnOrIllegalStopsTheMatch()
        {
            var g = new RawGuest();
            g.StartMatch();
            MakeItTheGuestsTurn(g);
            g.Send(new OnlineMessage { type = OnlineMessage.Move, seq = g.Host.MovesApplied, hand = 0, cell = 99, rot = 0, hash = "x" });
            Assert.That(g.Host.Ending, Is.EqualTo(EndReason.IllegalMove));
            Assert.That(g.Host.LastRejected, Is.EqualTo(RejectKind.Illegal));
        }

        [Test]
        public void AProtocolMismatchIsRefusedWithAVersionMessage()
        {
            var g = new RawGuest(protocol: 99);
            Assert.That(g.Host.GuestPresent, Is.False, "the host does not accept the guest");
            Assert.That(g.Host.LastRejected, Is.EqualTo(RejectKind.Protocol));
            var reject = g.Received.Find(m => m.type == OnlineMessage.Reject);
            Assert.That(reject, Is.Not.Null);
            Assert.That(reject.reason, Is.EqualTo("version"));
            Assert.Throws<InvalidOperationException>(() => g.Host.StartMatch(AllRules, CardBand.Beginner));
        }

        [Test]
        public void AGuestRefusesAHostWithAnotherProtocol()
        {
            var net = new LoopbackNetwork();
            var rawHost = new LoopbackTransport(net);
            var guestT = new LoopbackTransport(net);
            var guest = new OnlineMatchSession(guestT, OnlineRole.Guest, () => 0.0);
            EndReason? ended = null;
            guest.Ended += r => ended = r;
            rawHost.Host(Code);
            guestT.Join(Code);
            rawHost.Pump();
            guest.Tick();
            rawHost.Send(new OnlineMessage
            {
                type = OnlineMessage.Start, protocol = 2, rules = AllRules.ToBitString(), band = 1, seed = 5, hostIsP0 = true,
            }.ToJson());
            guest.Tick();
            Assert.That(ended, Is.EqualTo(EndReason.VersionMismatch));
            Assert.That(guest.Phase, Is.EqualTo(SessionPhase.Aborted));
            Assert.That(guest.State, Is.Null);
        }

        [Test]
        public void AGuestStopsWhenTheHostSaysRejected()
        {
            var net = new LoopbackNetwork();
            var rawHost = new LoopbackTransport(net);
            var guestT = new LoopbackTransport(net);
            var guest = new OnlineMatchSession(guestT, OnlineRole.Guest, () => 0.0);
            rawHost.Host(Code);
            guestT.Join(Code);
            rawHost.Pump();
            guest.Tick();
            rawHost.Send(new OnlineMessage { type = OnlineMessage.Reject, reason = "version" }.ToJson());
            guest.Tick();
            Assert.That(guest.Ending, Is.EqualTo(EndReason.VersionMismatch));
        }

        [Test]
        public void GarbageIsRejectedWithoutStoppingTheMatch()
        {
            var g = new RawGuest();
            g.StartMatch();
            g.Raw.Send("not json at all");
            g.Raw.Send("{\"type\":\"warp\"}");
            g.Pump();
            Assert.That(g.Host.RejectedCount, Is.EqualTo(2));
            Assert.That(g.Host.LastRejected, Is.EqualTo(RejectKind.Malformed));
            Assert.That(g.Host.Phase, Is.EqualTo(SessionPhase.Playing));
        }

        // ---- 再戦 ----

        [Test]
        public void ARematchStartsAfterBothAskAndUsesANewSeedAndSwapsSeats()
        {
            var p = new Pair(1111, 2222);
            p.Start();
            long firstSeed = p.Host.Seed;
            string firstStart = GameEngine.HashState(p.Host.StartState);
            p.PlayToEnd();
            Assert.That(p.Host.Phase, Is.EqualTo(SessionPhase.Finished));

            p.Guest.RequestRematch();
            p.Pump();
            Assert.That(p.Host.Phase, Is.EqualTo(SessionPhase.Finished), "one side asking is not enough");
            Assert.That(p.Host.OpponentWantsRematch, Is.True);
            Assert.That(p.Guest.RematchRequested, Is.True);

            p.Host.RequestRematch();
            p.Pump();
            Assert.That(p.Host.Phase, Is.EqualTo(SessionPhase.Playing));
            Assert.That(p.Guest.Phase, Is.EqualTo(SessionPhase.Playing));
            Assert.That(p.Host.Round, Is.EqualTo(1));
            Assert.That(p.Guest.Round, Is.EqualTo(1));
            Assert.That(p.Host.Seed, Is.EqualTo(2222));
            Assert.That(p.Host.Seed, Is.Not.EqualTo(firstSeed));
            Assert.That(p.Guest.Seed, Is.EqualTo(p.Host.Seed));
            Assert.That(GameEngine.HashState(p.Host.StartState), Is.Not.EqualTo(firstStart));
            Assert.That(GameEngine.HashState(p.Guest.State), Is.EqualTo(GameEngine.HashState(p.Host.State)));
            Assert.That(p.Host.LocalSeat, Is.EqualTo(Player.P1), "the seats are swapped in a rematch");
            Assert.That(p.Guest.LocalSeat, Is.EqualTo(Player.P0));
            Assert.That(p.Host.MovesApplied, Is.EqualTo(0));
            Assert.That(p.Host.RematchRequested, Is.False);

            // 新しい対戦も最後まで一致して進む
            p.PlayToEnd();
            Assert.That(GameEngine.HashState(p.Host.State), Is.EqualTo(GameEngine.HashState(p.Guest.State)));
        }

        [Test]
        public void ARematchNeverReusesTheSameSeed()
        {
            var p = new Pair(555, 555);
            p.Start();
            p.PlayToEnd();
            p.Host.RequestRematch();
            p.Guest.RequestRematch();
            p.Pump();
            Assert.That(p.Host.Seed, Is.Not.EqualTo(555));
            Assert.That(p.Guest.Seed, Is.EqualTo(p.Host.Seed));
        }

        // ---- 降参・退出・切断 ----

        [Test]
        public void ResigningEndsTheMatchForBothSides()
        {
            var p = new Pair();
            p.Start();
            p.PlayOne(0);
            p.Guest.Resign();
            p.Pump();
            Assert.That(p.Guest.Ending, Is.EqualTo(EndReason.Resigned));
            Assert.That(p.Host.Ending, Is.EqualTo(EndReason.OpponentResigned));
            Assert.That(p.Host.Phase, Is.EqualTo(SessionPhase.Finished));
            // 降参のあとでも再戦はできる
            p.Host.RequestRematch();
            p.Guest.RequestRematch();
            p.Pump();
            Assert.That(p.Host.Phase, Is.EqualTo(SessionPhase.Playing));
        }

        [Test]
        public void LeavingTheRoomGivesTheOtherSideAWinAndNoRematch()
        {
            var p = new Pair();
            p.Start();
            p.Guest.Leave();
            p.Pump();
            Assert.That(p.Host.Ending, Is.EqualTo(EndReason.OpponentLeft));
            Assert.That(p.Host.PeerGone, Is.True);
            p.Host.RequestRematch();
            Assert.That(p.Host.RematchRequested, Is.False, "nobody to play with");
        }

        [Test]
        public void AForfeitWinIsGivenThirtySecondsAfterTheOpponentDisconnects()
        {
            var p = new Pair();
            p.Start();
            p.PlayOne(0);
            p.Now = 100.0;
            p.GuestT.Drop();
            p.Pump();
            Assert.That(p.Host.WaitingForReconnect, Is.True);
            Assert.That(p.Host.Phase, Is.EqualTo(SessionPhase.Playing));
            Assert.That(p.Host.ReconnectSecondsLeft.Value, Is.EqualTo(30.0).Within(0.001));
            p.Now = 129.9;
            p.Pump();
            Assert.That(p.Host.Phase, Is.EqualTo(SessionPhase.Playing), "29.9 s is still waiting");
            Assert.That(p.Host.ReconnectSecondsLeft.Value, Is.EqualTo(0.1).Within(0.001));
            p.Now = 130.0;
            p.Pump();
            Assert.That(p.Host.Phase, Is.EqualTo(SessionPhase.Finished));
            Assert.That(p.Host.Ending, Is.EqualTo(EndReason.OpponentForfeit));
            Assert.That(p.Host.PeerGone, Is.True);
            Assert.That(p.Host.WaitingForReconnect, Is.False);
        }

        [Test]
        public void ReconnectingInTimeResumesAndResendsAMoveThatNeverArrived()
        {
            var p = new Pair(9001);
            p.Start();
            for (int i = 0; i < 2; i++) p.PlayOne(i);

            // 回線が切れている間に、手番の側が 1 手指す（届かない）
            p.Now = 10.0;
            p.GuestT.Drop();
            p.Pump();
            Assert.That(p.Host.WaitingForReconnect, Is.True);
            Assert.That(p.Guest.WaitingForReconnect, Is.True);
            var mover = p.Mover;
            var moves = GameEngine.LegalMoves(mover.State);
            var sent = mover.SubmitMove(moves[0]);
            p.Pump();
            var waiter = p.Waiter;
            Assert.That(waiter.TryTakeOpponentMove(out _), Is.False, "nothing is delivered while the line is down");
            Assert.That(waiter.MovesApplied, Is.EqualTo(sent.Seq));

            // 20 秒後に戻る（ゲストが繋ぎ直す）
            p.Now = 30.0;
            p.GuestT.Join(Code);
            p.Pump();
            Assert.That(p.Host.WaitingForReconnect, Is.False);
            Assert.That(p.Guest.WaitingForReconnect, Is.False);
            Assert.That(waiter.TryTakeOpponentMove(out var received), Is.True, "the lost move is sent again");
            Assert.That(received.Seq, Is.EqualTo(sent.Seq));
            Assert.That(GameEngine.HashState(p.Host.State), Is.EqualTo(GameEngine.HashState(p.Guest.State)));

            // 続きも遊べて、最後まで食い違わない
            p.PlayToEnd();
            Assert.That(GameEngine.HashState(p.Host.State), Is.EqualTo(GameEngine.HashState(p.Guest.State)));
            Assert.That(p.Host.Ending, Is.EqualTo(EndReason.Completed));
        }
    }
}
