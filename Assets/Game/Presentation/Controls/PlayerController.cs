using System.Collections.Generic;
using System.Threading;
using Edgewise.Core;
using Edgewise.Game.App;
using Edgewise.Game.Juice;
using Edgewise.Game.Match;
using Edgewise.Game.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Edgewise.Game.Controls
{
    /// <summary>
    /// プレイヤーの手番の操作（DESIGN §8-1 操作）。マウス／タッチ／キーボードの入力を「持つ・重ねる・置く・回す・戻す」の
    /// 命令に変換し、1 つの Move を作る。命令は public メソッドなので、テストからコードで注入できる。
    /// - マウス／タッチ: ドラッグ＆ドロップ、または「カードをタップ → マスをタップ」（タッチは 1 回目のタップで予告、2 回目で置く）。
    /// - キーボード: ←→（↑↓）で手札を選ぶ → Enter で持つ → 矢印でマス → Enter で置く。Esc で戻る。
    /// - Pivot: 持っている間に Q / E、右クリック、画面の回転ボタンで 90° ずつ回す。トークンは置いたときだけ消費（エンジンが処理）。
    /// 予告は MovePreview（エンジンの ApplyMove）で求める。
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        enum Mode
        {
            Idle,
            Dragging,
            Holding,
        }

        const float TapPixels = 14f;

        BoardView _board;
        HandView _hand;
        TableLayout _layout;
        JuiceSettings _juice;
        IMatchFx _fx = NullMatchFx.Instance;

        GameState _state;
        Player _seat;
        bool _awaiting;
        bool _resigned;
        Move? _result;

        Mode _mode;
        int _heldIndex = -1;
        CardView _held;
        int _rotation;
        int _hoverCell = -1;
        int _keyCell = -1;
        int _keyHand = -1;
        bool _keyboardMode;
        bool _touchInput;
        Vector2 _pressPos;
        Vector2 _lastPointer;
        Vector3 _pointerWorld;
        float _spinZ;
        MovePreview _preview;
        readonly List<int> _glowing = new List<int>();

        /// <summary>メニューが開いているなど、入力を受け付けない状態。</summary>
        public bool Blocked { get; set; }

        public bool IsAwaitingMove => _awaiting && _result == null && !_resigned;
        public bool IsHolding => _held != null && _mode != Mode.Idle;
        public int HeldIndex => _heldIndex;
        public int HoverCell => _hoverCell;
        public int Rotation => _rotation;
        public MovePreview Preview => _preview;
        public GameState State => _state;

        /// <summary>Esc（何も持っていないとき）でメニューを開く。</summary>
        public event System.Action MenuRequested;

        /// <summary>持ち始め・置く・戻すのたびに呼ぶ（回転ボタンの表示切り替え）。</summary>
        public event System.Action<bool> HoldingChanged;

        public void Configure(BoardView board, HandView hand, TableLayout layout, JuiceSettings juice, IMatchFx fx)
        {
            _board = board;
            _hand = hand;
            _layout = layout;
            _juice = juice;
            _fx = fx ?? NullMatchFx.Instance;
        }

        public bool CanRotate => _state != null && _state.Rules.Pivot && _state.GetPivotTokens(_seat) > 0;

        /// <summary>プレイヤーの手を待つ。降参したら null。</summary>
        public async Awaitable<Move?> WaitForMoveAsync(GameState state, Player seat, CancellationToken ct)
        {
            _state = state;
            _seat = seat;
            _result = null;
            _resigned = false;
            _mode = Mode.Idle;
            _held = null;
            _heldIndex = -1;
            _rotation = 0;
            _hoverCell = -1;
            _keyCell = -1;
            if (_keyHand >= _hand.Cards.Count) _keyHand = _hand.Cards.Count - 1;
            if (_keyboardMode && _keyHand >= 0) _hand.SetKeySelect(_keyHand);
            _awaiting = true;
            if (AutomationLog.Enabled) AutomationLog.AwaitingMove(this, _hand, _board, _layout);
            try
            {
                while (_result == null && !_resigned) await Awaitable.NextFrameAsync(ct);
            }
            finally
            {
                _awaiting = false;
                ClearPreview();
                _hand.ClearHighlights();
                HoldingChanged?.Invoke(false);
            }
            return _resigned ? (Move?)null : _result;
        }

        // ------------------------------------------------------------------
        // 命令（デバイス入力からもテストからも呼ぶ）
        // ------------------------------------------------------------------

        /// <summary>手札 handIndex を持つ。drag = true ならポインタに追従、false なら手札の上に浮かせて待つ。</summary>
        public bool Pick(int handIndex, bool drag = false)
        {
            if (!IsAwaitingMove || handIndex < 0 || handIndex >= _hand.Cards.Count) return false;
            if (_held != null) Cancel();
            _heldIndex = handIndex;
            _held = _hand.Cards[handIndex];
            _hand.Held = _held;
            _rotation = 0;
            _held.SetRotation(0);
            _held.SetOrder(100);
            _held.SetSelect(0f);
            _mode = drag ? Mode.Dragging : Mode.Holding;
            _hand.SetHover(-1);
            _fx.OnPick();
            HoldingChanged?.Invoke(true);
            if (AutomationLog.Enabled) AutomationLog.Write("pick " + handIndex + (drag ? " drag" : ""));
            return true;
        }

        /// <summary>持っているカードをマスに重ねる（予告を出す）。-1 で外す。</summary>
        public void Hover(int cell)
        {
            if (_held == null) return;
            if (cell == _hoverCell) return;
            _hoverCell = cell;
            _fx.OnCursorMove();
            UpdatePreview();
        }

        /// <summary>持っているカードを cell に置く。合法なら Move が確定する。</summary>
        public bool Place(int cell)
        {
            if (_held == null || !IsAwaitingMove) return false;
            var move = new Move(_heldIndex, cell, _rotation);
            if (!GameEngine.IsLegalMove(_state, move)) return false;
            _result = move;
            _mode = Mode.Idle;
            ClearPreview();
            _held.SetAlpha(1f);
            if (AutomationLog.Enabled) AutomationLog.Write("place " + move);
            return true;
        }

        /// <summary>持っているカードを 90° 回す（dir = +1 時計回り、-1 反時計回り）。Pivot が使えないときは何もしない。</summary>
        public bool Rotate(int dir)
        {
            if (_held == null || !CanRotate) return false;
            _rotation = (_rotation + (dir >= 0 ? 1 : 3)) & 3;
            _held.SetRotation(_rotation);
            // 数字は新しい値にしてから、見た目だけ逆向きに 90° ずらした所から回して戻す
            _spinZ += dir >= 0 ? 90f : -90f;
            UpdatePreview();
            if (AutomationLog.Enabled) AutomationLog.Write("rotate " + _rotation);
            return true;
        }

        /// <summary>持っているカードを手札に戻す。</summary>
        public void Cancel()
        {
            if (_held == null) return;
            var card = _held;
            _held = null;
            _heldIndex = -1;
            _mode = Mode.Idle;
            _rotation = 0;
            _spinZ = 0f;
            card.SetRotation(0);
            card.SetAlpha(1f);
            card.SetLift(0f);
            _hand.Held = null;
            ClearPreview();
            _hand.Layout(true);
            HoldingChanged?.Invoke(false);
        }

        public void Resign()
        {
            if (!_awaiting) return;
            Cancel();
            _resigned = true;
        }

        // ------------------------------------------------------------------
        // 予告
        // ------------------------------------------------------------------
        void UpdatePreview()
        {
            ClearGlows();
            _preview = null;
            if (_held == null || _hoverCell < 0)
            {
                _board.ShowCursor(-1, Color.white, false);
                _board.HideBadges();
                return;
            }
            bool empty = !_state.Board[_hoverCell].HasValue;
            var color = Theme.OwnerColor(_seat);
            _board.ShowCursor(_hoverCell, color, empty);
            if (!empty)
            {
                _board.HideBadges();
                return;
            }
            _preview = MovePreview.Compute(_state, new Move(_heldIndex, _hoverCell, _rotation));
            if (_preview == null)
            {
                _board.HideBadges();
                return;
            }
            // 取れる予定のカード: 所有者の色は変えず、脈打つ光の縁取りと「取れる」矢印の印だけを出す
            foreach (int c in _preview.FlipCells)
            {
                var card = _board.Placed[c];
                if (card == null) continue;
                card.SetGlow(_juice.previewGlow, Theme.CaptureGlow);
                int from = _preview.FromOf(c);
                var d = _board.CellPosition(c) - _board.CellPosition(from >= 0 ? from : _hoverCell);
                card.SetCaptureMark(d.sqrMagnitude > 1e-6f ? new Vector2(d.x, d.y).normalized : Vector2.up);
                _glowing.Add(c);
            }
            string first = _preview.Same || _preview.SameWall ? Edgewise.Game.UI.UiText.BadgeSame : _preview.Plus ? Edgewise.Game.UI.UiText.BadgePlus : null;
            string second = (_preview.Same || _preview.SameWall) && _preview.Plus ? Edgewise.Game.UI.UiText.BadgePlus : null;
            _board.ShowBadges(_hoverCell, first, second);
            if (AutomationLog.Enabled)
                AutomationLog.Write("preview cell=" + _hoverCell + " rot=" + _rotation + " flips=" + string.Join(",", _preview.FlipCells)
                    + (_preview.Same ? " same" : "") + (_preview.SameWall ? " sameWall" : "") + (_preview.Plus ? " plus" : ""));
        }

        void ClearGlows()
        {
            foreach (int c in _glowing)
            {
                var card = _board.Placed[c];
                if (card != null)
                {
                    card.SetGlow(0f, Color.white);
                    card.SetCaptureMark(Vector2.zero);
                }
            }
            _glowing.Clear();
        }

        void ClearPreview()
        {
            ClearGlows();
            _preview = null;
            _hoverCell = -1;
            if (_board != null)
            {
                _board.ShowCursor(-1, Color.white, false);
                _board.HideBadges();
            }
        }

        // ------------------------------------------------------------------
        // デバイス入力
        // ------------------------------------------------------------------
        void Update()
        {
            if (_board == null) return;
            if (_awaiting && !Blocked && _result == null)
            {
                // 対戦中は UI のボタン（ヒント・回転など）にキーボードの選択を残さない。
                // 残っていると Enter・矢印がボタンに取られて、キーボードで手札を操作できなくなる
                var es = EventSystem.current;
                if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);
                HandlePointer();
                HandleKeyboard();
            }
            AnimateHeld();
        }

        bool PointerOverUi(Vector2 pos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var data = new PointerEventData(es) { position = pos };
            var hits = new List<RaycastResult>();
            es.RaycastAll(data, hits);
            return hits.Count > 0;
        }

        void HandlePointer()
        {
            var pointer = Pointer.current;
            if (pointer == null) return;
            var pos = pointer.position.ReadValue();
            bool pressed = pointer.press.wasPressedThisFrame;
            bool released = pointer.press.wasReleasedThisFrame;
            bool moved = (pos - _lastPointer).sqrMagnitude > 1f;
            _lastPointer = pos;
            if (pressed) _touchInput = pointer is Touchscreen;
            if (moved || pressed)
            {
                if (_keyboardMode)
                {
                    _keyboardMode = false;
                    _hand.SetKeySelect(-1);
                }
            }
            _pointerWorld = _layout.ScreenToTable(pos);
            var world = _pointerWorld;
            bool overUi = (pressed || released) && PointerOverUi(pos);

            var mouse = Mouse.current;
            if (_held != null && mouse != null && mouse.rightButton.wasPressedThisFrame) Rotate(+1);

            switch (_mode)
            {
                case Mode.Idle:
                {
                    int idx = _keyboardMode ? _hand.KeySelectIndex : _hand.HitTest(world);
                    if (!_keyboardMode && !(pointer is Touchscreen))
                    {
                        if (idx >= 0 && idx != _hand.HoverIndex) _fx.OnCursorMove();
                        _hand.SetHover(idx);
                    }
                    if (pressed && !overUi && idx >= 0)
                    {
                        _pressPos = pos;
                        Pick(idx, true);
                    }
                    break;
                }
                case Mode.Dragging:
                {
                    int cell = _board.CellAt(world);
                    Hover(cell);
                    if (released)
                    {
                        if (cell >= 0 && Place(cell)) break;
                        if ((pos - _pressPos).magnitude < TapPixels)
                        {
                            _mode = Mode.Holding; // タップで持った: 次のタップでマスを選ぶ
                            Hover(-1);
                        }
                        else Cancel();
                    }
                    break;
                }
                case Mode.Holding:
                {
                    if (!_keyboardMode && moved && !_touchInput) Hover(_board.CellAt(world));
                    if (pressed && !overUi)
                    {
                        int cell = _board.CellAt(world);
                        int idx = _hand.HitTest(world);
                        if (cell >= 0)
                        {
                            if (_touchInput && _hoverCell != cell) Hover(cell);
                            else if (!Place(cell)) Hover(cell);
                        }
                        else if (idx >= 0 && idx != _heldIndex)
                        {
                            Cancel();
                            _pressPos = pos;
                            Pick(idx, true);
                        }
                        else Cancel();
                    }
                    break;
                }
            }
        }

        void HandleKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool left = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame;
            bool right = kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            bool up = kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame;
            bool down = kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame;
            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            bool esc = kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;

            if (_held != null)
            {
                if (kb.qKey.wasPressedThisFrame) Rotate(-1);
                if (kb.eKey.wasPressedThisFrame) Rotate(+1);
            }

            if (_mode == Mode.Idle)
            {
                int n = _hand.Cards.Count;
                if (n == 0) return;
                if (left || right || up || down)
                {
                    if (!_keyboardMode)
                    {
                        _keyboardMode = true;
                        _hand.SetHover(-1);
                        if (_keyHand < 0 || _keyHand >= n) _keyHand = 0;
                    }
                    else
                    {
                        int d = left || up ? -1 : 1;
                        _keyHand = (_keyHand + d + n) % n;
                    }
                    _fx.OnCursorMove();
                    _hand.SetKeySelect(_keyHand);
                }
                else if (enter && _keyboardMode && _keyHand >= 0 && _keyHand < n)
                {
                    if (Pick(_keyHand, false))
                    {
                        _keyCell = FirstEmptyCell();
                        Hover(_keyCell);
                    }
                }
                else if (esc) MenuRequested?.Invoke();
                return;
            }

            if (left || right || up || down)
            {
                _keyboardMode = true;
                if (_keyCell < 0) _keyCell = _hoverCell >= 0 ? _hoverCell : FirstEmptyCell();
                else
                {
                    int col = _keyCell % 3;
                    int row = _keyCell / 3;
                    if (left) col = (col + 2) % 3;
                    if (right) col = (col + 1) % 3;
                    if (up) row = (row + 2) % 3;
                    if (down) row = (row + 1) % 3;
                    _keyCell = row * 3 + col;
                }
                Hover(_keyCell);
            }
            else if (enter && _hoverCell >= 0)
            {
                Place(_hoverCell);
            }
            else if (esc)
            {
                _keyboardMode = true;
                int k = _heldIndex;
                Cancel();
                _keyHand = k;
                _hand.SetKeySelect(_keyHand);
            }
        }

        int FirstEmptyCell()
        {
            if (!_state.Board[4].HasValue) return 4;
            for (int c = 0; c < 9; c++)
            {
                if (!_state.Board[c].HasValue) return c;
            }
            return 4;
        }

        /// <summary>持っているカードを動かす（ポインタに追従・マスに吸い付く・傾いて揺れる・回転のアニメーション）。</summary>
        void AnimateHeld()
        {
            if (_held == null || _mode == Mode.Idle) return;
            float dt = Time.unscaledDeltaTime;
            float lift = _juice.pickLift;
            Vector3 target;
            float scale;
            float alpha = 1f;
            bool snapped = _hoverCell >= 0 && _state != null && !_state.Board[_hoverCell].HasValue;
            if (snapped)
            {
                // マスに吸い付く。半透明にして、裏返る予定のカードと盤が見えるようにする
                target = _board.CellPosition(_hoverCell) + new Vector3(0f, 0f, -0.12f);
                scale = 1f;
                alpha = Mathf.Clamp01(_juice.ghostAlpha + 0.3f);
            }
            else if (_mode == Mode.Dragging || (_mode == Mode.Holding && !_keyboardMode && !_touchInput))
            {
                var screen = Pointer.current != null ? Pointer.current.position.ReadValue() : _lastPointer;
                target = _layout.ScreenToTable(screen, -lift);
                if (_touchInput) target += new Vector3(0f, 0.55f, 0f); // 指で隠れないように少し上
                scale = _juice.pickScale;
            }
            else
            {
                target = _hand.SlotPosition(_heldIndex) + _hand.HoverDirection * 0.5f + new Vector3(0f, 0f, -lift);
                scale = _juice.pickScale;
            }

            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, _juice.pickTime * 0.5f));
            var t = _held.transform;
            t.position = Vector3.Lerp(t.position, target, k);
            t.localScale = Vector3.Lerp(t.localScale, Vector3.one * scale, k);
            _held.SetAlpha(Mathf.Lerp(_held.Alpha, alpha, k));
            _held.SetLift(snapped ? 0.15f : 1f);

            // 回転（Pivot）のアニメーション: _spinZ を 0 へ戻す
            float spinK = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, _juice.rotateTime * 0.35f));
            _spinZ = Mathf.Lerp(_spinZ, 0f, spinK);
            if (Mathf.Abs(_spinZ) < 0.05f) _spinZ = 0f;
            float time = Time.unscaledTime * Mathf.PI * 2f * _juice.holdWobbleHz;
            float tilt = snapped ? 0f : _juice.holdTiltDegrees;
            t.rotation = Quaternion.Euler(Mathf.Sin(time * 0.8f) * tilt * 0.6f, Mathf.Sin(time) * tilt, _spinZ + Mathf.Sin(time * 0.7f) * tilt * 0.3f);
        }
    }
}
