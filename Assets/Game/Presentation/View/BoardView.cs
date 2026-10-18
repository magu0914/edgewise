using Edgewise.Core;
using TMPro;
using UnityEngine;

namespace Edgewise.Game.View
{
    /// <summary>
    /// 盤面（3×3）の見た目: 羊皮紙の盤、属性マスの印、マスのカーソル、Same/Plus の光の線、着地の光の輪、予告の札。
    /// 置かれたカード（CardView）の参照も持つが、所有者などのルール上の状態は持たない（MatchController の GameState が正）。
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        public static readonly Vector2 Pitch = new Vector2(1.06f, 1.31f);
        public static readonly Vector2 BoardSize = new Vector2(3.5f, 4.25f);

        [SerializeField] MeshRenderer boardRenderer;
        [SerializeField] Transform[] cells = new Transform[9];
        [SerializeField] MeshRenderer[] elementRings = new MeshRenderer[9];
        [SerializeField] TextMeshPro[] elementGlyphs = new TextMeshPro[9];
        [SerializeField] MeshRenderer cursor;
        [SerializeField] MeshRenderer hintMarker;
        [SerializeField] MeshRenderer[] links = new MeshRenderer[4];
        [SerializeField] MeshRenderer[] bursts = new MeshRenderer[3];
        [SerializeField] TextMeshPro[] badges = new TextMeshPro[2];
        [SerializeField] MeshRenderer[] badgePlates = new MeshRenderer[2];

        public readonly CardView[] Placed = new CardView[GameConstants.BoardSize];

        MaterialPropertyBlock _mpb;
        int _burstNext;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int ModeId = Shader.PropertyToID("_Mode");
        static readonly int SizeId = Shader.PropertyToID("_Size");
        static readonly int CornerId = Shader.PropertyToID("_Corner");
        static readonly int SoftnessId = Shader.PropertyToID("_Softness");
        static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int PulseId = Shader.PropertyToID("_Pulse");

        // ------------------------------------------------------------------
        // 生成（Editor の ProjectSetup がシーンを作るときに呼ぶ）
        // ------------------------------------------------------------------
        public void Build(GameAssets assets)
        {
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);

            boardRenderer = Quad("Board", assets, assets.boardMaterial, Vector3.zero, BoardSize, -400);

            cells = new Transform[9];
            elementRings = new MeshRenderer[9];
            elementGlyphs = new TextMeshPro[9];
            for (int c = 0; c < 9; c++)
            {
                var cell = new GameObject("Cell" + c).transform;
                cell.SetParent(transform, false);
                cell.localPosition = LocalCellPosition(c);
                cells[c] = cell;
                elementRings[c] = Quad("ElementRing", assets, assets.shapeMaterial, Vector3.zero, new Vector2(0.9f, 0.9f), -390, cell);
                elementRings[c].gameObject.SetActive(false);
                var g = new GameObject("ElementGlyph");
                g.transform.SetParent(cell, false);
                g.transform.localPosition = new Vector3(0f, 0f, -0.001f);
                var t = g.AddComponent<TextMeshPro>();
                t.font = assets.font;
                t.fontSize = 5f;
                t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.rectTransform.sizeDelta = new Vector2(0.8f, 0.8f);
                t.sortingOrder = -389;
                t.text = "";
                g.SetActive(false);
                elementGlyphs[c] = t;
            }

