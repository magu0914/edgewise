using Edgewise.Core;
using TMPro;
using UnityEngine;

namespace Edgewise.Game.View
{
    /// <summary>
    /// カード 1 枚の見た目（表示だけ。ルールは持たない）。ワールド空間のクアッド 1 枚＋TextMeshPro の文字。
    /// 本体の絵（枠・カード絵＝星雲・シルエット・星座・影・光）は Edgewise/Card シェーダーが MaterialPropertyBlock の値から描く。
    /// ランクの数字は Pivot で回しても常に正立させ、値だけを回転後の並びにする（読みやすさ優先）。カード絵は回す。
    /// </summary>
    public sealed class CardView : MonoBehaviour
    {
        public static readonly Vector2 Size = new Vector2(1f, 1.25f);
        public static readonly Vector2 QuadSize = new Vector2(1.24f, 1.49f);

        [SerializeField] MeshRenderer body;
        [SerializeField] TextMeshPro[] ranks = new TextMeshPro[4];
        [SerializeField] TextMeshPro nameText;
        [SerializeField] TextMeshPro elementText;
        [SerializeField] TextMeshPro tribeText;
        [SerializeField] TextMeshPro markText;
        [SerializeField] TextMeshPro modText;

        /// <summary>カード絵（SDF アトラスと星座）。Build で GameAssets から受け取る。</summary>
        [SerializeField] CardArtAtlas art;

        static readonly int OwnerColorId = Shader.PropertyToID("_OwnerColor");
        static readonly int ElemColorId = Shader.PropertyToID("_ElemColor");
        static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        static readonly int PatternId = Shader.PropertyToID("_Pattern");
        static readonly int ElemOnId = Shader.PropertyToID("_ElemOn");
        static readonly int TribeOnId = Shader.PropertyToID("_TribeOn");
        static readonly int ArtRectId = Shader.PropertyToID("_ArtRect");
        static readonly int ArtCountId = Shader.PropertyToID("_ArtCount");
        static readonly int ArtStarsId = Shader.PropertyToID("_ArtStars");
        static readonly int ArtLinesId = Shader.PropertyToID("_ArtLines");
        static readonly int TribeId = Shader.PropertyToID("_Tribe");
        static readonly int LevelFxId = Shader.PropertyToID("_LevelFx");
        static readonly int TiltId = Shader.PropertyToID("_Tilt");
        static readonly int ArtTexId = Shader.PropertyToID("_ArtTex");
        static readonly Vector4[] StarBuffer = new Vector4[CardArtAtlas.MaxStars];
        static readonly Vector4[] LineBuffer = new Vector4[CardArtAtlas.MaxLines];
        static readonly int EmblemRotId = Shader.PropertyToID("_EmblemRot");
        static readonly int LevelId = Shader.PropertyToID("_Level");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int SelectId = Shader.PropertyToID("_Select");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        static readonly int FaceDownId = Shader.PropertyToID("_FaceDown");
        static readonly int LiftId = Shader.PropertyToID("_Lift");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        static readonly int PivotId = Shader.PropertyToID("_Pivot");
        static readonly int MarkDirId = Shader.PropertyToID("_MarkDir");
        Vector2 _markDir;

        MaterialPropertyBlock _mpb;
        bool _dirty = true;
        float _glow, _select, _alpha = 1f, _lift, _flash, _emblemRot, _levelFx;
        Vector2 _tilt;
        Color _glowColor = Color.white;
        int _order;

        public CardDef Def { get; private set; }
        public Player Owner { get; private set; }
        public int Rotation { get; private set; }
        public bool FaceDown { get; private set; }

        /// <summary>手札・盤のどこにいるか（表示側の管理用）。</summary>
        public Player HomeSide { get; set; }

        /// <summary>レイアウト上の基準の大きさ（手札の縮小など）。演出の拡大はこれに掛ける。</summary>
        public float BaseScale { get; set; } = 1f;

        public string CardId => Def?.Id;

