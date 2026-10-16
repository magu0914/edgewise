using System.Collections.Generic;
using UnityEngine;

namespace Edgewise.Game.Campaign
{
    /// <summary>
    /// 星図巡りのセーブの置き場（1 スロットの JSON 文字列）。中身の形式は Core の CampaignSave が決め、ここは出し入れだけを担当する。
    /// 実際の置き場は PlayerPrefs（WebGL では IndexedDB）。テストではメモリ上の置き場に差し替える。
    /// </summary>
    public interface ICampaignStore
    {
        /// <summary>保存されている文字列（なければ null）。</summary>
        string Read();

        void Write(string json);

        /// <summary>壊れていて読めなかった元の文字列を、上書きする前に別の場所へ退避する。</summary>
        void WriteBackup(string raw);

        void Delete();
    }

    /// <summary>PlayerPrefs に保存する置き場。</summary>
    public sealed class PlayerPrefsCampaignStore : ICampaignStore
    {
        public const string Key = "edgewise.campaign.save";
        public const string BackupKey = "edgewise.campaign.backup";

        public string Read() => PlayerPrefs.HasKey(Key) ? PlayerPrefs.GetString(Key) : null;

        public void Write(string json)
        {
            PlayerPrefs.SetString(Key, json);
            PlayerPrefs.Save();
        }

        public void WriteBackup(string raw)
        {
            PlayerPrefs.SetString(BackupKey, raw ?? "");
            PlayerPrefs.Save();
        }

        public void Delete()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }

    /// <summary>メモリ上の置き場（テスト用）。呼ばれた順に Calls へ "read" / "write" / "backup" / "delete" を記録する。</summary>
    public sealed class MemoryCampaignStore : ICampaignStore
    {
        public string Json;
        public string Backup;
        public readonly List<string> Calls = new List<string>();
        public int Writes { get; private set; }

        public MemoryCampaignStore(string json = null) => Json = json;

        public string Read()
        {
            Calls.Add("read");
            return Json;
        }

        public void Write(string json)
        {
            Calls.Add("write");
            Writes++;
            Json = json;
        }

        public void WriteBackup(string raw)
        {
            Calls.Add("backup");
            Backup = raw;
        }

        public void Delete()
        {
            Calls.Add("delete");
            Json = null;
        }
    }

    /// <summary>実行時に使う置き場を決める（テストは Override を設定してからシーンを読み込む）。</summary>
    public static class CampaignStores
    {
        public static ICampaignStore Override;

        public static ICampaignStore Create() => Override ?? new PlayerPrefsCampaignStore();
    }
}
