using Edgewise.Game.View;
using UnityEngine;
using UnityEngine.UI;

namespace Edgewise.Game.UI
{
    /// <summary>
    /// 画面の向きに合わせて uGUI の基準解像度を切り替え（横 1280×720 / 縦 720×1280）、子の IResponsiveUi に配置をやり直させる。
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class ResponsiveCanvas : MonoBehaviour
    {
        int _w, _h;
        bool _portrait;

        public bool Portrait => _portrait;

        void Start() => Apply(true);

        void LateUpdate()
        {
            if (Screen.width != _w || Screen.height != _h) Apply(false);
        }

        public void Apply(bool force)
        {
            _w = Screen.width;
            _h = Screen.height;
            bool portrait = TableLayout.IsPortrait(_w, _h);
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = portrait ? new Vector2(720, 1280) : new Vector2(1280, 720);
            scaler.matchWidthOrHeight = portrait ? 0f : 1f;
            // 横長すぎる・縦長すぎる画面では短い辺に合わせる
            float aspect = _h > 0 ? _w / (float)_h : 1.7f;
            if (!portrait && aspect < 16f / 9f) scaler.matchWidthOrHeight = 0f;
            if (portrait && aspect > 9f / 16f) scaler.matchWidthOrHeight = 1f;
            if (portrait == _portrait && !force) return;
            _portrait = portrait;
            foreach (var r in GetComponentsInChildren<IResponsiveUi>(true)) r.ApplyLayout(portrait);
            if (!force) StartCoroutine(LogAfterLayout());
        }

        System.Collections.IEnumerator LogAfterLayout()
        {
            yield return null;
            Edgewise.Game.App.AutomationLog.Buttons(_portrait ? "Portrait" : "Landscape", transform);
        }
    }
}
