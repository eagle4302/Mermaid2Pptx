using System.Globalization;
using System.Text.RegularExpressions;

namespace Mermaid2Pptx;

public static partial class SvgTransformResolver
{
    public static AffineMatrix Parse(string? transform)
    {
        if (string.IsNullOrWhiteSpace(transform))
        {
            return AffineMatrix.Identity;
        }

        var matrix = AffineMatrix.Identity;
        foreach (Match match in TransformRegex().Matches(transform))
        {
            var name = match.Groups["name"].Value;
            var values = ParseNumbers(match.Groups["args"].Value).ToArray();
            var next = name.ToLowerInvariant() switch
            {
                "translate" => Translate(values.ElementAtOrDefault(0), values.ElementAtOrDefault(1)),
                "scale" => Scale(values.ElementAtOrDefault(0, 1), values.Length > 1 ? values[1] : values.ElementAtOrDefault(0, 1)),
                "rotate" => Rotate(values),
                "matrix" when values.Length >= 6 => new AffineMatrix(values[0], values[1], values[2], values[3], values[4], values[5]),
                "skewx" => SkewX(values.ElementAtOrDefault(0)),
                "skewy" => SkewY(values.ElementAtOrDefault(0)),
                _ => AffineMatrix.Identity
            };

            matrix = matrix.Multiply(next);
        }

        return matrix;
    }

    public static AffineMatrix Translate(double x, double y) => new(1, 0, 0, 1, x, y);
    public static AffineMatrix Scale(double x, double y) => new(x, 0, 0, y, 0, 0);

    private static AffineMatrix Rotate(IReadOnlyList<double> values)
    {
        var radians = DegreesToRadians(values.Count > 0 ? values[0] : 0);
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var rotation = new AffineMatrix(cos, sin, -sin, cos, 0, 0);
        if (values.Count < 3)
        {
            return rotation;
        }

        var cx = values[1];
        var cy = values[2];
        return Translate(cx, cy).Multiply(rotation).Multiply(Translate(-cx, -cy));
    }

    private static AffineMatrix SkewX(double degrees)
    {
        var tangent = Math.Tan(DegreesToRadians(degrees));
        return new AffineMatrix(1, 0, tangent, 1, 0, 0);
    }

    private static AffineMatrix SkewY(double degrees)
    {
        var tangent = Math.Tan(DegreesToRadians(degrees));
        return new AffineMatrix(1, tangent, 0, 1, 0, 0);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    private static IEnumerable<double> ParseNumbers(string text)
    {
        foreach (Match match in NumberRegex().Matches(text))
        {
            if (double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                yield return value;
            }
        }
    }

    private static T ElementAtOrDefault<T>(this IReadOnlyList<T> values, int index, T fallback = default!)
        where T : struct =>
        index >= 0 && index < values.Count ? values[index] : fallback;

    [GeneratedRegex(@"(?<name>[a-zA-Z]+)\s*\((?<args>[^)]*)\)", RegexOptions.Compiled)]
    private static partial Regex TransformRegex();

    [GeneratedRegex(@"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", RegexOptions.Compiled)]
    private static partial Regex NumberRegex();
}
