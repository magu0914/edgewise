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

        /// <summary>Trait のバッジの漢字 1 字（右下寄りの丸の上。Traits ルールの対戦と図鑑で出す。Phase 6）。</summary>
        [SerializeField] TextMeshPro traitText;

        /// <summary>図鑑の収集表示の枚数（×2）。5b-2。</summary>
        [SerializeField] TextMeshPro countText;

        /// <summary>カード絵（SDF アトラスと星座）。Build で GameAssets から受け取る。</summary>
        [SerializeField] CardArtAtlas art;

        /// <summary>見た目（本体のクアッドと文字）をまとめた子。傾き（§7-1 の層 5）はこれだけを回す（裏返しなどの演出は親を回す）。</summary>
        [SerializeField] Transform visual;

        /// <summary>傾きの最大角（度）。</summary>
        public const float MaxTiltDegrees = 12f;

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
        static readonly int UnknownId = Shader.PropertyToID("_Unknown");
        static readonly int LiftId = Shader.PropertyToID("_Lift");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        static readonly int PivotId = Shader.PropertyToID("_Pivot");
        static readonly int MarkDirId = Shader.PropertyToID("_MarkDir");
        static readonly int TraitOnId = Shader.PropertyToID("_TraitOn");
        static readonly int TraitColorId = Shader.PropertyToID("_TraitColor");
        static readonly int TraitUsedId = Shader.PropertyToID("_TraitUsed");
        static readonly int ResonanceId = Shader.PropertyToID("_Resonance");
        static readonly int ShieldId = Shader.PropertyToID("_Shield");
        Vector2 _markDir;

        MaterialPropertyBlock _mpb;
        bool _dirty = true;
        float _glow, _select, _alpha = 1f, _lift, _flash, _emblemRot;
        bool _showTraits, _traitUsed, _resonance;
        float _shield;
        Vector2 _tilt;
        Vector2 _tiltTarget;
        Color _glowColor = Color.white;
        int _order;

        public CardDef Def { get; private set; }
        public Player Owner { get; private set; }
        public int Rotation { get; private set; }
        public bool FaceDown { get; private set; }

        /// <summary>未入手（図鑑の収集表示）: 暗いシルエットだけで、数字・名前を伏せる。</summary>
        public bool Unknown { get; private set; }

        /// <summary>表示している枚数（0 なら出していない）。</summary>
        public int Count { get; private set; }

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

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(visual, false);
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
            // 図鑑の枚数（ランクのプレートと属性の丸の間）
            // Trait のバッジの漢字（属性・系統のバッジの下、右の列）。丸と縁はシェーダーが描く
            traitText = Text("Trait", assets, new Vector2(0.352f, -0.262f), 1.2f, new Vector2(0.17f, 0.17f), assets.fontOutline);
            traitText.fontStyle = FontStyles.Bold;
            traitText.gameObject.SetActive(false);
            countText = Text("Count", assets, new Vector2(0.115f, 0.49f), 1.5f, new Vector2(0.26f, 0.18f), assets.fontOutline);
            countText.fontStyle = FontStyles.Bold;
            countText.color = Theme.Gold;
            countText.gameObject.SetActive(false);
        }

        TextMeshPro Text(string name, GameAssets assets, Vector2 pos, float size, Vector2 rect, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(visual, false);
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
            Unknown = false;
            Count = 0;
            _traitUsed = false;
            _resonance = false;
            _shield = 0f;
            _emblemRot = -Rotation * Mathf.PI * 0.5f;
            RefreshTexts();
            _dirty = true;
            Apply();
        }

        /// <summary>未入手の表示（図鑑の収集表示）。</summary>
        public void SetUnknown(bool unknown)
        {
            if (Unknown == unknown) return;
            Unknown = unknown;
            RefreshTexts();
            _dirty = true;
        }

        /// <summary>持っている枚数のバッジ（0 で消す）。</summary>
        public void SetCount(int count)
        {
            Count = count;
            if (countText == null) return;
            bool show = count > 0 && !FaceDown && !Unknown;
            countText.gameObject.SetActive(show);
            if (show) countText.text = "×" + count;
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

        /// <summary>Trait のバッジを出すか。Traits ルールの対戦と図鑑で true にする（既定は false）。</summary>
        public void SetTraitsEnabled(bool on)
        {
            if (_showTraits == on) return;
            _showTraits = on;
            RefreshTexts();
            _dirty = true;
        }

        /// <summary>Trait のバッジを出している（カードが Trait を持ち、表示が ON で、表向き）。</summary>
        public bool TraitBadgeVisible => _showTraits && Def != null && Def.Trait.HasValue && !FaceDown && !Unknown;

        /// <summary>バッジの漢字（出していなければ空）。</summary>
        public string TraitBadgeText => traitText != null && traitText.gameObject.activeSelf ? traitText.text : "";

        /// <summary>Guard を使い切った（バッジをくすませる）。</summary>
        public void SetTraitUsed(bool used)
        {
            if (_traitUsed == used) return;
            _traitUsed = used;
            if (traitText != null) traitText.alpha = used ? 0.4f * _alpha : _alpha;
            _dirty = true;
        }

        public bool TraitUsed => _traitUsed;

        /// <summary>共鳴している（縁が系統の色で光る）。</summary>
        public void SetResonance(bool on)
        {
            if (_resonance == on) return;
            _resonance = on;
            _dirty = true;
        }

        public bool Resonating => _resonance;

        /// <summary>Guard が防いだ演出の盾の強さ（0..1）。</summary>
        public void SetShield(float amount)
        {
            if (Mathf.Approximately(_shield, amount)) return;
            _shield = amount;
            _dirty = true;
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
        /// 傾けたい向き（DESIGN §7-1 の 5。x = カーソルが右、y = カーソルが上、各 -1..1。ゼロで水平に戻る）。
        /// カードはカーソルの方へ最大 <see cref="MaxTiltDegrees"/>° 傾き（カーソル側が奥へ沈む）、シェーダーは層ごとにずらして
        /// 奥行きを出す（背景は少し、シルエット、星は大きく）。ホロの光沢も傾きに連動する。毎フレームなめらかに追いかける。
        /// </summary>
        public void SetTiltTarget(Vector2 target)
        {
            _tiltTarget = Vector2.ClampMagnitude(target, 1f);
        }

        /// <summary>傾きをすぐに 0 にする（盤に置いたカードなど、静止させたいとき）。</summary>
        public void ResetTilt()
        {
            _tiltTarget = Vector2.zero;
            if (_tilt == Vector2.zero) return;
            _tilt = Vector2.zero;
            if (visual != null) visual.localRotation = Quaternion.identity;
            _dirty = true;
        }

        public Vector2 Tilt => _tilt;
        public Vector2 TiltTarget => _tiltTarget;

        /// <summary>傾きの見た目の回転（カーソル側が奥 = +z へ沈む向き）。</summary>
        public static Quaternion TiltRotation(Vector2 tilt) => Quaternion.Euler(tilt.y * MaxTiltDegrees, -tilt.x * MaxTiltDegrees, 0f);

        /// <summary>world の点に向けて傾ける（点がカードの縁にあるとき最大）。</summary>
        public void TiltToward(Vector3 world)
        {
            var c = transform.position;
            var half = Size * 0.5f * transform.lossyScale.x;
            SetTiltTarget(new Vector2((world.x - c.x) / Mathf.Max(0.01f, half.x), (world.y - c.y) / Mathf.Max(0.01f, half.y)));
        }

        void UpdateTilt()
        {
            if (_tilt == _tiltTarget) return;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.09f);
            _tilt = Vector2.Lerp(_tilt, _tiltTarget, k);
            if ((_tilt - _tiltTarget).sqrMagnitude < 1e-6f) _tilt = _tiltTarget;
            if (visual != null) visual.localRotation = TiltRotation(_tilt);
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
            if (traitText != null) traitText.alpha = _traitUsed ? 0.4f * alpha : alpha;
            if (countText != null) countText.alpha = alpha;
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
            if (traitText != null) traitText.sortingOrder = order * 10 + 3;
            if (countText != null) countText.sortingOrder = order * 10 + 3;
        }

        public int Order => _order;

        void RefreshTexts()
        {
            bool show = !FaceDown && !Unknown && Def != null;
            if (countText != null) countText.gameObject.SetActive(show && Count > 0);
            for (int i = 0; i < ranks.Length; i++) ranks[i].gameObject.SetActive(show);
            nameText.gameObject.SetActive(show);
            elementText.gameObject.SetActive(show && Def.Element.HasValue);
            tribeText.gameObject.SetActive(show && Def.Tribe.HasValue);
            if (traitText != null)
            {
                bool trait = show && _showTraits && Def.Trait.HasValue;
                traitText.gameObject.SetActive(trait);
                if (trait)
                {
                    var tc = Theme.TraitColor(Def.Trait.Value);
                    traitText.text = Theme.TraitGlyph(Def.Trait.Value);
                    traitText.color = Color.Lerp(tc, Color.white, 0.6f);
                    traitText.alpha = _traitUsed ? 0.4f * _alpha : _alpha;
                }
            }
            markText.gameObject.SetActive(!Unknown);
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

        void LateUpdate()
        {
            UpdateTilt();
            Apply();
        }

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
            _mpb.SetFloat(UnknownId, Unknown ? 1f : 0f);
            _mpb.SetFloat(LiftId, _lift);
            _mpb.SetFloat(FlashId, _flash);
            _mpb.SetVector(QuadSizeId, QuadSize);
            _mpb.SetFloat(ResonanceId, _resonance ? 1f : 0f);
            _mpb.SetFloat(ShieldId, _shield);
            _mpb.SetFloat(TraitUsedId, _traitUsed ? 1f : 0f);
            if (Def != null)
            {
                bool traitOn = _showTraits && Def.Trait.HasValue;
                _mpb.SetFloat(TraitOnId, traitOn ? 1f : 0f);
                _mpb.SetColor(TraitColorId, traitOn ? Theme.TraitColor(Def.Trait.Value) : Color.white);
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
                _mpb.SetFloat(LevelFxId, (float)CardLevelFx.TierOf(Def.Level));
                _mpb.SetVector(TiltId, new Vector4(_tilt.x, _tilt.y, 0f, 0f));
                _mpb.SetFloat(EmblemRotId, _emblemRot);
                _mpb.SetFloat(PivotId, Rotation);
                _mpb.SetVector(MarkDirId, new Vector4(_markDir.x, _markDir.y, 0f, 0f));
            }
            else
            {
                _mpb.SetFloat(TraitOnId, 0f);
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
