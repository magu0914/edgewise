using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using static Edgewise.Core.Tests.H;

namespace Edgewise.Core.Tests.Engine
{
    // 移植元: tripletriad-ts-legacy/tests/engine/purity.test.ts
    // TS 版は Math.random / Date などの使用を静的検査＋実行時のモックで確かめていた。
    // C# 版は「Core のソースに System.Random / DateTime / Stopwatch / UnityEngine / float / double などが出てこない」静的検査に置き換え、
    // 実行時の検査は「隠れた共有状態（可変な static フィールド）がない・対局を交互に進めても結果が変わらない」に置き換える。

    public static class CoreSources
    {
        /// <summary>リポジトリのルート（Assets/Game/Core があるディレクトリ）を、テストの実行ディレクトリから上にたどって探す。</summary>
        public static string RepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Game", "Core"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Assets/Game/Core not found above " + TestContext.CurrentContext.TestDirectory);
        }

        public static string CoreDir() => Path.Combine(RepoRoot(), "Assets", "Game", "Core");

        public static List<(string File, string Code)> Load() =>
            Directory.GetFiles(CoreDir(), "*.cs", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => (Path.GetRelativePath(CoreDir(), f).Replace('\\', '/'), StripCommentsAndLiterals(File.ReadAllText(f))))
                .ToList();

        /// <summary>コメント・文字列リテラル・文字リテラルを空白に置き換える（説明文やメッセージ中の単語に引っかからないように）。</summary>
        public static string StripCommentsAndLiterals(string code)
        {
            var sb = new StringBuilder(code.Length);
            int i = 0;
            while (i < code.Length)
            {
                char c = code[i];
                char next = i + 1 < code.Length ? code[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < code.Length && code[i] != '\n') i++;
                }
                else if (c == '/' && next == '*')
                {
                    i += 2;
                    while (i + 1 < code.Length && !(code[i] == '*' && code[i + 1] == '/')) i++;
                    i += 2;
                    sb.Append(' ');
                }
                else if (c == '@' && next == '"')
                {
                    i += 2;
                    while (i < code.Length)
                    {
                        if (code[i] == '"' && i + 1 < code.Length && code[i + 1] == '"') i += 2;
                        else if (code[i] == '"') break;
                        else i++;
                    }
                    i++;
                    sb.Append("\"\"");
                }
                else if (c == '"' || c == '\'')
                {
                    char quote = c;
                    i++;
                    while (i < code.Length && code[i] != quote)
                    {
                        if (code[i] == '\\') i++;
                        i++;
                    }
                    i++;
                    sb.Append(quote).Append(quote);
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }
    }

    [TestFixture]
    public class CorePurityStaticTests
    {
        [Test]
        public void FindsTheCoreSources()
        {
            var files = CoreSources.Load().Select(s => s.File).ToList();
            Assert.That(files, Does.Contain("Engine/GameEngine.Apply.cs"));
            Assert.That(files, Does.Contain("Data/CardCatalog.cs"));
            Assert.That(files, Does.Contain("AI/SearchSession.cs"));
            Assert.That(files.Count, Is.GreaterThanOrEqualTo(10));
        }

        static IEnumerable<TestCaseData> Forbidden()
        {
            TestCaseData C(string name, string pattern) => new TestCaseData(pattern).SetName("does not use " + name);
            yield return C("System.Random", @"\bSystem\s*\.\s*Random\b|\bnew\s+Random\s*\(|\bRandom\s*\.\s*Shared\b");
            yield return C("DateTime", @"\bDateTime(Offset)?\b|\bTimeSpan\b");
            yield return C("Stopwatch", @"\bStopwatch\b");
            yield return C("UnityEngine", @"\bUnityEngine\b|\bUnityEditor\b");
            yield return C("float", @"\bfloat\b|\bSingle\b|(?<![\w.])\d+\.\d+|(?<![\w.])\d+[fF]\b");
            yield return C("double", @"\bdouble\b|\bDouble\b|(?<![\w.])\d+[dD]\b");
            yield return C("decimal", @"\bdecimal\b|\bDecimal\b|(?<![\w.])\d+[mM]\b");
            yield return C("Math (floating point helpers)", @"\bMath(F)?\s*\.");
            yield return C("Environment / Guid", @"\bEnvironment\b|\bGuid\b");
            yield return C("threads and tasks", @"\bSystem\s*\.\s*Threading\b|\bThread\b|\bTask\b|\basync\b|\bawait\b");
            yield return C("file system / console", @"\bSystem\s*\.\s*IO\b|\bConsole\b");
            yield return C("unsafe code / native interop", @"\bunsafe\b|\bDllImport\b|\bextern\b");
        }

        [TestCaseSource(nameof(Forbidden))]
        public void DoesNotUse(string pattern)
        {
            var re = new Regex(pattern);
            foreach (var (file, code) in CoreSources.Load())
            {
                var m = re.Match(code);
                Assert.That(m.Success, Is.False, file + " uses a forbidden API: \"" + m.Value + "\"");
            }
        }

        [Test]
        public void UsesOnlyAllowedNamespacesAndTheAsmdefForbidsEngineReferences()
        {
            var allowed = new HashSet<string>
            {
                "System", "System.Collections", "System.Collections.Generic", "System.Collections.ObjectModel", "System.Globalization",
                "System.Text", "Edgewise.Core", "Edgewise.Core.Data",
            };
            var usingRe = new Regex(@"^\s*using\s+(static\s+)?([\w.]+)\s*;", RegexOptions.Multiline);
            foreach (var (file, code) in CoreSources.Load())
            {
                foreach (Match m in usingRe.Matches(code))
                    Assert.That(allowed, Does.Contain(m.Groups[2].Value), file + " uses namespace " + m.Groups[2].Value);
                Assert.That(Regex.IsMatch(code, @"\bnamespace\s+Edgewise\.Core\b"), Is.True, file + " must be in Edgewise.Core");
            }

            var asmdef = File.ReadAllText(Path.Combine(CoreSources.CoreDir(), "Edgewise.Core.asmdef"));
            Assert.That(Regex.IsMatch(asmdef, "\"noEngineReferences\"\\s*:\\s*true"), Is.True, "Edgewise.Core.asmdef must set noEngineReferences");
            Assert.That(Regex.IsMatch(asmdef, "\"references\"\\s*:\\s*\\[\\s*\\]"), Is.True, "Edgewise.Core.asmdef must not reference other assemblies");
        }
    }

    [TestFixture]
    public class CorePurityRuntimeTests
    {
        [Test]
        public void HasNoMutableStaticStateAndGamesAreIndependentOfInterleaving()
        {
            // 1) Core の型に可変な static フィールドがない（コンパイラ生成型は除く）
            var coreTypes = typeof(GameEngine).Assembly.GetTypes()
                .Where(t => t.Namespace != null && (t.Namespace == "Edgewise.Core" || t.Namespace == "Edgewise.Core.Data" || t.Namespace == "Edgewise.Core.AI"))
                .Where(t => !t.Name.Contains("<") && !(t.DeclaringType?.Name.Contains("<") ?? false))
                .ToList();
            Assert.That(coreTypes, Has.Member(typeof(GameEngine)));
            Assert.That(coreTypes, Has.Member(typeof(Edgewise.Core.AI.SearchSession)));
            foreach (var t in coreTypes)
            {
                foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.Name.Contains("<")) continue;
                    Assert.That(f.IsInitOnly || f.IsLiteral, Is.True, t.FullName + "." + f.Name + " is a mutable static field");
                }
            }

            // 2) 200 局を順番に流した結果と、全局を 1 手ずつ交互に進めた結果が一致する
            const int Games = 200;
            var sequential = new List<List<string>>();
            for (int g = 0; g < Games; g++)
            {
                var r = new TestRandom(g);
                var state = NewRandomGame(r);
                var hashes = new List<string>();
                while (state.Phase == Phase.Playing)
                {
                    var move = r.Pick(GameEngine.LegalMoves(state));
                    GameEngine.ApplyMoveFast(state, move);
                    state = GameEngine.ApplyMove(state, move).State;
                    hashes.Add(GameEngine.HashState(state));
                }
                sequential.Add(hashes);
            }

            var rands = Enumerable.Range(0, Games).Select(g => new TestRandom(g)).ToArray();
            var states = rands.Select(r => NewRandomGame(r)).ToArray();
            var interleaved = Enumerable.Range(0, Games).Select(_ => new List<string>()).ToArray();
            bool any = true;
            while (any)
            {
                any = false;
                for (int g = Games - 1; g >= 0; g--)
                {
                    if (states[g].Phase != Phase.Playing) continue;
                    any = true;
                    states[g] = GameEngine.ApplyMove(states[g], rands[g].Pick(GameEngine.LegalMoves(states[g]))).State;
                    interleaved[g].Add(GameEngine.HashState(states[g]));
                }
            }
            for (int g = 0; g < Games; g++) Assert.That(interleaved[g], Is.EqualTo(sequential[g]), "game " + g);
        }
    }
}
