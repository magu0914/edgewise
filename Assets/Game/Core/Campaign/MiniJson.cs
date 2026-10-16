using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Edgewise.Core.Campaign
{
    /// <summary>JSON の値の種類。数値は整数だけを扱う（Core は浮動小数点を使わない）。</summary>
    public enum JsonKind
    {
        Null = 0,
        Bool = 1,
        Integer = 2,
        String = 3,
        Array = 4,
        Object = 5,
    }

    /// <summary>
    /// セーブ用の小さな JSON の値（DESIGN §12-8）。Core は Unity の JsonUtility も外部ライブラリも使えないので自前で持つ。
    /// オブジェクトのキーは書き出し時に挿入順を保つ（同じ内容なら同じ文字列になる）。
    /// </summary>
    public sealed class JsonValue
    {
        public static readonly JsonValue Null = new JsonValue(JsonKind.Null);
        public static readonly JsonValue True = new JsonValue(JsonKind.Bool) { _bool = true };
        public static readonly JsonValue False = new JsonValue(JsonKind.Bool);

        bool _bool;
        long _int;
        string _string;
        List<JsonValue> _array;
        List<KeyValuePair<string, JsonValue>> _object;

        public JsonKind Kind { get; }

        JsonValue(JsonKind kind)
        {
            Kind = kind;
        }

        public static JsonValue Bool(bool v) => v ? True : False;

        public static JsonValue Integer(long v) => new JsonValue(JsonKind.Integer) { _int = v };

        public static JsonValue String(string v) => v == null ? Null : new JsonValue(JsonKind.String) { _string = v };

        public static JsonValue NewArray() => new JsonValue(JsonKind.Array) { _array = new List<JsonValue>() };

        public static JsonValue NewObject() => new JsonValue(JsonKind.Object) { _object = new List<KeyValuePair<string, JsonValue>>() };

        public bool AsBool => Kind == JsonKind.Bool ? _bool : throw new JsonException("expected a boolean");

        public long AsLong => Kind == JsonKind.Integer ? _int : throw new JsonException("expected an integer");

        public string AsString => Kind == JsonKind.String ? _string : throw new JsonException("expected a string");

        public IReadOnlyList<JsonValue> Items => Kind == JsonKind.Array ? _array : throw new JsonException("expected an array");

        public IReadOnlyList<KeyValuePair<string, JsonValue>> Members =>
            Kind == JsonKind.Object ? _object : throw new JsonException("expected an object");

        /// <summary>配列に追加する（組み立て用）。</summary>
        public JsonValue Add(JsonValue v)
        {
            if (Kind != JsonKind.Array) throw new JsonException("not an array");
            _array.Add(v ?? Null);
            return this;
        }

        /// <summary>オブジェクトにキーを設定する（組み立て用。同じキーは上書き）。</summary>
        public JsonValue Set(string key, JsonValue v)
        {
            if (Kind != JsonKind.Object) throw new JsonException("not an object");
            if (key == null) throw new ArgumentNullException(nameof(key));
            for (int i = 0; i < _object.Count; i++)
            {
                if (_object[i].Key == key)
                {
                    _object[i] = new KeyValuePair<string, JsonValue>(key, v ?? Null);
                    return this;
                }
            }
            _object.Add(new KeyValuePair<string, JsonValue>(key, v ?? Null));
            return this;
        }

        /// <summary>オブジェクトのメンバー（なければ null）。</summary>
        public JsonValue Get(string key)
        {
            if (Kind != JsonKind.Object) return null;
            for (int i = 0; i < _object.Count; i++)
            {
                if (_object[i].Key == key) return _object[i].Value;
            }
            return null;
        }

        public override string ToString() => MiniJson.Write(this);
    }

    /// <summary>JSON として読めない・形が違う。CampaignSave の中で捕まえて初期状態に戻す。</summary>
    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// 最小限の JSON 読み書き。数値は 64bit 整数だけ（小数・指数は不正として扱う）。
    /// 入れ子の深さを制限し（スタックあふれは捕まえられないため）、どんな入力でも JsonException 以外の例外を出さない。
    /// </summary>
    public static class MiniJson
    {
        /// <summary>入れ子の最大の深さ。</summary>
        public const int MaxDepth = 32;

        /// <summary>入力文字列の最大長（壊れたデータで時間を食わないため）。</summary>
        public const int MaxLength = 1 << 20;

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new JsonException("no data");
            if (text.Length > MaxLength) throw new JsonException("data too large");
            var p = new Parser(text);
            p.SkipWhitespace();
            var v = p.ParseValue(0);
            p.SkipWhitespace();
            if (!p.AtEnd) throw new JsonException("unexpected data after the value at " + p.Position);
            return v;
        }

        public static string Write(JsonValue value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value ?? JsonValue.Null);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, JsonValue v)
        {
            switch (v.Kind)
            {
                case JsonKind.Null:
                    sb.Append("null");
                    break;
                case JsonKind.Bool:
                    sb.Append(v.AsBool ? "true" : "false");
                    break;
                case JsonKind.Integer:
                    sb.Append(v.AsLong.ToString(CultureInfo.InvariantCulture));
                    break;
                case JsonKind.String:
                    WriteString(sb, v.AsString);
                    break;
                case JsonKind.Array:
                    sb.Append('[');
                    for (int i = 0; i < v.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        WriteValue(sb, v.Items[i]);
                    }
                    sb.Append(']');
                    break;
                default:
                    sb.Append('{');
                    for (int i = 0; i < v.Members.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        WriteString(sb, v.Members[i].Key);
                        sb.Append(':');
                        WriteValue(sb, v.Members[i].Value);
                    }
                    sb.Append('}');
                    break;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string s)
            {
                _s = s;
            }

            public bool AtEnd => _i >= _s.Length;

            public int Position => _i;

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') _i++;
                    else break;
                }
            }

            JsonException Error(string what) => new JsonException(what + " at " + _i);

            char Peek()
            {
                if (_i >= _s.Length) throw Error("unexpected end of data");
                return _s[_i];
            }

            void Expect(string word)
            {
                if (_i + word.Length > _s.Length || string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error("expected " + word);
                _i += word.Length;
            }

            public JsonValue ParseValue(int depth)
            {
                if (depth > MaxDepth) throw Error("nested too deeply");
                char c = Peek();
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return JsonValue.String(ParseString());
                    case 't': Expect("true"); return JsonValue.True;
                    case 'f': Expect("false"); return JsonValue.False;
                    case 'n': Expect("null"); return JsonValue.Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseInteger();
                        throw Error("unexpected character '" + c + "'");
                }
            }

            JsonValue ParseObject(int depth)
            {
                _i++; // {
                var obj = JsonValue.NewObject();
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _i++;
                    return obj;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw Error("expected a key");
                    string key = ParseString();
                    SkipWhitespace();
                    if (Peek() != ':') throw Error("expected ':'");
                    _i++;
                    SkipWhitespace();
                    obj.Set(key, ParseValue(depth + 1));
                    SkipWhitespace();
                    char c = Peek();
                    _i++;
                    if (c == '}') return obj;
                    if (c != ',') throw Error("expected ',' or '}'");
                }
            }

            JsonValue ParseArray(int depth)
            {
                _i++; // [
                var arr = JsonValue.NewArray();
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _i++;
                    return arr;
                }
                while (true)
                {
                    SkipWhitespace();
                    arr.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    char c = Peek();
                    _i++;
                    if (c == ']') return arr;
                    if (c != ',') throw Error("expected ',' or ']'");
                }
            }

            string ParseString()
            {
                _i++; // "
                var sb = new StringBuilder();
                while (true)
                {
                    char c = Peek();
                    _i++;
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Error("control character in string");
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    char e = Peek();
                    _i++;
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            int code = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                char h = Peek();
                                _i++;
                                int d = h >= '0' && h <= '9' ? h - '0' : h >= 'a' && h <= 'f' ? h - 'a' + 10 : h >= 'A' && h <= 'F' ? h - 'A' + 10 : -1;
                                if (d < 0) throw Error("bad \\u escape");
                                code = code * 16 + d;
                            }
                            sb.Append((char)code);
                            break;
                        default:
                            throw Error("bad escape");
                    }
                }
            }

            JsonValue ParseInteger()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                int digits = 0;
                while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9')
                {
                    _i++;
                    digits++;
                }
                if (digits == 0) throw Error("expected a digit");
                if (_i < _s.Length && (_s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E')) throw Error("only integers are supported");
                if (digits > 18) throw Error("integer too large");
                return JsonValue.Integer(long.Parse(_s.Substring(start, _i - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
            }
        }
    }
}
