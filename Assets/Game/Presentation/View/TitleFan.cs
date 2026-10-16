using System.Collections.Generic;
using Edgewise.Core;
using Edgewise.Core.Data;
using UnityEngine;

namespace Edgewise.Game.View
{
    /// <summary>
    /// タイトル画面の演出（DESIGN §7-1 見せ場）: ロゴの背後で、高レベルのカードが扇状に並んでゆっくり回る。
    /// カードは扇の弧に沿って少しずつ進み、端で薄れて反対の端から戻ってくる（回り続ける扇）。
    /// ロゴと「はじめる」を読みやすく保つため、カードは半透明にして、扇の中心はロゴの少し下に置く。
    /// </summary>
    public sealed class TitleFan : MonoBehaviour
    {
        /// <summary>扇に並べるカード（Lv8〜10 から、色と形がばらけるように）。</summary>
        public static readonly string[] CardIds =
        {
            "andromede", "pegasion", "ophiwyrm", "sextantor", "ursamaw", "orionmane", "cygnwraith", "scorpidrake", "nocturnis", "polarion",
        };

        /// <summary>扇の開き（度）。カードはこの範囲を回り、端で薄れる。</summary>
        public const float Spread = 110f;

        /// <summary>回る速さ（度 / 秒）。</summary>
        public const float Speed = 3.2f;

        public const float MaxAlpha = 0.8f;

        [SerializeField] GameAssets assets;
        [SerializeField] TableLayout layout;

        readonly List<CardView> _cards = new List<CardView>();
        float _angle;

        public IReadOnlyList<CardView> Cards => _cards;

        public void Wire(GameAssets a, TableLayout tableLayout)
        {
            assets = a;
            layout = tableLayout;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            if (_cards.Count == 0)
            {
                foreach (var id in CardIds)
                {
                    var c = Instantiate(assets.cardPrefab, transform);
                    c.name = "Fan " + id;
                    c.Bind(CardCatalog.ById[id], Player.P0, 0, false);
                    _cards.Add(c);
                }
            }
            Place(0f);
        }

        public void Hide() => gameObject.SetActive(false);

        void Update() => Place(Time.unscaledDeltaTime);

        /// <summary>カード i の扇の上での角度（度、0 が真上、+ が左回り）。端を越えたら反対の端へ回る。</summary>
        public static float SlotAngle(int index, int count, float angle)
        {
            float step = (Spread + Spread / (count - 1)) / count;
            float a = index * step + angle;
            float span = step * count;
            a = Mathf.Repeat(a, span) - Spread * 0.5f - step * 0.5f;
            return a;
        }

        void Place(float dt)
        {
            if (layout == null || _cards.Count == 0) return;
            _angle += Speed * dt;
            float h = layout.VisibleHeight;
            float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
            float w = h * aspect;
            bool portrait = layout.Portrait;
            // 構図: 扇の要をロゴのずっと下に置き、半径を大きくして、ロゴの上と左右を囲む弧にする（カードがロゴを飾る）。
            // ロゴと副題の範囲（楕円）に近いカードはさらに薄くし、文字の後ろはハロー（TitleScreen）で暗く沈める
            Vector3 pivot;
            float radius, scale, logoCy, logoRx, logoRy;
            if (portrait)
            {
                pivot = new Vector3(0f, -0.01f * h, 0f);
                radius = 0.366f * h;
                scale = 0.7f;
                logoCy = 0.129f * h;
                logoRx = 0.47f * w;
                logoRy = 0.085f * h;
            }
            else
            {
                pivot = new Vector3(0f, -0.778f * h, 0f);
                radius = 1.167f * h;
                scale = 0.78f;
                logoCy = 0.115f * h;
                logoRx = 0.66f * h;
                logoRy = 0.19f * h;
            }
            float t = Time.unscaledTime;
            for (int i = 0; i < _cards.Count; i++)
            {
                float a = SlotAngle(i, _cards.Count, _angle);
                float rad = a * Mathf.Deg2Rad;
                var pos = pivot + new Vector3(-Mathf.Sin(rad) * radius, Mathf.Cos(rad) * radius, 0f);
                var c = _cards[i];
                // 放射状より少し立てる（下の角がロゴへ食い込まないように）
                c.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 0f, a * RotationFollow));
                c.transform.localScale = Vector3.one * scale;
                // 端で薄れる（中央ほど濃い）。ロゴの楕円に近いほど薄い。手前に来るほど描画順を上に
                float edge = Mathf.Abs(a) / (Spread * 0.5f);
                float ex = pos.x / logoRx, ey = (pos.y - logoCy) / logoRy;
                float nearLogo = Mathf.Sqrt(ex * ex + ey * ey);
                float alpha = MaxAlpha * Mathf.Clamp01((1f - edge) * 3f) * LogoFade(nearLogo);
                c.SetAlpha(alpha);
                c.SetOrder(5 + Mathf.RoundToInt((1f - edge) * 10f));
                // ホロ・オーロラの光が流れるように、ゆっくり傾ける
                c.SetTiltTarget(new Vector2(Mathf.Sin(t * 0.6f + i * 1.3f) * 0.6f, Mathf.Sin(t * 0.45f + i * 0.7f) * 0.4f));
            }
        }

        /// <summary>カードの回転を、弧の角度にどれだけ合わせるか（1 = 放射状）。</summary>
        public const float RotationFollow = 0.6f;

        /// <summary>ロゴの楕円（1 = 楕円の縁）からの距離による濃さ（縁の内側ではほぼ消え、離れると元の濃さ）。</summary>
        public static float LogoFade(float ellipseDistance) =>
            Mathf.Lerp(0.08f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.15f, 1.7f, ellipseDistance)));
    }
}