        /// <summary>ランクの菱形の中心と間隔（シェーダーのプレートと合わせる）。</summary>
        public static readonly Vector2 RankCenter = new Vector2(-0.235f, 0.32f);
        public const float RankStepX = 0.135f;
        public const float RankStepY = 0.142f;
        public const float RankFontSize = 2.35f;

        // ------------------------------------------------------------------
        // 生成（Editor の ProjectSetup がプレハブを作るときに呼ぶ。実行時は呼ばない）
        // ------------------------------------------------------------------
        public void Build(GameAssets assets)
        {
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(transform, false);
            bodyGo.transform.localScale = new Vector3(QuadSize.x, QuadSize.y, 1f);
            bodyGo.AddComponent<MeshFilter>().sharedMesh = assets.quad;
            body = bodyGo.AddComponent<MeshRenderer>();
            body.sharedMaterial = assets.cardMaterial;
            body.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            body.receiveShadows = false;
            art = assets.cardArt;

            // 原作と同じく左上に菱形（上・右・下・左）に並べる。下地はシェーダーの濃いプレート（中心をそろえる）。
            // 小さく表示される縦画面の手札でも読めるように、太字＋暗い縁取りで大きめにし、4 つの間隔を空ける
            var rc = RankCenter;
            var rankRect = new Vector2(0.22f, 0.22f);
            ranks = new TextMeshPro[4];
            ranks[0] = Text("RankTop", assets, rc + new Vector2(0f, RankStepY), RankFontSize, rankRect, assets.fontOutline);
            ranks[1] = Text("RankRight", assets, rc + new Vector2(RankStepX, 0f), RankFontSize, rankRect, assets.fontOutline);
            ranks[2] = Text("RankBottom", assets, rc + new Vector2(0f, -RankStepY), RankFontSize, rankRect, assets.fontOutline);
            ranks[3] = Text("RankLeft", assets, rc + new Vector2(-RankStepX, 0f), RankFontSize, rankRect, assets.fontOutline);
            foreach (var r in ranks)
            {
                r.fontStyle = FontStyles.Bold;
                r.horizontalAlignment = HorizontalAlignmentOptions.Center;
                r.verticalAlignment = VerticalAlignmentOptions.Geometry;
                r.color = Color.white;
            }
            elementText = Text("Element", assets, new Vector2(0.335f, 0.47f), 1.3f, new Vector2(0.2f, 0.2f), null);
            tribeText = Text("Tribe", assets, new Vector2(0.335f, 0.255f), 1.05f, new Vector2(0.18f, 0.18f), null);
            nameText = Text("Name", assets, new Vector2(-0.045f, -0.525f), 1.15f, new Vector2(0.78f, 0.16f), null);
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 0.6f;
            nameText.fontSizeMax = 1.15f;
            markText = Text("Mark", assets, new Vector2(0.385f, -0.525f), 1.25f, new Vector2(0.16f, 0.16f), null);
            modText = Text("Modifier", assets, new Vector2(0.04f, -0.035f), 3.2f, new Vector2(0.6f, 0.4f), assets.fontOutline);
            modText.gameObject.SetActive(false);
        }

        TextMeshPro Text(string name, GameAssets assets, Vector2 pos, float size, Vector2 rect, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(pos.x, pos.y, -0.001f);
            var t = go.AddComponent<TextMeshPro>();
            t.font = assets.font;
            if (mat != null) t.fontSharedMaterial = mat;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.rectTransform.sizeDelta = rect;
            t.color = Theme.Ink;
            t.text = "";
            var r = t.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return t;
        }

        // ------------------------------------------------------------------
        // 表示の更新
        // ------------------------------------------------------------------
        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>カードの中身を設定する。</summary>
        public void Bind(CardDef def, Player owner, int rotation, bool faceDown)
        {
            Def = def;
            Owner = owner;
            Rotation = rotation & 3;
            FaceDown = faceDown || def == null;
            _emblemRot = -Rotation * Mathf.PI * 0.5f;
            RefreshTexts();
            _dirty = true;
            Apply();
        }

        public void SetOwner(Player owner)
        {
            Owner = owner;
            RefreshTexts();
            _dirty = true;
        }

