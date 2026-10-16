using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Edgewise.EditorTools
{
    /// <summary>
    /// シーンファイル（YAML）の fileID を決定論的に付け直す。
    /// Unity は新しく作ったオブジェクトに乱数の fileID を付けるので、SceneBuilder でシーンを作り直すたびに中身が同じでも
    /// 全行が変わってしまう。ここで「階層のパス（親のパス/名前[兄弟の順番]）＋コンポーネントの型と順番」から ID を作り直し、
    /// ドキュメントを ID の順に並べ替える。中身が同じなら毎回同じバイト列になり、git の差分が出ない。
    /// 設定のドキュメント（OcclusionCullingSettings など、ID が小さい固定値）と SceneRoots（long.MaxValue）はそのまま。
    /// </summary>
    public static class SceneFileIds
    {
        static readonly Regex Header = new Regex(@"^--- !u!(\d+) &(-?\d+)(.*)$");
        static readonly Regex LocalRef = new Regex(@"\{fileID: (-?\d+)\}");
        static readonly Regex ScriptRef = new Regex(@"m_Script: \{fileID: -?\d+, guid: ([0-9a-f]+)");

        sealed class Doc
        {
            public int ClassId;
            public long Id;
            public string Suffix;
            public List<string> Lines = new List<string>();
            public string Name;
            public long GameObject;
            public long Father;
            public List<long> Children = new List<long>();
            public List<long> Components = new List<long>();
            public string Script = "";
            public long NewId;
        }

        /// <summary>path のシーンを正規化して書き直す。before（保存前の内容）と違えば true。</summary>
        public static bool Normalize(string path, string before)
        {
            string text = File.ReadAllText(path);
            string normalized = NormalizeText(text);
            if (before != null && before == normalized)
            {
                File.WriteAllText(path, before, new UTF8Encoding(false));
                return false;
            }
            File.WriteAllText(path, normalized, new UTF8Encoding(false));
            return true;
        }

        public static string NormalizeText(string text)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var head = new List<string>();
            var docs = new List<Doc>();
            Doc cur = null;
            foreach (var line in lines)
            {
                var m = Header.Match(line);
                if (m.Success)
                {
                    cur = new Doc { ClassId = int.Parse(m.Groups[1].Value), Id = long.Parse(m.Groups[2].Value), Suffix = m.Groups[3].Value };
                    docs.Add(cur);
                    continue;
                }
                if (cur == null) head.Add(line);
                else cur.Lines.Add(line);
            }
            // 末尾の空行（ファイル終端の改行）は最後のドキュメントから外す
            while (docs.Count > 0 && docs[docs.Count - 1].Lines.Count > 0 && docs[docs.Count - 1].Lines[docs[docs.Count - 1].Lines.Count - 1] == "")
                docs[docs.Count - 1].Lines.RemoveAt(docs[docs.Count - 1].Lines.Count - 1);

            var byId = new Dictionary<long, Doc>();
            var roots = new List<long>();
            foreach (var d in docs)
            {
                byId[d.Id] = d;
                string section = null;
                foreach (var l in d.Lines)
                {
                    if (l.StartsWith("  m_Name: ")) d.Name = l.Substring(10);
                    if (l.StartsWith("  m_GameObject: ")) d.GameObject = RefOf(l);
                    if (l.StartsWith("  m_Father: ")) d.Father = RefOf(l);
                    var sm = ScriptRef.Match(l);
                    if (sm.Success) d.Script = sm.Groups[1].Value;
                    if (l.StartsWith("  m_Children:")) section = "children";
                    else if (l.StartsWith("  m_Component:")) section = "components";
                    else if (l.StartsWith("  m_Roots:")) section = "roots";
                    else if (l.StartsWith("  - ") && section != null)
                    {
                        long r = RefOf(l);
                        if (section == "children") d.Children.Add(r);
                        else if (section == "components") d.Components.Add(r);
                        else roots.Add(r);
                    }
                    else if (!l.StartsWith("  - ") && !l.StartsWith("    ")) section = null;
                }
            }

            // GameObject → Transform（m_Father をたどるため）
            var transformOf = new Dictionary<long, long>();
            foreach (var d in docs)
            {
                if ((d.ClassId == 4 || d.ClassId == 224) && d.GameObject != 0) transformOf[d.GameObject] = d.Id;
            }

            var goKey = new Dictionary<long, string>();
            string KeyOfGo(long go)
            {
                if (goKey.TryGetValue(go, out var k)) return k;
                var g = byId[go];
                string parentKey = "";
                int sibling = 0;
                if (transformOf.TryGetValue(go, out long tr))
                {
                    var t = byId[tr];
                    if (t.Father != 0 && byId.TryGetValue(t.Father, out var father))
                    {
                        parentKey = KeyOfGo(father.GameObject);
                        sibling = father.Children.IndexOf(tr);
                    }
                    else sibling = roots.IndexOf(tr);
                }
                k = parentKey + "/" + g.Name + "[" + sibling + "]";
                goKey[go] = k;
                return k;
            }

            var keys = new Dictionary<long, string>();
            var unknownOrdinal = new Dictionary<string, int>();
            foreach (var d in docs)
            {
                if (d.Id == long.MaxValue || (d.Id > 0 && d.Id < 1000)) continue; // 固定 ID
                string key;
                if (d.ClassId == 1) key = "go:" + KeyOfGo(d.Id);
                else if (d.GameObject != 0 && byId.ContainsKey(d.GameObject))
                {
                    var g = byId[d.GameObject];
                    int ord = 0;
                    foreach (var c in g.Components)
                    {
                        if (c == d.Id) break;
                        if (byId.TryGetValue(c, out var other) && other.ClassId == d.ClassId && other.Script == d.Script) ord++;
                    }
                    key = "c:" + KeyOfGo(d.GameObject) + "|" + d.ClassId + "|" + d.Script + "#" + ord;
                }
                else
                {
                    // 持ち主の無いドキュメント: 型と（参照を伏せた）中身で区別する
                    string body = LocalRef.Replace(string.Join("\n", d.Lines), "{fileID: ?}");
                    string baseKey = "d:" + d.ClassId + "|" + Fnv64(body).ToString("x");
                    unknownOrdinal.TryGetValue(baseKey, out int n);
                    unknownOrdinal[baseKey] = n + 1;
                    key = baseKey + "#" + n;
                }
                keys[d.Id] = key;
            }

            var map = new Dictionary<long, long>();
            var used = new HashSet<long>(docs.Where(d => !keys.ContainsKey(d.Id)).Select(d => d.Id));
            foreach (var d in docs)
            {
                if (!keys.TryGetValue(d.Id, out var key))
                {
                    d.NewId = d.Id;
                    continue;
                }
                long id;
                string k = key;
                do
                {
                    id = (long)(Fnv64(k) & 0x3FFFFFFFFFFFFFFFUL) + 1000000L;
                    k += "'";
                } while (!used.Add(id));
                map[d.Id] = id;
                d.NewId = id;
            }

            var sb = new StringBuilder();
            foreach (var l in head) sb.Append(l).Append('\n');
            foreach (var d in docs.OrderBy(d => (ulong)d.NewId))
            {
                sb.Append("--- !u!").Append(d.ClassId).Append(" &").Append(d.NewId).Append(d.Suffix).Append('\n');
                foreach (var l in d.Lines)
                {
                    string line = l.Contains("fileID: ")
                        ? LocalRef.Replace(l, mm => map.TryGetValue(long.Parse(mm.Groups[1].Value), out long nid) ? "{fileID: " + nid + "}" : mm.Value)
                        : l;
                    sb.Append(line).Append('\n');
                }
            }
            return sb.ToString();
        }

        static long RefOf(string line)
        {
            var m = LocalRef.Match(line);
            return m.Success ? long.Parse(m.Groups[1].Value) : 0;
        }

        static ulong Fnv64(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s)
            {
                h ^= c;
                h *= 1099511628211UL;
            }
            return h;
        }
    }
}
