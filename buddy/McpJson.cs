#if FEATURE_MCP
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HeartopiaMod
{
    // ============================================================================================
    // JSON for the MCP bridge.
    //
    // PARSING is System.Text.Json (it ships in the loader's runtime folder, …/dotnet/), because the
    // parser is the piece that eats network input and battle-tested beats hand-rolled there. What
    // does NOT cross into the mod is JsonElement: every document is materialized into plain BCL
    // types and released on the socket thread, so a queued call can never hold a disposed
    // JsonDocument, and op handlers only ever see Dictionary/List/string/double/bool/null.
    //
    // WRITING stays hand-rolled: responses are assembled by splicing already-serialized fragments
    // (an op result, the cached describe blob) into an envelope, which a streaming StringBuilder
    // writer does in one pass with no re-parse and no intermediate object model.
    // ============================================================================================

    internal sealed class McpJsonException : Exception
    {
        internal McpJsonException(string message)
            : base(message)
        {
        }
    }

    // Streaming writer. Tracks comma placement per container so callers never write separators.
    // Deliberately not a struct: handlers pass it down into helper methods.
    internal sealed class McpJsonWriter
    {
        private readonly StringBuilder sb = new StringBuilder(256);
        private readonly List<bool> needsComma = new List<bool>(8);

        internal McpJsonWriter BeginObject()
        {
            this.Separate();
            this.sb.Append('{');
            this.needsComma.Add(false);
            return this;
        }

        internal McpJsonWriter BeginObject(string name)
        {
            this.Key(name);
            this.sb.Append('{');
            this.needsComma.Add(false);
            return this;
        }

        internal McpJsonWriter EndObject()
        {
            this.Pop();
            this.sb.Append('}');
            return this;
        }

        internal McpJsonWriter BeginArray(string name)
        {
            this.Key(name);
            this.sb.Append('[');
            this.needsComma.Add(false);
            return this;
        }

        internal McpJsonWriter EndArray()
        {
            this.Pop();
            this.sb.Append(']');
            return this;
        }

        // Element inside an array: opens an object with no key.
        internal McpJsonWriter BeginArrayObject()
        {
            this.Separate();
            this.sb.Append('{');
            this.needsComma.Add(false);
            return this;
        }

        internal McpJsonWriter Str(string name, string value)
        {
            this.Key(name);
            AppendString(this.sb, value);
            return this;
        }

        internal McpJsonWriter Num(string name, long value)
        {
            this.Key(name);
            this.sb.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        internal McpJsonWriter Num(string name, double value)
        {
            this.Key(name);
            AppendDouble(this.sb, value);
            return this;
        }

        internal McpJsonWriter Bool(string name, bool value)
        {
            this.Key(name);
            this.sb.Append(value ? "true" : "false");
            return this;
        }

        // Splices an already-serialized fragment (an op result, the describe blob) without
        // re-parsing it. The caller owns its validity.
        internal McpJsonWriter Raw(string name, string jsonFragment)
        {
            this.Key(name);
            this.sb.Append(string.IsNullOrEmpty(jsonFragment) ? "null" : jsonFragment);
            return this;
        }

        internal McpJsonWriter ArrayStr(string value)
        {
            this.Separate();
            AppendString(this.sb, value);
            return this;
        }

        public override string ToString() => this.sb.ToString();

        private void Key(string name)
        {
            this.Separate();
            AppendString(this.sb, name);
            this.sb.Append(':');
        }

        private void Separate()
        {
            int depth = this.needsComma.Count - 1;
            if (depth < 0)
            {
                return;
            }

            if (this.needsComma[depth])
            {
                this.sb.Append(',');
            }
            else
            {
                this.needsComma[depth] = true;
            }
        }

        private void Pop()
        {
            int depth = this.needsComma.Count - 1;
            if (depth >= 0)
            {
                this.needsComma.RemoveAt(depth);
            }
        }

        // NaN and the infinities are not JSON; they become null rather than emitting a token the
        // bridge cannot parse back.
        private static void AppendDouble(StringBuilder sb, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                sb.Append("null");
                return;
            }

            sb.Append(value.ToString("G17", CultureInfo.InvariantCulture));
        }

        internal static void AppendString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        // Control characters must be escaped; everything else (including non-BMP
                        // surrogate pairs) goes out as-is in UTF-8.
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }

            sb.Append('"');
        }

        internal static string Quote(string value)
        {
            StringBuilder sb = new StringBuilder((value?.Length ?? 0) + 2);
            AppendString(sb, value);
            return sb.ToString();
        }
    }

    // Materializing parser. Every failure — malformed JSON, too deep, a number that will not fit a
    // double — comes out as McpJsonException so the client loop has exactly one catch to map to
    // `bad_args`, instead of leaking JsonException/InvalidOperationException/OverflowException.
    internal static class McpJsonParser
    {
        private const int MaxDepth = 32;

        private static readonly JsonDocumentOptions Options = new JsonDocumentOptions
        {
            MaxDepth = MaxDepth,
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        };

        internal static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new McpJsonException("empty document");
            }

            try
            {
                // `using` matters: the document owns pooled buffers, and nothing below keeps a
                // JsonElement — Materialize copies everything into plain BCL objects.
                using (JsonDocument doc = JsonDocument.Parse(text, Options))
                {
                    return Materialize(doc.RootElement, 0);
                }
            }
            catch (McpJsonException)
            {
                throw;
            }
            catch (JsonException ex)
            {
                throw new McpJsonException(ex.Message);
            }
            catch (Exception ex)
            {
                throw new McpJsonException(ex.GetType().Name + ": " + ex.Message);
            }
        }

        internal static Dictionary<string, object> ParseObject(string text)
        {
            if (Parse(text) is Dictionary<string, object> map)
            {
                return map;
            }

            throw new McpJsonException("expected a JSON object");
        }

        private static object Materialize(JsonElement element, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new McpJsonException("nesting deeper than " + MaxDepth);
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        map[property.Name] = Materialize(property.Value, depth + 1);
                    }

                    return map;
                }

                case JsonValueKind.Array:
                {
                    List<object> list = new List<object>();
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        list.Add(Materialize(item, depth + 1));
                    }

                    return list;
                }

                case JsonValueKind.String:
                    return element.GetString();

                case JsonValueKind.Number:
                    if (element.TryGetDouble(out double number))
                    {
                        return number;
                    }

                    throw new McpJsonException("number out of range: " + element.GetRawText());

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                default:
                    return null;
            }
        }
    }

    // Argument accessors. Every one is total: a missing key, a null, or the wrong JSON type yields
    // the default instead of throwing, so op handlers never need defensive casts. Ops that require
    // an argument check for themselves and raise McpOpException("bad_args", …).
    internal static class McpArgs
    {
        internal static string GetString(Dictionary<string, object> args, string key, string fallback = null)
        {
            if (args != null && args.TryGetValue(key, out object v) && v is string s)
            {
                return s;
            }

            return fallback;
        }

        internal static int GetInt(Dictionary<string, object> args, string key, int fallback)
        {
            if (args != null && args.TryGetValue(key, out object v) && v is double d
                && !double.IsNaN(d) && d >= int.MinValue && d <= int.MaxValue)
            {
                return (int)d;
            }

            return fallback;
        }

        internal static bool GetBool(Dictionary<string, object> args, string key, bool fallback)
        {
            if (args != null && args.TryGetValue(key, out object v) && v is bool b)
            {
                return b;
            }

            return fallback;
        }
    }
}
#endif