        /// <summary>Pivot の回転（0..3、時計回り 90° 単位）。数字は回転後の値で正立表示する。</summary>
        public void SetRotation(int rotation)
        {
            Rotation = rotation & 3;
            _emblemRot = -Rotation * Mathf.PI * 0.5f;
            RefreshTexts();
            _dirty = true;
        }

        public void SetFaceDown(bool faceDown)
        {
            FaceDown = faceDown || Def == null;
            RefreshTexts();
            _dirty = true;
        }

        /// <summary>Elemental の補正（+1 / −1 / 0）を表示する。値はエンジンの Board.ElementalModifier から得る。</summary>
        public void SetModifier(int mod)
        {
            if (modText == null) return;
            modText.gameObject.SetActive(mod != 0 && !FaceDown);
            modText.text = mod > 0 ? "+" + mod : mod < 0 ? "−" + (-mod) : "";
            modText.color = mod > 0 ? new Color(0.55f, 1f, 0.6f) : new Color(1f, 0.55f, 0.55f);
        }

        public void SetGlow(float amount, Color color)
        {
            if (Mathf.Approximately(_glow, amount) && _glowColor == color) return;
            _glow = amount;
            _glowColor = color;
            _dirty = true;
        }

        /// <summary>予告の「取れる」印。dir は置く位置からこのカードへの向き（ゼロで消す）。</summary>
        public void SetCaptureMark(Vector2 dir)
        {
            if (_markDir == dir) return;
            _markDir = dir;
            _dirty = true;
        }

        public void SetSelect(float amount)
        {
            if (Mathf.Approximately(_select, amount)) return;
            _select = amount;
            _dirty = true;
        }

        public void SetLift(float lift)
        {
            if (Mathf.Approximately(_lift, lift)) return;
            _lift = lift;
            _dirty = true;
        }

        /// <summary>
        /// 傾き（DESIGN §7-1 の 5。x = 右へ、y = 上へ、各 -1..1）。シェーダーは層ごとにずらす（背景は少し、星は大きく）。
        /// 4a では呼び出し元がなく常に 0（口だけ用意）。
        /// </summary>
        public void SetTilt(Vector2 tilt)
        {
            if (_tilt == tilt) return;
            _tilt = tilt;
            _dirty = true;
        }

        /// <summary>レベルごとの加工の強さ（DESIGN §7-1 の 4。4b で実装。4a では 0）。</summary>
        public void SetLevelFx(float amount)
        {
            if (Mathf.Approximately(_levelFx, amount)) return;
            _levelFx = amount;
            _dirty = true;
        }

        public void SetFlash(float flash)
        {
            if (Mathf.Approximately(_flash, flash)) return;
            _flash = flash;
            _dirty = true;
        }

        public float Alpha => _alpha;

        public void SetAlpha(float alpha)
        {
            if (Mathf.Approximately(_alpha, alpha)) return;
            _alpha = alpha;
            for (int i = 0; i < ranks.Length; i++) ranks[i].alpha = alpha;
            nameText.alpha = alpha;
            elementText.alpha = alpha;
            tribeText.alpha = alpha;
            markText.alpha = alpha;
            modText.alpha = alpha;
            _dirty = true;
        }

        /// <summary>描画順（大きいほど手前）。本体 = order*10、文字 = order*10+2。</summary>
        public void SetOrder(int order)
        {
            _order = order;
            body.sortingOrder = order * 10;
            for (int i = 0; i < ranks.Length; i++) ranks[i].sortingOrder = order * 10 + 2;
            nameText.sortingOrder = order * 10 + 2;
            elementText.sortingOrder = order * 10 + 2;
            tribeText.sortingOrder = order * 10 + 2;
            markText.sortingOrder = order * 10 + 2;
            modText.sortingOrder = order * 10 + 3;
        }

        public int Order => _order;

