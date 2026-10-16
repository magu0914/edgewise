using Edgewise.Core.Campaign;
using Edgewise.Game.App;
using Edgewise.Game.View;
using TMPro;
using UnityEngine;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 立ち絵のコンタクトシート（5b-2 の監査用）。URL に ?portraits（と ?log）を付けたときだけ、タイトルの上に 12 人を並べる:
    /// 大きな立ち絵（会話の画面と同じ）・小さな印（円・主は冠）・肩書きと名前・アイコン（作者）。実際のゲームと同じ描画で確かめるため、実行時に作る。
    /// </summary>
    public static class PortraitSheet
    {
        public static GameObject Show(Transform canvas, GameAssets a)
        {
            var root = UiKit.Rect(canvas, "PortraitSheet");
            UiKit.Stretch(root);
            UiKit.Fill(root, "Back", new Color(0.02f, 0.025f, 0.06f, 0.97f));
            var table = a.cardArtTable;
            var opponents = CampaignData.Opponents;
            const int cols = 6;
            const float cw = 208f, ch = 340f;
            for (int i = 0; i < opponents.Count; i++)
            {
                var o = opponents[i];
                int col = i % cols, row = i / cols;
                var cell = UiKit.Rect(root, "Cell " + o.Id);
                UiKit.Place(cell, new Vector2(0.5f, 0.5f), new Vector2((col - (cols - 1) * 0.5f) * cw, row == 0 ? 172f : -172f), new Vector2(cw - 8f, ch - 8f));
                var frame = UiKit.Panel(cell, "Frame", a, new Color(0.06f, 0.07f, 0.15f, 0.9f), false);
                UiKit.Stretch(frame.rectTransform);
                var big = PortraitView.Create(cell, "Big", a, true);
                UiKit.Place((RectTransform)big.transform, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(188f, 188f));
                big.Show(o);
                var badge = PortraitView.Create(cell, "Badge", a);
                UiKit.Place((RectTransform)badge.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 70f), new Vector2(64f, 64f));
                badge.Show(o);
                var name = UiKit.Label(cell, "Name", a, o.Title + "\n<size=125%><b>" + o.Name + "</b></size>", 16, Theme.Ink, TextAlignmentOptions.TopLeft);
                UiKit.Place(name.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(86f, 70f), new Vector2(116f, 64f));
                var e = table != null ? table.Find(CardArtTable.PortraitId(o.Id)) : null;
                var icon = UiKit.Label(cell, "Icon", a, e != null ? e.icon + "\n(" + CardArtTable.AuthorDisplayName(e.author) + ")" : "?", 14, Theme.Gold);
                UiKit.Place(icon.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(196f, 50f));
            }
            if (AutomationLog.Enabled) AutomationLog.Write("portraits ready " + opponents.Count);
            return root.gameObject;
        }
    }
}