            cursor = Quad("Cursor", assets, assets.shapeMaterial, Vector3.zero, new Vector2(1.1f, 1.35f), -380);
            cursor.gameObject.SetActive(false);
            hintMarker = Quad("HintMarker", assets, assets.shapeMaterial, Vector3.zero, new Vector2(1.16f, 1.41f), -381);
            hintMarker.gameObject.SetActive(false);
            links = new MeshRenderer[4];
            for (int i = 0; i < links.Length; i++)
            {
                links[i] = Quad("Link" + i, assets, assets.shapeMaterial, Vector3.zero, new Vector2(1f, 0.2f), 900);
                links[i].gameObject.SetActive(false);
            }
            bursts = new MeshRenderer[9];
            for (int i = 0; i < bursts.Length; i++)
            {
                bursts[i] = Quad("Burst" + i, assets, assets.shapeMaterial, Vector3.zero, new Vector2(2f, 2f), 910);
                bursts[i].gameObject.SetActive(false);
            }
            badges = new TextMeshPro[2];
            badgePlates = new MeshRenderer[2];
            for (int i = 0; i < badges.Length; i++)
            {
                badgePlates[i] = Quad("BadgePlate" + i, assets, assets.shapeMaterial, Vector3.zero, new Vector2(0.62f, 0.24f), 920);
                var g = new GameObject("Badge" + i);
                g.transform.SetParent(badgePlates[i].transform, false);
                g.transform.localPosition = new Vector3(0f, 0f, -0.001f);
                // 親のクアッドの拡大を打ち消す
                g.transform.localScale = new Vector3(1f / 0.62f, 1f / 0.24f, 1f);
                var t = g.AddComponent<TextMeshPro>();
                t.font = assets.font;
                t.fontSharedMaterial = assets.fontOutline;
                t.fontSize = 1.6f;
                t.fontStyle = FontStyles.Bold;
                t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.rectTransform.sizeDelta = new Vector2(0.6f, 0.24f);
                t.sortingOrder = 921;
                t.color = Theme.Gold;
                badges[i] = t;
                badgePlates[i].gameObject.SetActive(false);
            }
        }

        MeshRenderer Quad(string name, GameAssets assets, Material mat, Vector3 pos, Vector2 size, int order, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = assets.quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.sortingOrder = order;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        // ------------------------------------------------------------------
        // 幾何
        // ------------------------------------------------------------------
        public static Vector3 LocalCellPosition(int cell)
        {
            int col = cell % 3;
            int row = cell / 3;
            return new Vector3((col - 1) * Pitch.x, (1 - row) * Pitch.y, 0f);
        }

        public Vector3 CellPosition(int cell) => transform.TransformPoint(LocalCellPosition(cell));

        /// <summary>ワールド座標の点がどのマスの上か（盤の外なら -1）。</summary>
        public int CellAt(Vector3 world)
        {
            var p = transform.InverseTransformPoint(world);
            int col = Mathf.RoundToInt(p.x / Pitch.x) + 1;
            int row = 1 - Mathf.RoundToInt(p.y / Pitch.y);
            if (col < 0 || col > 2 || row < 0 || row > 2) return -1;
            var c = LocalCellPosition(row * 3 + col);
            if (Mathf.Abs(p.x - c.x) > Pitch.x * 0.5f || Mathf.Abs(p.y - c.y) > Pitch.y * 0.5f) return -1;
            return row * 3 + col;
        }

        public float WorldScale => transform.lossyScale.x;

        // ------------------------------------------------------------------
        // 表示
        // ------------------------------------------------------------------
        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
        }

        void Shape(MeshRenderer r, Color color, float mode, Vector2 size, float corner = 0.08f, float softness = 0.02f, float thickness = 0.03f,
            float radius = 0.4f, float pulse = 0f)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorId, color);
            _mpb.SetFloat(ModeId, mode);
            _mpb.SetVector(SizeId, size);
            _mpb.SetFloat(CornerId, corner);
            _mpb.SetFloat(SoftnessId, softness);
            _mpb.SetFloat(ThicknessId, thickness);
            _mpb.SetFloat(RadiusId, radius);
            _mpb.SetFloat(PulseId, pulse);
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>
        /// 共鳴の縁を更新する（Resonance ルール）。同じ所有者・同じ系統のカードが縦横に隣り合っているカードを光らせる。
        /// エンジンの Board.IsResonant と同じ規則を、表示している CardView の状態から決める（勝敗は決めない）。enabled = false なら全部消す。
        /// </summary>
        public void RefreshResonance(bool enabled)
        {
            for (int c = 0; c < Placed.Length; c++)
            {
                var card = Placed[c];
                if (card == null) continue;
                bool on = false;
                var tribe = card.Def?.Tribe;
                if (enabled && tribe.HasValue)
                {
                    for (int dir = 0; dir < 4 && !on; dir++)
                    {
                        int n = Edgewise.Core.Board.Neighbor(c, dir);
                        if (n == GameConstants.Wall) continue;
                        var other = Placed[n];
                        on = other != null && other.Owner == card.Owner && other.Def != null && other.Def.Tribe == tribe;
                    }
                }
                card.SetResonance(on);
            }
        }

        public void ClearPlaced()
        {
            for (int i = 0; i < Placed.Length; i++) Placed[i] = null;
        }

        /// <summary>Elemental のマス属性を表示する。</summary>
        public void SetCellElements(ReadOnlyArray<Element?> elements)
        {
            for (int c = 0; c < 9; c++)
            {
                var e = elements.Count == 9 ? elements[c] : null;
                bool on = e.HasValue;
                elementRings[c].gameObject.SetActive(on);
                elementGlyphs[c].gameObject.SetActive(on);
                if (!on) continue;
                var col = Theme.ElementColor(e);
                Shape(elementRings[c], new Color(col.r, col.g, col.b, 0.8f), 2f, new Vector2(0.9f, 0.9f), thickness: 0.05f, radius: 0.36f);
                elementGlyphs[c].text = Theme.ElementGlyph(e.Value);
                elementGlyphs[c].color = new Color(col.r, col.g, col.b, 0.55f);
            }
        }

        /// <summary>マスのカーソル（持っているカードを重ねている・キーボードで選んでいるマス）。cell = -1 で消す。</summary>
        public void ShowCursor(int cell, Color color, bool valid)
        {
            if (cell < 0)
            {
                cursor.gameObject.SetActive(false);
                return;
            }
            cursor.gameObject.SetActive(true);
            cursor.transform.localPosition = LocalCellPosition(cell) + new Vector3(0, 0, -0.002f);
            var c = valid ? color * 1.6f : new Color(0.6f, 0.6f, 0.7f, 0.8f);
            c.a = valid ? 1f : 0.6f;
            Shape(cursor, c, 1f, new Vector2(1.1f, 1.35f), corner: 0.1f, softness: 0.025f, thickness: 0.022f, pulse: valid ? 1f : 0f);
        }

        /// <summary>ヒントのマス（脈打つ枠）。cell = -1 で消す。</summary>
        public void ShowHint(int cell, Color color)
        {
            if (hintMarker == null) return;
            if (cell < 0)
            {
                hintMarker.gameObject.SetActive(false);
                return;
            }
            hintMarker.gameObject.SetActive(true);
            hintMarker.transform.localPosition = LocalCellPosition(cell) + new Vector3(0, 0, -0.003f);
            var c = color * 1.8f;
            c.a = 1f;
            Shape(hintMarker, c, 1f, new Vector2(1.16f, 1.41f), corner: 0.12f, softness: 0.04f, thickness: 0.035f, pulse: 1f);
        }

        public bool HintVisible => hintMarker != null && hintMarker.gameObject.activeSelf;

        /// <summary>Same / Plus が成立した辺（置いたマスと cell の境目）に光の線を出す。index は線の番号（0..3）。</summary>
        public MeshRenderer ShowLink(int index, int placedCell, int cell, Color color)
        {
            if (index < 0 || index >= links.Length) return null;
            var r = links[index];
            var a = LocalCellPosition(placedCell);
            var b = LocalCellPosition(cell);
            var mid = (a + b) * 0.5f;
            bool horizontal = Mathf.Abs(a.y - b.y) > 0.01f; // 上下に並ぶなら境目は水平な線
            var size = horizontal ? new Vector2(Pitch.x * 0.95f, 0.26f) : new Vector2(0.26f, Pitch.y * 0.95f);
            r.transform.localPosition = new Vector3(mid.x, mid.y, -0.01f);
            r.transform.localScale = new Vector3(size.x, size.y, 1f);
            r.gameObject.SetActive(true);
            Shape(r, color, 0f, size, corner: 0.1f, softness: 0.09f);
            return r;
        }

        public void SetLinkAlpha(MeshRenderer r, Color color, float alpha, bool hideAtZero = false)
        {
            if (r == null) return;
            var c = color;
            c.a = alpha;
            var s = new Vector2(r.transform.localScale.x, r.transform.localScale.y);
            Shape(r, c, 0f, s, corner: 0.1f, softness: 0.09f);
            if (hideAtZero && alpha <= 0.001f) r.gameObject.SetActive(false);
        }

        public void HideLinks()
        {
            foreach (var l in links) l.gameObject.SetActive(false);
        }

        /// <summary>着地の光の輪（t = 0..1 で広がって消える）。</summary>
        public MeshRenderer NextBurst(int cell)
        {
            var r = bursts[_burstNext];
            _burstNext = (_burstNext + 1) % bursts.Length;
            r.transform.localPosition = LocalCellPosition(cell) + new Vector3(0, 0, -0.05f);
            r.gameObject.SetActive(true);
            return r;
        }

        public void SetBurst(MeshRenderer r, Color color, float t, float size = 1f)
        {
            var c = color * 2f;
            c.a = (1f - t) * 0.9f;
            float radius = Mathf.Lerp(0.18f, 0.48f, t) * Mathf.Min(size, 1.04f);
            Shape(r, c, 2f, new Vector2(2f, 2f), thickness: Mathf.Lerp(0.02f, 0.05f, t), radius: radius);
            if (t >= 1f) r.gameObject.SetActive(false);
        }

        /// <summary>予告の札（SAME / PLUS）。texts が空なら消す。</summary>
        public void ShowBadges(int cell, string first, string second)
        {
            ShowBadge(0, cell, first, 0);
            ShowBadge(1, cell, second, string.IsNullOrEmpty(first) ? 0 : 1);
        }

        void ShowBadge(int i, int cell, string text, int slot)
        {
            var plate = badgePlates[i];
            if (cell < 0 || string.IsNullOrEmpty(text))
            {
                plate.gameObject.SetActive(false);
                return;
            }
            plate.gameObject.SetActive(true);
            plate.transform.localPosition = LocalCellPosition(cell) + new Vector3(0f, Pitch.y * 0.5f + 0.05f + slot * 0.27f, -0.02f);
            Shape(plate, new Color(0.05f, 0.05f, 0.12f, 0.92f), 0f, new Vector2(0.62f, 0.24f), corner: 0.11f, softness: 0.01f);
            badges[i].text = text;
        }

        public void HideBadges()
        {
            foreach (var p in badgePlates) p.gameObject.SetActive(false);
        }
    }
}
