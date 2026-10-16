using System.Collections.Generic;

namespace Edgewise.Game.Campaign
{
    /// <summary>報酬で 1 枚めくった結果。</summary>
    public readonly struct RewardPick
    {
        public readonly int Index;
        public readonly string CardId;

        /// <summary>初めて手に入れたカード（「NEW!」）。</summary>
        public readonly bool IsNew;

        /// <summary>受け取った後の枚数（2 以上なら「×2」）。</summary>
        public readonly int CopiesAfter;

        public RewardPick(int index, string cardId, bool isNew, int copiesAfter)
        {
            Index = index;
            CardId = cardId;
            IsNew = isNew;
            CopiesAfter = copiesAfter;
        }
    }

    /// <summary>
    /// 報酬の選択（DESIGN §12-3 / §12-11）: 相手が使った 5 枚が裏向きで並び、Count 枚（勝ち 1、完勝 2）を選ぶ。
    /// 表示に依存しない。手持ちへの反映は CampaignFlow が Core の ApplyMatch で行う。
    /// </summary>
    public sealed class RewardModel
    {
        readonly List<string> _cards;
        readonly IReadOnlyDictionary<string, int> _before;
        readonly List<string> _picks = new List<string>();
        readonly List<int> _pickedIndices = new List<int>();

        public RewardModel(IReadOnlyList<string> opponentDeck, int count, IReadOnlyDictionary<string, int> collectionBefore)
        {
            _cards = new List<string>(opponentDeck ?? new string[0]);
            Count = count < 0 ? 0 : count > _cards.Count ? _cards.Count : count;
            _before = collectionBefore ?? new Dictionary<string, int>();
        }

        public IReadOnlyList<string> Cards => _cards;

        /// <summary>選べる枚数。</summary>
        public int Count { get; }

        public IReadOnlyList<string> Picks => _picks;
        public IReadOnlyList<int> PickedIndices => _pickedIndices;

        public int Remaining => Count - _picks.Count;
        public bool IsDone => _picks.Count >= Count;

        public bool IsPicked(int index) => _pickedIndices.Contains(index);

        /// <summary>index のカードを選ぶ。選べなければ null（済み・範囲外・もう選び終えた・同じカード）。</summary>
        public RewardPick? Pick(int index)
        {
            if (IsDone || index < 0 || index >= _cards.Count || _pickedIndices.Contains(index)) return null;
            var id = _cards[index];
            if (_picks.Contains(id)) return null;
            int had = _before.TryGetValue(id, out int n) ? n : 0;
            _picks.Add(id);
            _pickedIndices.Add(index);
            return new RewardPick(index, id, had == 0, had + 1);
        }
    }
}