        void RefreshTexts()
        {
            bool show = !FaceDown && Def != null;
            for (int i = 0; i < ranks.Length; i++) ranks[i].gameObject.SetActive(show);
            nameText.gameObject.SetActive(show);
            elementText.gameObject.SetActive(show && Def.Element.HasValue);
            tribeText.gameObject.SetActive(show && Def.Tribe.HasValue);
            markText.gameObject.SetActive(true);
            markText.text = Theme.Mark(Owner);
            markText.color = Theme.OwnerColor(Owner);
            if (!show)
            {
                modText.gameObject.SetActive(false);
                return;
            }
            var sides = Board.RotateSides(Def.Sides, Rotation);
            for (int dir = 0; dir < 4; dir++) ranks[dir].text = Theme.Rank(sides[dir]);
            nameText.text = Def.Name;
            if (Def.Element.HasValue)
            {
                elementText.text = Theme.ElementGlyph(Def.Element.Value);
                elementText.color = Color.Lerp(Theme.ElementColor(Def.Element), Color.white, 0.55f);
            }
            if (Def.Tribe.HasValue) tribeText.text = Theme.TribeGlyph(Def.Tribe.Value);
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            if (!_dirty || body == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _dirty = false;
            body.GetPropertyBlock(_mpb);
            _mpb.SetColor(OwnerColorId, Theme.OwnerColor(Owner));
            _mpb.SetFloat(PatternId, Theme.Pattern(Owner));
            _mpb.SetColor(GlowColorId, _glowColor);
            _mpb.SetFloat(GlowId, _glow);
            _mpb.SetFloat(SelectId, _select);
            _mpb.SetFloat(AlphaId, _alpha);
            _mpb.SetFloat(FaceDownId, FaceDown ? 1f : 0f);
            _mpb.SetFloat(LiftId, _lift);
            _mpb.SetFloat(FlashId, _flash);
            _mpb.SetVector(QuadSizeId, QuadSize);
            if (Def != null)
            {
                var entry = art != null ? art.Find(Def.Id) : null;
                _mpb.SetColor(ElemColorId, Theme.ElementColor(Def.Element));
                _mpb.SetFloat(ElemOnId, Def.Element.HasValue ? 1f : 0f);
                _mpb.SetFloat(TribeOnId, Def.Tribe.HasValue ? 1f : 0f);
                _mpb.SetFloat(TribeId, Def.Tribe.HasValue ? (float)(int)Def.Tribe.Value : -1f);
                CardArtAtlas.Fill(entry?.stars, StarBuffer);
                CardArtAtlas.Fill(entry?.lines, LineBuffer);
                _mpb.SetVectorArray(ArtStarsId, StarBuffer);
                _mpb.SetVectorArray(ArtLinesId, LineBuffer);
                if (art != null && art.atlas != null) _mpb.SetTexture(ArtTexId, art.atlas);
                _mpb.SetVector(ArtRectId, entry != null ? entry.uvRect : Vector4.zero);
                _mpb.SetVector(ArtCountId, entry != null
                    ? new Vector4(Mathf.Min(entry.stars.Length, CardArtAtlas.MaxStars), Mathf.Min(entry.lines.Length, CardArtAtlas.MaxLines), 1f, 0f)
                    : Vector4.zero);
                _mpb.SetFloat(LevelId, Def.Level);
                _mpb.SetFloat(LevelFxId, _levelFx);
                _mpb.SetVector(TiltId, new Vector4(_tilt.x, _tilt.y, 0f, 0f));
                _mpb.SetFloat(EmblemRotId, _emblemRot);
                _mpb.SetFloat(PivotId, Rotation);
                _mpb.SetVector(MarkDirId, new Vector4(_markDir.x, _markDir.y, 0f, 0f));
            }
            else
            {
                _mpb.SetVector(ArtCountId, Vector4.zero);
                _mpb.SetFloat(ElemOnId, 0);
                _mpb.SetFloat(TribeOnId, 0);
                _mpb.SetFloat(LevelId, 0);
            }
            body.SetPropertyBlock(_mpb);
        }

        /// <summary>今すぐ見た目を反映する（スクリーンショット・テスト用）。</summary>
        public void ApplyNow()
        {
            _dirty = true;
            Apply();
        }
    }
}
