using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Edgewise.Core.Campaign;
using Edgewise.Game.App;
using Edgewise.Game.Campaign;
using Edgewise.Game.Juice;
using Edgewise.Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Edgewise.Tests.PlayMode
{
    /// <summary>
    /// Phase 8a（DESIGN §14-4）:
    /// 1. 会話の不具合の再発防止（2026-10-18 magu 報告。コミット「Fix previous dialogue flashing when a new conversation starts」で修正）:新しい会話を始めた最初のフレームから帯のせり上がりの間、
    ///    前の会話の本文・名前・立ち絵が見えないこと（読み終えた後・途中でスキップした後の両方）。
    /// 2. 遊び方: タイトルから開き、4 コマ（基本 3 ＋ Pivot）とルール確認への誘導が出て、閉じるとタイトルへ戻る。
    /// </summary>
    public class Phase8aPlayTests
    {
        AppFlow _app;
        CancellationTokenSource _cts;

        [UnitySetUp]
        public IEnumerator LoadMain()
        {
            // 帯がせり上がる演出（0.2 秒）が実際に走る速さにする（即時だと不具合の出るフレームが無くなる）
            UserPrefs.SpeedOverride = PlaybackSpeed.Normal;
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = Object.FindAnyObjectByType<AppFlow>();
            Assert.That(_app, Is.Not.Null);
            _cts = new CancellationTokenSource();
        }

        [UnityTearDown]
        public IEnumerator Reset()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            if (_app != null && _app.Campaign != null) _app.Campaign.Dialogue.Skip();
            // 途中で失敗しても、帯のせり上がりの Tween が終わってから次のテストのシーンを読む（壊れた参照のログで次のテストまで落とさない）
            yield return new WaitForSecondsRealtime(0.4f);
            UserPrefs.SpeedOverride = null;
        }

        static List<DialogueLine> Lines(Opponent who, IEnumerable<string> texts)
        {
            var list = new List<DialogueLine>();
            foreach (var t in texts) list.Add(new DialogueLine(who, t));
            return list;
        }

        /// <summary>前の会話の名残（本文・名前・立ち絵・▼）が見えていないこと。</summary>
        static void AssertFresh(DialogueBox box, Opponent prev, IReadOnlyList<DialogueLine> prevLines, Opponent next, string when)
        {
            Assert.That(box.IsOpen, Is.True, when + ": the band is shown");
            foreach (var l in prevLines)
            {
                Assert.That(box.BodyText, Does.Not.Contain(l.Text), when + ": the previous body text is gone");
                Assert.That(box.VisibleBody, Does.Not.Contain(l.Text));
            }
            Assert.That(box.SpeakerName, Does.Contain(next.Name).And.Not.Contain(prev.Name), when + ": the name plate shows the new speaker");
            Assert.That(box.Portrait.ArtId, Is.EqualTo("@" + next.Id), when + ": the small portrait");
            Assert.That(box.BigPortrait.ArtId, Is.EqualTo("@" + next.Id), when + ": the big portrait");
            Assert.That(box.NextShown, Is.False, when + ": no ▼ before the first line is typed");
        }

        IEnumerator NewConversationStartsClean(bool skipFirst)
        {
            var box = _app.Campaign.Dialogue;
            var mira = CampaignData.Regions[0].Opponents[0];
            var io = CampaignData.Regions[3].Boss;
            var a = Lines(mira, mira.Lines.Intro);
            var b = Lines(io, io.Lines.Intro);
            Assert.That(a.Count, Is.GreaterThan(0));
            Assert.That(b.Count, Is.GreaterThan(0));

            // 会話 A（ミラ）: 最後まで読む、または 1 行目の途中でスキップする
            _ = box.PlayAsync(a, 1f, _cts.Token);
            if (skipFirst)
            {
                float w = 0f;
                while (box.VisibleCharacters < 3 && w < 5f)
                {
                    w += Time.unscaledDeltaTime;
                    yield return null;
                }
                box.Skip();
            }
            float t = 0f;
            while (box.IsOpen && t < 30f)
            {
                t += Time.unscaledDeltaTime;
                if (!skipFirst) box.Advance();
                yield return null;
            }
            Assert.That(box.IsOpen, Is.False, "conversation A ended");
            // 前提: 閉じた帯には A の本文と名前が残っている（これが次の会話の最初に見えてしまうのが不具合だった）
            Assert.That(box.BodyText, Is.EqualTo(skipFirst ? a[0].Text : a[a.Count - 1].Text), "A's text is still set on the hidden band");
            Assert.That(box.SpeakerName, Does.Contain(mira.Name));

            // 会話 B（イオ）を始めた瞬間 = 最初のフレームが描かれる前
            _ = box.PlayAsync(b, 1f, _cts.Token, overBoard: skipFirst);
            AssertFresh(box, mira, a, io, "frame 0");
            // 帯がせり上がる間（0.2 秒）と、1 行目を打ち始めるまで、毎フレーム確かめる
            t = 0f;
            int frames = 0;
            while (t < 0.5f && box.IsOpen)
            {
                yield return null;
                t += Time.unscaledDeltaTime;
                frames++;
                foreach (var l in a) Assert.That(box.VisibleBody, Does.Not.Contain(l.Text), "frame " + frames + ": A's text");
                Assert.That(box.SpeakerName, Does.Contain(io.Name), "frame " + frames + ": name plate");
                Assert.That(box.Portrait.ArtId, Is.EqualTo("@" + io.Id), "frame " + frames + ": portrait");
                Assert.That(io.Lines.Intro[0], Does.StartWith(box.VisibleBody), "frame " + frames + ": only B's first line, typed so far");
            }
            Assert.That(frames, Is.GreaterThan(1), "watched the band slide in");
            box.Skip();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ANewConversationNeverShowsThePreviousOneAfterReadingItToTheEnd()
        {
            yield return NewConversationStartsClean(false);
        }

        [UnityTest]
        public IEnumerator ANewConversationNeverShowsThePreviousOneAfterSkippingIt()
        {
            yield return NewConversationStartsClean(true);
        }

        [UnityTest]
        public IEnumerator HowToOpensFromTheTitleWithFourPanelsAndCloses()
        {
            Assert.That(_app.Title.HowToButton, Is.Not.Null);
            Assert.That(_app.Title.HowToButton.gameObject.activeInHierarchy, Is.True, "the title has a 遊び方 button");
            _app.Title.HowToButton.onClick.Invoke();
            yield return null;
            var h = _app.HowTo;
            Assert.That(h.IsOpen, Is.True);
            Assert.That(_app.Title.gameObject.activeSelf, Is.False);
            Assert.That(_app.Fan.gameObject.activeSelf, Is.True, "the fan keeps turning behind");
            Assert.That(HowToScreen.PanelCount, Is.EqualTo(4));
            Assert.That(h.FrameTitle(1), Does.Contain("裏返す"));
            Assert.That(h.FrameTitle(3), Does.Contain("Pivot"));
            Assert.That(h.RulesNote, Does.Contain("ルール確認"), "the other rules are in the match menu");
            // 図が動く: 相手のカードが自分の色に裏返る瞬間がある
            float t = 0f;
            while (!h.FlipShowsMine && t < 4f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(h.FlipShowsMine, Is.True, "the flip diagram animates");
            h.CloseButton.onClick.Invoke();
            yield return null;
            Assert.That(h.IsOpen, Is.False);
            Assert.That(_app.Title.gameObject.activeSelf, Is.True, "back to the title");
        }
    }
}
