using System;
using Edgewise.Core.Campaign;

namespace Edgewise.Game.Campaign
{
    /// <summary>
    /// 星図巡りの進行の読み書き（DESIGN §12-8）。Unity に依存しない（置き場は ICampaignStore で差し替える）。
    /// - Load: 置き場から読む。空・壊れている・新しすぎる場合は新しい進行をメモリに作る（まだ保存しない）。
    ///   壊れている（Corrupt）ときは元の文字列を退避してから使う（次の保存で上書きする）。
    ///   新しすぎる（TooNew）ときは上書きしない: この起動中の進行は保存しない（ReadOnly）。
    /// - Save: 進行を置き場に書く（ReadOnly なら書かない）。
    /// - StartNew: はじめから（導入の後に呼ぶ）。原作モードの設定は引き継ぐ。
    /// - Erase: 置き場から消す（設定の「データを消す」）。
    /// </summary>
    public sealed class CampaignSession
    {
        readonly ICampaignStore _store;
        readonly Func<long> _newSeed;

        public CampaignSession(ICampaignStore store, Func<long> newSeed)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _newSeed = newSeed ?? (() => 1L);
            State = CampaignRules.NewGame(_newSeed());
        }

        public ICampaignStore Store => _store;

        /// <summary>今の進行（常に使える。保存前の新しい進行のこともある）。</summary>
        public CampaignState State { get; private set; }

        /// <summary>保存済みの進行があるか（タイトルの「続き」の表示・導入を出すかの判断）。</summary>
        public bool HasSave { get; private set; }

        /// <summary>新しすぎる形式のセーブがあったので、この起動中は保存しない。</summary>
        public bool ReadOnly { get; private set; }

        /// <summary>直近の読み込みの結果（Load を呼ぶまで null）。</summary>
        public SaveLoadResult LastLoad { get; private set; }

        /// <summary>保存した回数（テスト・ログ用）。</summary>
        public int SaveCount { get; private set; }

        public SaveLoadStatus LoadStatus => LastLoad?.Status ?? SaveLoadStatus.Empty;

        public SaveLoadResult Load()
        {
            string raw = _store.Read();
            var r = CampaignSave.Load(raw, _newSeed());
            LastLoad = r;
            State = r.State;
            ReadOnly = false;
            switch (r.Status)
            {
                case SaveLoadStatus.Empty:
                    HasSave = false;
                    break;
                case SaveLoadStatus.Corrupt:
                    // 上書きする前に元のデータを退避する
                    _store.WriteBackup(raw);
                    HasSave = false;
                    break;
                case SaveLoadStatus.TooNew:
                    // このゲームより新しい形式: 上書きしない
                    HasSave = false;
                    ReadOnly = true;
                    break;
                default:
                    HasSave = true;
                    break;
            }
            return r;
        }

        /// <summary>進行を差し替えて保存する（ReadOnly なら差し替えるだけ）。</summary>
        public void Save(CampaignState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            if (ReadOnly) return;
            _store.Write(CampaignSave.ToJson(state));
            HasSave = true;
            SaveCount++;
        }

        /// <summary>はじめから始めて保存する（原作モードの ON/OFF は今の設定を引き継ぐ）。</summary>
        public void StartNew()
        {
            Save(CampaignRules.NewGame(_newSeed(), State.ClassicMode));
        }

        /// <summary>原作モードを切り替える。保存済みの進行があれば保存する（まだ始めていなければ、始めるときに使う）。</summary>
        public void SetClassicMode(bool on)
        {
            if (State.ClassicMode == on) return;
            var next = State.WithClassicMode(on);
            if (HasSave) Save(next);
            else State = next;
        }

        /// <summary>星図巡りのデータを消して、まだ始めていない状態に戻す。</summary>
        public void Erase()
        {
            _store.Delete();
            State = CampaignRules.NewGame(_newSeed());
            HasSave = false;
            ReadOnly = false;
            LastLoad = null;
        }
    }
}
