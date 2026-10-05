using System.Globalization;
using System.Text;
using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.IO;

/// <summary>The DATA section of an ISO 10303-21 (STEP) file as entities by id. Arguments are numbers (double or long),
/// strings, enumerations (as their name; .T./.F. as bools), references, nested lists, and null for $ and *.</summary>
internal static class StepParser
{
    public sealed record Ref(int Id);

    public sealed class Entity(int id, string type, List<object?> args, List<Entity>? parts = null)
    {
        public int Id { get; } = id;

        /// <summary>The entity's type, or "" for a complex instance made of <see cref="Parts"/>.</summary>
        public string Type { get; } = type;

        public List<object?> Args { get; } = args;
        public List<Entity>? Parts { get; } = parts;
    }

    public static Dictionary<int, Entity> Parse(string text)
    {
        var start = text.IndexOf("DATA;", StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidDataException("not a STEP file (no DATA section)");
        var result = new Dictionary<int, Entity>();
        var i = start + 5;
        while (i < text.Length)
        {
            SkipSpace(text, ref i);
            if (i >= text.Length || text.AsSpan(i).StartsWith("ENDSEC"))
                break;
            if (text[i] != '#')
            {
                // Anything else up to the next statement end (a stray token) is skipped.
                SkipStatement(text, ref i);
                continue;
            }
            i++;
            var id = (int)ReadInteger(text, ref i);
            SkipSpace(text, ref i);
            if (text[i] != '=')
                throw new InvalidDataException($"expected '=' after #{id}");
            i++;
            SkipSpace(text, ref i);
            Entity entity;
            if (text[i] == '(')
            {
                i++;
                var parts = new List<Entity>();
                while (true)
                {
                    SkipSpace(text, ref i);
                    if (text[i] == ')')
                    {
                        i++;
                        break;
                    }
                    var name = ReadKeyword(text, ref i);
                    SkipSpace(text, ref i);
                    parts.Add(new Entity(id, name, ReadList(text, ref i)));
                }
                entity = new Entity(id, "", [], parts);
            }
            else
            {
                var name = ReadKeyword(text, ref i);
                SkipSpace(text, ref i);
                entity = new Entity(id, name, ReadList(text, ref i));
            }
            result[id] = entity;
            SkipStatement(text, ref i);
        }
        return result;
    }

    private static void SkipSpace(string t, ref int i)
    {
        while (i < t.Length)
        {
            if (char.IsWhiteSpace(t[i]))
                i++;
            else if (t[i] == '/' && i + 1 < t.Length && t[i + 1] == '*')
            {
                var end = t.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? t.Length : end + 2;
            }
            else
                break;
        }
    }

    private static void SkipStatement(string t, ref int i)
    {
        while (i < t.Length && t[i] != ';')
        {
            if (t[i] == '\'')
                ReadString(t, ref i);
            else
                i++;
        }
        i++;
    }

    private static long ReadInteger(string t, ref int i)
    {
        var s = i;
        while (i < t.Length && char.IsDigit(t[i]))
            i++;
        return long.Parse(t.AsSpan(s, i - s), CultureInfo.InvariantCulture);
    }

    private static string ReadKeyword(string t, ref int i)
    {
        var s = i;
        while (i < t.Length && (char.IsLetterOrDigit(t[i]) || t[i] == '_' || t[i] == '!'))
            i++;
        return t[s..i];
    }

    private static string ReadString(string t, ref int i)
    {
        var sb = new StringBuilder();
        i++;
        while (i < t.Length)
        {
            if (t[i] == '\'')
            {
                if (i + 1 < t.Length && t[i + 1] == '\'')
                {
                    sb.Append('\'');
                    i += 2;
                    continue;
                }
                i++;
                break;
            }
            sb.Append(t[i++]);
        }
        return sb.ToString();
    }

    private static List<object?> ReadList(string t, ref int i)
    {
        var list = new List<object?>();
        if (t[i] != '(')
            throw new InvalidDataException($"expected '(' at {i}");
        i++;
        while (true)
        {
            SkipSpace(t, ref i);
            if (t[i] == ')')
            {
                i++;
                return list;
            }
            list.Add(ReadValue(t, ref i));
            SkipSpace(t, ref i);
            if (t[i] == ',')
                i++;
        }
    }

    private static object? ReadValue(string t, ref int i)
    {
        var c = t[i];
        switch (c)
        {
            case '(':
                return ReadList(t, ref i);
            case '\'':
                return ReadString(t, ref i);
            case '#':
                i++;
                return new Ref((int)ReadInteger(t, ref i));
            case '$' or '*':
                i++;
                return null;
            case '.':
            {
                var end = t.IndexOf('.', i + 1);
                var name = t[(i + 1)..end];
                i = end + 1;
                return name switch
                {
                    "T" => true,
                    "F" => false,
                    "U" => null,
                    _ => name,
                };
            }
            case '"':
            {
                var end = t.IndexOf('"', i + 1);
                var hex = t[(i + 1)..end];
                i = end + 1;
                return hex;
            }
        }
        if (char.IsDigit(c) || c is '-' or '+')
        {
            var s = i;
            i++;
            while (i < t.Length && (char.IsDigit(t[i]) || t[i] is '.' or 'E' or 'e' or '-' or '+'))
                i++;
            var token = t[s..i];
            return token.Contains('.') || token.Contains('E') || token.Contains('e')
                ? double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture)
                : long.Parse(token, CultureInfo.InvariantCulture);
        }
        // A typed value such as LENGTH_MEASURE(1.E-03): its content stands for it.
        ReadKeyword(t, ref i);
        SkipSpace(t, ref i);
        var inner = ReadList(t, ref i);
        return inner.Count == 1 ? inner[0] : inner;
    }
}

/// <summary>Rational B-spline evaluation by de Boor's algorithm (weights null for plain B-splines).</summary>
internal static class Nurbs
{
    public static int Span(int degree, List<double> knots, double t)
    {
        var n = knots.Count - degree - 2;
        if (t >= knots[n + 1])
            return n;
        if (t <= knots[degree])
            return degree;
        var (lo, hi) = (degree, n + 1);
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (t < knots[mid])
                hi = mid;
            else
                lo = mid;
        }
        return lo;
    }

    /// <summary>The <paramref name="degree"/> + 1 basis functions non-zero at <paramref name="t"/> in its span.</summary>
    public static double[] Basis(int degree, List<double> knots, int span, double t)
    {
        var n = new double[degree + 1];
        var left = new double[degree + 1];
        var right = new double[degree + 1];
        n[0] = 1;
        for (var j = 1; j <= degree; j++)
        {
            left[j] = t - knots[span + 1 - j];
            right[j] = knots[span + j] - t;
            var saved = 0.0;
            for (var r = 0; r < j; r++)
            {
                var denominator = right[r + 1] + left[j - r];
                var temp = denominator == 0 ? 0 : n[r] / denominator;
                n[r] = saved + right[r + 1] * temp;
                saved = left[j - r] * temp;
            }
            n[j] = saved;
        }
        return n;
    }

    public static Vec3 Curve(int degree, List<Vec3> control, List<double> knots, List<double>? weights, double t)
    {
        var span = Span(degree, knots, t);
        var basis = Basis(degree, knots, span, t);
        var sum = Vec3.Zero;
        var w = 0.0;
        for (var i = 0; i <= degree; i++)
        {
            var k = span - degree + i;
            var weight = basis[i] * (weights?[k] ?? 1);
            sum += control[k] * weight;
            w += weight;
        }
        return w == 0 ? sum : sum * (1 / w);
    }
}
