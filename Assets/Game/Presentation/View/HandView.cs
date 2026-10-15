using System.Collections.Generic;
using Edgewise.Core;
using Edgewise.Game.Tweening;
using UnityEngine;

namespace Edgewise.Game.View
{
    /// <summary>
    /// 片方のプレイヤーの手札の並び（表示だけ）。横画面では縦の列（重ねて並べる）、縦画面では横の列。
    /// Cards の並びはエンジンの手札の並び（handIndex）と常に一致させる。
    /// </summary>
    public sealed class HandView : MonoBehaviour
    {
        public Player owner;

        public readonly List<CardView> Cards = new List<CardView>();

        /// <summary>true = 縦の列（上から index 0）、false = 横の列（左から index 0）。</summary>
        public bool Vertical = true;

        /// <summary>隣のカードとの間隔（ワールド単位）。</summary>
        public float Step = 0.72f;

        /// <summary>手札のカードの大きさ。</summary>
        public float CardScale = 1f;

        /// <summary>カーソルを乗せたときに浮かせる向き（盤の方向）。</summary>
        public Vector3 HoverDirection = Vector3.right;

        public float HoverRaise = 0.16f;
        public float MoveTime = 0.15f;

        /// <summary>持ち上げられている（PlayerController が動かしている）カード。並べ直しの対象外。</summary>
        public CardView Held { get; set; }

        int _hover = -1;
        int _keySelect = -1;

        public int HoverIndex => _hover;
        public int KeySelectIndex => _keySelect;

        public Vector3 SlotPosition(int index) => SlotPosition(index, Cards.Count);

        public Vector3 SlotPosition(int index, int count)
        {
            float offset = (index - (count - 1) * 0.5f) * Step;
            var local = Vertical ? new Vector3(0f, -offset, 0f) : new Vector3(offset, 0f, 0f);
            return transform.position + local;
        }

        int BaseOrder(int index) => 20 + index;

        /// <summary>手札を並べ直す。animate = false なら即座に。</summary>
        public void Layout(bool animate, float timeFactor = 1f)
        {
            for (int i = 0; i < Cards.Count; i++)
            {
                var card = Cards[i];
                if (card == null || card == Held) continue;
                bool raised = i == _hover || i == _keySelect;
                var target = SlotPosition(i) + (raised ? HoverDirection * HoverRaise + new Vector3(0, 0, -0.08f) : Vector3.zero);
                card.BaseScale = CardScale;
                card.SetOrder(raised ? 45 : BaseOrder(i));
                card.SetSelect(i == _keySelect ? 1f : 0f);
                card.SetLift(raised ? 0.25f : 0f);
                MoveCard(card, target, Quaternion.identity, CardScale, animate ? MoveTime * timeFactor : 0f);
            }
        }

        public static void MoveCard(CardView card, Vector3 target, Quaternion rotation, float scale, float duration, Ease ease = Ease.OutCubic)
        {
            var from = card.transform.position;
            var fromRot = card.transform.rotation;
            var fromScale = card.transform.localScale;
            var toScale = Vector3.one * scale;
            Tweens.Play(duration, t =>
            {
                card.transform.position = Vector3.LerpUnclamped(from, target, t);
                card.transform.rotation = Quaternion.SlerpUnclamped(fromRot, rotation, Mathf.Clamp01(t));
                card.transform.localScale = Vector3.LerpUnclamped(fromScale, toScale, t);
            }, ease, 0f, card, "move");
        }

        /// <summary>world の点の上にある手札の index（重なりは手前のカードを優先）。なければ -1。</summary>
        public int HitTest(Vector3 world)
        {
            var size = CardView.Size * CardScale;
            int best = -1;
            int bestOrder = int.MinValue;
            for (int i = 0; i < Cards.Count; i++)
            {
                if (Cards[i] == null || Cards[i] == Held) continue;
                bool raised = i == _hover || i == _keySelect;
                var c = SlotPosition(i) + (raised ? HoverDirection * HoverRaise : Vector3.zero);
                if (Mathf.Abs(world.x - c.x) > size.x * 0.5f || Mathf.Abs(world.y - c.y) > size.y * 0.5f) continue;
                int order = raised ? 45 : BaseOrder(i);
                if (order > bestOrder)
                {
                    bestOrder = order;
                    best = i;
                }
            }
            return best;
        }

        public void SetHover(int index)
        {
            if (index == _hover) return;
            _hover = index;
            Layout(true);
        }

        public void SetKeySelect(int index)
        {
            if (index == _keySelect) return;
            _keySelect = index;
            Layout(true);
        }

        public void ClearHighlights()
        {
            _hover = -1;
            _keySelect = -1;
            Layout(true);
        }

        /// <summary>CPU が考えている間の演出: index のカードを盤の方向へ amount だけ浮かせる（毎フレーム呼ぶ）。</summary>
        public void Bob(int index, float amount)
        {
            if (index < 0 || index >= Cards.Count || Cards[index] == null) return;
            var card = Cards[index];
            card.transform.position = SlotPosition(index) + HoverDirection * amount + new Vector3(0f, 0f, -amount);
            card.SetLift(Mathf.Clamp01(amount * 3f));
            card.SetOrder(amount > 0.01f ? 45 : BaseOrder(index));
        }

        /// <summary>handIndex のカードを手札から外して返す（並べ直しはしない）。</summary>
        public CardView TakeAt(int index)
        {
            var card = Cards[index];
            Cards.RemoveAt(index);
            if (card == Held) Held = null;
            _hover = -1;
            _keySelect = -1;
            return card;
        }
    }
}
