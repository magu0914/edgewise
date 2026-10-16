using System.IO;
using System.Linq;
using Edgewise.Core.Data;
using Edgewise.EditorTools;
using Edgewise.Game.UI;
using Edgewise.Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Edgewise.Tests.EditMode
{
    /// <summary>Phase 4b: レベル加工の分岐、図鑑の操作（選択・スクロール）、クレジットの文字、シーンの fileID の正規化。</summary>
    public class Phase4bTests
    {
        [Test]
        public void LevelEffectTiersFollowTheDesign()
        {
            for (int lv = 1; lv <= 3; lv++) Assert.That(CardLevelFx.TierOf(lv), Is.EqualTo(LevelFxTier.Plain), "Lv" + lv);
            for (int lv = 4; lv <= 6; lv++) Assert.That(CardLevelFx.TierOf(lv), Is.EqualTo(LevelFxTier.Gold), "Lv" + lv);
            for (int lv = 7; lv <= 9; lv++) Assert.That(CardLevelFx.TierOf(lv), Is.EqualTo(LevelFxTier.Holo), "Lv" + lv);
            Assert.That(CardLevelFx.TierOf(10), Is.EqualTo(LevelFxTier.Aurora));
            // カード 50 枚では各段に 15 / 15 / 15 / 5 枚
            var counts = CardCatalog.All.GroupBy(c => CardLevelFx.TierOf(c.Level)).ToDictionary(g => g.Key, g => g.Count());
            Assert.That(counts[LevelFxTier.Plain], Is.EqualTo(15));
            Assert.That(counts[LevelFxTier.Gold], Is.EqualTo(15));
            Assert.That(counts[LevelFxTier.Holo], Is.EqualTo(15));
            Assert.That(counts[LevelFxTier.Aurora], Is.EqualTo(5));
        }

        [Test]
        public void CardShaderBranchesOnTheLevelEffectTier()
        {
            string shader = File.ReadAllText("Assets/Game/Art/Shaders/Card.shader");
            Assert.That(shader, Does.Contain("if (_LevelFx > 0.5) acc = goldTrim("), "Lv4+ gold trim");
            Assert.That(shader, Does.Contain("if (_LevelFx > 1.5 && _LevelFx < 2.5) acc = holo("), "Lv7-9 holo");
            Assert.That(shader, Does.Contain("if (_LevelFx > 2.5) acc = aurora("), "Lv10 aurora");
            Assert.That(shader, Does.Contain("if (_LevelFx > 2.5) acc = sparkles("), "Lv10 sparkles");
            // ホロ・粒はランクのプレートと名前の帯を避ける（fxMask）
            Assert.That(shader, Does.Contain("holo(p, acc, inside * fxMask, sil)"));
            Assert.That(shader, Does.Contain("sparkles(p, acc, inside * fxMask)"));
        }

        [Test]
        public void TiltRotationLeansTowardTheCursorUpToTwelveDegrees()
        {
            // カーソルが右: 右側が奥（+z）へ沈む
            var right = CardView.TiltRotation(new Vector2(1f, 0f)) * new Vector3(0.5f, 0f, 0f);
            Assert.That(right.z, Is.GreaterThan(0.05f));
            var up = CardView.TiltRotation(new Vector2(0f, 1f)) * new Vector3(0f, 0.5f, 0f);
            Assert.That(up.z, Is.GreaterThan(0.05f));
            float angle = Quaternion.Angle(Quaternion.identity, CardView.TiltRotation(new Vector2(1f, 0f)));
            Assert.That(angle, Is.EqualTo(CardView.MaxTiltDegrees).Within(0.01f));
        }

        [Test]
        public void DexGridKeyboardNavigationStaysInsideTheList()
        {
            var g = new DexGrid(50, 10);
            Assert.That(g.Rows, Is.EqualTo(5));
            g.Select(0);
            Assert.That(g.Move(-1, 0), Is.False, "left at the first card stays");
            Assert.That(g.Move(0, -1), Is.False, "up on the first row stays");
            Assert.That(g.Move(1, 0), Is.True);
            Assert.That(g.Selected, Is.EqualTo(1));
            g.Select(9);
            g.Move(1, 0);
            Assert.That(g.Selected, Is.EqualTo(10), "right at the end of a row goes to the next row");
            g.Move(0, 1);
            Assert.That(g.Selected, Is.EqualTo(20), "down keeps the column");
            g.Select(45);
            Assert.That(g.Move(0, 1), Is.False, "down on the last row stays");
            g.Select(49);
            Assert.That(g.Move(1, 0), Is.False);

            // 縦画面の 5 列: 列が変わっても選択は保たれる
            g.SetColumns(5);
            Assert.That(g.Rows, Is.EqualTo(10));
            g.Select(47);
            g.Move(0, -1);
            Assert.That(g.Selected, Is.EqualTo(42));

            // 最後の行が短いとき、下へは最後のカード
            var h = new DexGrid(7, 5);
            h.Select(4);
            h.Move(0, 1);
            Assert.That(h.Selected, Is.EqualTo(6));
        }

        [Test]
        public void DexZoomStepWrapsAround()
        {
            var g = new DexGrid(50, 10);
            g.Select(49);
            Assert.That(g.Step(+1), Is.EqualTo(0));
            Assert.That(g.Step(-1), Is.EqualTo(49));
            g.Select(10);
            Assert.That(g.Step(-1), Is.EqualTo(9));
        }

        [Test]
        public void DexScrollKeepsTheSelectionVisible()
        {
            Assert.That(DexGrid.ClampScroll(-3f, 10f, 4f), Is.EqualTo(0f));
            Assert.That(DexGrid.ClampScroll(9f, 10f, 4f), Is.EqualTo(6f));
            Assert.That(DexGrid.ClampScroll(2f, 3f, 4f), Is.EqualTo(0f), "content that fits does not scroll");
            var g = new DexGrid(50, 5); // 10 行
            g.Select(0);
            float s = g.ScrollToShow(0f, 1f, 3.5f);
            Assert.That(s, Is.EqualTo(0f));
            g.Select(29); // 6 行目（0 始まりで 5）
            s = g.ScrollToShow(0f, 1f, 3.5f);
            Assert.That(s, Is.EqualTo(2.5f).Within(1e-4), "scroll so that the bottom of row 5 is visible");
            g.Select(3);
            s = g.ScrollToShow(s, 1f, 3.5f);
            Assert.That(s, Is.EqualTo(0f));
        }

        [Test]
        public void DexShowsElementAndTribeNamesForEveryCard()
        {
            foreach (var c in CardCatalog.All)
            {
                Assert.That(CardDex.ElementName(c.Element), Is.Not.Empty);
                Assert.That(CardDex.TribeName(c.Tribe), Is.Not.Empty);
            }
            Assert.That(CardDex.ElementName(null), Is.EqualTo(UiText.DexNone));
        }

        [Test]
        public void CreditsTextUsesOnlyBakedGlyphsAndCreditsEveryIcon()
        {
            string text = CreditsScreen.Compose();
            // リッチテキストのタグを除いた文字がすべてフォントに焼かれていること
            string plain = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
            string charset = FontBuilder.Charset();
            var missing = plain.Where(ch => ch != '\n' && charset.IndexOf(ch) < 0).Distinct().ToArray();
            Assert.That(missing, Is.Empty, "characters not in the font: " + new string(missing));
            foreach (var a in CardArtTable.Assignments)
            {
                Assert.That(text, Does.Contain(CardCatalog.ById[a.cardId].Name + " — " + a.icon + " (" + CardArtTable.AuthorDisplayName(a.author)));
            }
            Assert.That(text, Does.Contain("CC BY 3.0"));
            Assert.That(text, Does.Contain("Noto Sans JP"));
            Assert.That(text, Does.Contain("OpenTracks"));
            Assert.That(text, Does.Contain(UiText.CreditsHomageBody));
        }

        [Test]
        public void SceneFileIdsAreDeterministicRegardlessOfTheOriginalIds()
        {
            string a = Scene(111111111, 222222222, 333333333, 444444444);
            string b = Scene(987654321, 123123123, 555555555, 777777777);
            string na = SceneFileIds.NormalizeText(a);
            string nb = SceneFileIds.NormalizeText(b);
            Assert.That(nb, Is.EqualTo(na), "same hierarchy -> same file");
            Assert.That(SceneFileIds.NormalizeText(na), Is.EqualTo(na), "idempotent");
            Assert.That(na, Does.Not.Contain("111111111"));
            Assert.That(na, Does.Contain("--- !u!29 &1"), "fixed settings ids stay");
            // 参照が付け直されていること（Transform → GameObject）
            var goId = System.Text.RegularExpressions.Regex.Match(na, @"--- !u!1 &(\d+)\nGameObject:\n(?:.*\n)*?  m_Name: Root").Groups[1].Value;
            Assert.That(na, Does.Contain("m_GameObject: {fileID: " + goId + "}"));
        }

        static string Scene(long rootGo, long rootTr, long childGo, long childTr) =>
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n"
            + "--- !u!29 &1\nOcclusionCullingSettings:\n  m_ObjectHideFlags: 0\n"
            + "--- !u!1 &" + rootGo + "\nGameObject:\n  m_Component:\n  - component: {fileID: " + rootTr + "}\n  m_Layer: 0\n  m_Name: Root\n"
            + "--- !u!4 &" + rootTr + "\nTransform:\n  m_GameObject: {fileID: " + rootGo + "}\n  m_Children:\n  - {fileID: " + childTr + "}\n  m_Father: {fileID: 0}\n"
            + "--- !u!1 &" + childGo + "\nGameObject:\n  m_Component:\n  - component: {fileID: " + childTr + "}\n  m_Layer: 0\n  m_Name: Child\n"
            + "--- !u!4 &" + childTr + "\nTransform:\n  m_GameObject: {fileID: " + childGo + "}\n  m_Children: []\n  m_Father: {fileID: " + rootTr + "}\n"
            + "--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_Roots:\n  - {fileID: " + rootTr + "}\n";
    }
}
