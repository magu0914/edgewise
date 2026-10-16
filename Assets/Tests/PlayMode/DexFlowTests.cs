using System.Collections;
using Edgewise.Core.Data;
using Edgewise.Game.App;
using Edgewise.Game.Juice;
using Edgewise.Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Edgewise.Tests.PlayMode
{
    /// <summary>Phase 4b: タイトル → カード図鑑（一覧・拡大・前後・戻る）→ タイトル、クレジット画面、タイトルの扇。</summary>
    public class DexFlowTests
    {
        AppFlow _app;

        [UnitySetUp]
        public IEnumerator LoadMain()
        {
            UserPrefs.SpeedOverride = PlaybackSpeed.Instant;
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
            _app = Object.FindAnyObjectByType<AppFlow>();
            Assert.That(_app, Is.Not.Null);
        }

        [TearDown]
        public void Reset() => UserPrefs.SpeedOverride = null;

        [UnityTest]
        public IEnumerator TitleShowsARotatingFanOfHighLevelCards()
        {
            Assert.That(_app.Fan.gameObject.activeSelf, Is.True, "the fan is shown with the title");
            Assert.That(_app.Fan.Cards.Count, Is.EqualTo(Edgewise.Game.View.TitleFan.CardIds.Length));
            foreach (var c in _app.Fan.Cards) Assert.That(c.Def.Level, Is.GreaterThanOrEqualTo(8));
            var before = _app.Fan.Cards[0].transform.position;
            for (int i = 0; i < 30; i++) yield return null;
            Assert.That(Vector3.Distance(before, _app.Fan.Cards[0].transform.position), Is.GreaterThan(0f), "the fan keeps rotating");
            _app.ShowSetup();
            Assert.That(_app.Fan.gameObject.activeSelf, Is.False, "the fan hides with the title");
        }

        [UnityTest]
        public IEnumerator DexOpensZoomsStepsAndReturnsToTheTitle()
        {
            _app.Title.DexButton.onClick.Invoke();
            yield return null;
            var dex = _app.Dex;
            Assert.That(dex.CurrentMode, Is.EqualTo(CardDex.Mode.List));
            Assert.That(dex.Cards.Count, Is.EqualTo(CardCatalog.All.Count), "all 50 cards are listed");
            Assert.That(_app.Title.gameObject.activeSelf, Is.False);

            int lv10 = CardCatalog.All.Count - 5; // andromede
            dex.OpenZoom(lv10);
            yield return new WaitForSecondsRealtime(0.5f); // 一覧の位置から拡大位置へ動く
            Assert.That(dex.CurrentMode, Is.EqualTo(CardDex.Mode.Zoom));
            Assert.That(dex.ZoomedCardId, Is.EqualTo(CardCatalog.All[lv10].Id));
            Assert.That(dex.InfoCredit, Does.Contain("angel-outfit by Lorc").And.Contain("CC BY 3.0"), "one-line icon credit");
            Assert.That(dex.ZoomCard.transform.localScale.x, Is.GreaterThan(dex.Cards[0].transform.localScale.x * 2f), "zoomed card is large");

            // 拡大中はカーソルの方へ（動かさなければゆっくり揺れて）傾いている
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.That(dex.ZoomCard.Tilt.magnitude, Is.GreaterThan(0.05f), "the zoomed card tilts");
            Assert.That(dex.ZoomCard.Tilt.magnitude, Is.LessThanOrEqualTo(1.0001f));

            dex.StepZoom(+1);
            yield return null;
            Assert.That(dex.ZoomedCardId, Is.EqualTo(CardCatalog.All[lv10 + 1].Id));
            dex.BackToList();
            yield return null;
            Assert.That(dex.CurrentMode, Is.EqualTo(CardDex.Mode.List));
            dex.BackButton.onClick.Invoke();
            yield return null;
            Assert.That(dex.IsOpen, Is.False);
            Assert.That(_app.Title.gameObject.activeSelf, Is.True, "back to the title");
        }

        [UnityTest]
        public IEnumerator CreditsOpenFromTheTitleAndClose()
        {
            _app.Title.CreditsButton.onClick.Invoke();
            yield return null;
            Assert.That(_app.Credits.IsOpen, Is.True);
            Assert.That(_app.Credits.Body, Does.Contain("Noto Sans JP").And.Contain("CC BY 3.0").And.Contain("OpenTracks"));
            _app.Credits.CloseButton.onClick.Invoke();
            yield return null;
            Assert.That(_app.Credits.IsOpen, Is.False);
            Assert.That(_app.Title.gameObject.activeSelf, Is.True);
        }
    }
}
