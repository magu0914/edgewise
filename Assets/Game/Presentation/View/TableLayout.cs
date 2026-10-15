using System;
using UnityEngine;

namespace Edgewise.Game.View
{
    /// <summary>
    /// 画面比率に合わせて盤・手札・カメラを配置する（DESIGN §8-1 レイアウト）。
    /// 横画面: 左にプレイヤーの手札、中央に盤、右に CPU の手札、上にスコア。縦画面: 上に CPU、中央に盤、下にプレイヤー。
    /// カメラは透視投影（カードの 3D 回転を立体的に見せるため）。必要な範囲が収まるようにカメラの距離を変える。
    /// </summary>
    public sealed class TableLayout : MonoBehaviour
    {
        public const float FieldOfView = 30f;

        /// <summary>縦画面に切り替える縦横比（幅 / 高さ）。</summary>
        public const float PortraitBelowAspect = 0.9f;

        [SerializeField] Camera cam;
        [SerializeField] BoardView board;
        [SerializeField] HandView hand0;
        [SerializeField] HandView hand1;
        [SerializeField] Transform starfield;

        int _w, _h;
        Vector3 _camBase;

        /// <summary>画面の揺れのずれ（JuiceFx が毎フレーム設定する。カメラの位置に足す）。</summary>
        public Vector2 ShakeOffset { get; set; }

        public bool Portrait { get; private set; }

        /// <summary>z = 0 の面で見えている高さ（ワールド単位）。</summary>
        public float VisibleHeight { get; private set; }

        public event Action Changed;

        public Camera Camera => cam;

        public void Wire(Camera camera, BoardView boardView, HandView h0, HandView h1, Transform stars)
        {
            cam = camera;
            board = boardView;
            hand0 = h0;
            hand1 = h1;
            starfield = stars;
        }

        public static bool IsPortrait(int width, int height) => height > 0 && width / (float)height < PortraitBelowAspect;

        void Start() => Apply();

        void LateUpdate()
        {
            if (Screen.width != _w || Screen.height != _h) Apply();
            cam.transform.position = _camBase + new Vector3(ShakeOffset.x, ShakeOffset.y, 0f);
        }

        public void Apply()
        {
            _w = Screen.width;
            _h = Screen.height;
            float aspect = _h > 0 ? _w / (float)_h : 16f / 9f;
            Portrait = aspect < PortraitBelowAspect;
            float H;
            if (!Portrait)
            {
                H = Mathf.Max(5.4f, 7.1f / aspect);
                float halfH = H * 0.5f;
                float halfW = halfH * aspect;
                float hud = H * (74f / 720f);
                float top = halfH - hud - 0.05f;
                float bottom = -halfH + 0.45f;
                float boardY = (top + bottom) * 0.5f;
                board.transform.position = new Vector3(0f, boardY, 0f);
                float handX = Mathf.Clamp(halfW - 0.62f, 2.38f, 3.3f);
                float avail = top - bottom;
                float step = Mathf.Min(0.74f, (avail - CardView.Size.y) / 4f);
                ConfigureHand(hand0, new Vector3(-handX, boardY, 0f), true, step, 1f, Vector3.right);
                ConfigureHand(hand1, new Vector3(handX, boardY, 0f), true, step, 1f, Vector3.left);
            }
            else
            {
                H = Mathf.Max(7.6f, 3.75f / aspect);
                float halfH = H * 0.5f;
                float halfW = halfH * aspect;
                float hud = H * (92f / 1280f);
                float top = halfH - hud - 0.05f;
                const float cpuScale = 0.6f;
                const float youScale = 0.88f; // 縦画面の手札は数字が読めるように大きめ（少し重なってよい）
                float content = CardView.Size.y * cpuScale + 0.08f + BoardView.BoardSize.y + 0.1f + CardView.Size.y * youScale + 0.7f;
                float avail = top + halfH;
                top -= Mathf.Max(0f, (avail - content) * 0.5f);
                float cpuY = top - CardView.Size.y * cpuScale * 0.5f;
                float boardTop = cpuY - CardView.Size.y * cpuScale * 0.5f - 0.08f;
                float boardY = boardTop - BoardView.BoardSize.y * 0.5f;
                float youY = boardY - BoardView.BoardSize.y * 0.5f - 0.1f - CardView.Size.y * youScale * 0.5f;
                board.transform.position = new Vector3(0f, boardY, 0f);
                float width = halfW * 2f - 0.12f;
                ConfigureHand(hand1, new Vector3(0f, cpuY, 0f), false, Mathf.Min(0.66f, (width - cpuScale) / 4f), cpuScale, Vector3.down);
                ConfigureHand(hand0, new Vector3(0f, youY, 0f), false, Mathf.Min(0.92f, (width - youScale) / 4f), youScale, Vector3.up);
            }

            VisibleHeight = H;
            float dist = H * 0.5f / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            cam.orthographic = false;
            cam.fieldOfView = FieldOfView;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = dist + 80f;
            _camBase = new Vector3(0f, 0f, -dist);
            cam.transform.position = _camBase;
            cam.transform.rotation = Quaternion.identity;

            if (starfield != null)
            {
                const float depth = 40f;
                float sh = 2f * (dist + depth) * Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;
                float sw = sh * aspect * 1.08f;
                starfield.position = new Vector3(0f, 0f, depth);
                starfield.localScale = new Vector3(sw, sh, 1f);
                var r = starfield.GetComponent<Renderer>();
                if (r != null)
                {
                    var mpb = new MaterialPropertyBlock();
                    r.GetPropertyBlock(mpb);
                    // 星の細かさを画面の高さ（ワールド単位）に合わせる
                    mpb.SetVector("_QuadSize", new Vector4(sw / sh * 12f, 12f, 0, 0));
                    r.SetPropertyBlock(mpb);
                }
            }

            // 盤上のカードを新しい位置へ
            for (int c = 0; c < board.Placed.Length; c++)
            {
                var card = board.Placed[c];
                if (card != null) card.transform.position = board.CellPosition(c);
            }
            Changed?.Invoke();
        }

        static void ConfigureHand(HandView hand, Vector3 pos, bool vertical, float step, float scale, Vector3 hoverDir)
        {
            hand.transform.position = pos;
            hand.Vertical = vertical;
            hand.Step = step;
            hand.CardScale = scale;
            hand.HoverDirection = hoverDir;
            hand.Layout(false);
        }

        /// <summary>画面上の点（ピクセル、左下原点）から z = 0 の面の点を求める。</summary>
        public Vector3 ScreenToTable(Vector2 screen, float z = 0f)
        {
            var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            if (Mathf.Abs(ray.direction.z) < 1e-6f) return ray.origin;
            float t = (z - ray.origin.z) / ray.direction.z;
            return ray.origin + ray.direction * t;
        }

        public Vector2 TableToScreen(Vector3 world)
        {
            var s = cam.WorldToScreenPoint(world);
            return new Vector2(s.x, s.y);
        }
    }
}
