using System.Globalization;
using System.Text.RegularExpressions;

namespace Mermaid2Pptx;

public sealed partial class SvgPathParser
{
    public IReadOnlyList<SvgPathSegment> Parse(string? pathData)
    {
        if (string.IsNullOrWhiteSpace(pathData))
        {
            return [];
        }

        var tokens = Tokenize(pathData);
        var segments = new List<SvgPathSegment>();
        var index = 0;
        var command = '\0';
        var current = new SvgPoint(0, 0);
        var subpathStart = new SvgPoint(0, 0);
        SvgPoint? lastCubicControl = null;
        SvgPoint? lastQuadraticControl = null;
        char previousCommand = '\0';

        while (index < tokens.Count)
        {
            if (IsCommand(tokens[index]))
            {
                command = tokens[index++][0];
            }
            else if (command == '\0')
            {
                throw new FormatException("SVG path data must start with a command.");
            }

            var lower = char.ToLowerInvariant(command);
            var relative = char.IsLower(command);

            if (lower == 'z')
            {
                segments.Add(new ClosePath());
                current = subpathStart;
                lastCubicControl = null;
                lastQuadraticControl = null;
                previousCommand = command;
                continue;
            }

            if (lower == 'm')
            {
                var first = true;
                while (HasNumber(tokens, index))
                {
                    var point = ReadPoint(tokens, ref index);
                    if (relative)
                    {
                        point += current;
                    }

                    if (first)
                    {
                        segments.Add(new MoveTo(point));
                        subpathStart = point;
                        first = false;
                    }
                    else
                    {
                        segments.Add(new LineTo(point));
                    }

                    current = point;
                    lastCubicControl = null;
                    lastQuadraticControl = null;
                    previousCommand = first ? command : (relative ? 'l' : 'L');
                }

                continue;
            }

            while (HasNumber(tokens, index))
            {
                switch (lower)
                {
                    case 'l':
                    {
                        var point = ReadPoint(tokens, ref index);
                        if (relative)
                        {
                            point += current;
                        }

                        segments.Add(new LineTo(point));
                        current = point;
                        lastCubicControl = null;
                        lastQuadraticControl = null;
                        break;
                    }
                    case 'h':
                    {
                        var x = ReadNumber(tokens, ref index);
                        var point = new SvgPoint(relative ? current.X + x : x, current.Y);
                        segments.Add(new LineTo(point));
                        current = point;
                        lastCubicControl = null;
                        lastQuadraticControl = null;
                        break;
                    }
                    case 'v':
                    {
                        var y = ReadNumber(tokens, ref index);
                        var point = new SvgPoint(current.X, relative ? current.Y + y : y);
                        segments.Add(new LineTo(point));
                        current = point;
                        lastCubicControl = null;
                        lastQuadraticControl = null;
                        break;
                    }
                    case 'c':
                    {
                        var c1 = ReadPoint(tokens, ref index);
                        var c2 = ReadPoint(tokens, ref index);
                        var point = ReadPoint(tokens, ref index);
                        if (relative)
                        {
                            c1 += current;
                            c2 += current;
                            point += current;
                        }

                        segments.Add(new CubicBezierTo(c1, c2, point));
                        current = point;
                        lastCubicControl = c2;
                        lastQuadraticControl = null;
                        break;
                    }
                    case 's':
                    {
                        var c1 = previousCommand is 'C' or 'c' or 'S' or 's' && lastCubicControl.HasValue
                            ? Reflect(lastCubicControl.Value, current)
                            : current;
                        var c2 = ReadPoint(tokens, ref index);
                        var point = ReadPoint(tokens, ref index);
                        if (relative)
                        {
                            c2 += current;
                            point += current;
                        }

                        segments.Add(new CubicBezierTo(c1, c2, point));
                        current = point;
                        lastCubicControl = c2;
                        lastQuadraticControl = null;
                        break;
                    }
                    case 'q':
                    {
                        var control = ReadPoint(tokens, ref index);
                        var point = ReadPoint(tokens, ref index);
                        if (relative)
                        {
                            control += current;
                            point += current;
                        }

                        segments.Add(new QuadraticBezierTo(control, point));
                        current = point;
                        lastQuadraticControl = control;
                        lastCubicControl = null;
                        break;
                    }
                    case 't':
                    {
                        var control = previousCommand is 'Q' or 'q' or 'T' or 't' && lastQuadraticControl.HasValue
                            ? Reflect(lastQuadraticControl.Value, current)
                            : current;
                        var point = ReadPoint(tokens, ref index);
                        if (relative)
                        {
                            point += current;
                        }

                        segments.Add(new QuadraticBezierTo(control, point));
                        current = point;
                        lastQuadraticControl = control;
                        lastCubicControl = null;
                        break;
                    }
                    case 'a':
                    {
                        var rx = Math.Abs(ReadNumber(tokens, ref index));
                        var ry = Math.Abs(ReadNumber(tokens, ref index));
                        var rotation = ReadNumber(tokens, ref index);
                        var largeArc = Math.Abs(ReadNumber(tokens, ref index)) > 0.5;
                        var sweep = Math.Abs(ReadNumber(tokens, ref index)) > 0.5;
                        var end = ReadPoint(tokens, ref index);
                        if (relative)
                        {
                            end += current;
                        }

                        foreach (var cubic in ArcToCubics(current, rx, ry, rotation, largeArc, sweep, end))
                        {
                            segments.Add(cubic);
                        }

                        current = end;
                        lastCubicControl = segments.LastOrDefault() is CubicBezierTo c ? c.Control2 : null;
                        lastQuadraticControl = null;
                        break;
                    }
                    default:
                        throw new FormatException($"Unsupported SVG path command '{command}'.");
                }

                previousCommand = command;
            }
        }

        return segments;
    }

    public static CubicBezierTo QuadraticToCubic(SvgPoint start, QuadraticBezierTo quadratic)
    {
        var c1 = start + (quadratic.Control - start) * (2d / 3d);
        var c2 = quadratic.Point + (quadratic.Control - quadratic.Point) * (2d / 3d);
        return new CubicBezierTo(c1, c2, quadratic.Point);
    }

    private static IReadOnlyList<CubicBezierTo> ArcToCubics(
        SvgPoint start,
        double rx,
        double ry,
        double xAxisRotation,
        bool largeArc,
        bool sweep,
        SvgPoint end)
    {
        if (rx <= 0 || ry <= 0 || Distance(start, end) < 0.000001)
        {
            return [new CubicBezierTo(start, end, end)];
        }

        var phi = xAxisRotation * Math.PI / 180d;
        var cosPhi = Math.Cos(phi);
        var sinPhi = Math.Sin(phi);

        var dx = (start.X - end.X) / 2d;
        var dy = (start.Y - end.Y) / 2d;
        var x1Prime = cosPhi * dx + sinPhi * dy;
        var y1Prime = -sinPhi * dx + cosPhi * dy;

        var rxSq = rx * rx;
        var rySq = ry * ry;
        var x1Sq = x1Prime * x1Prime;
        var y1Sq = y1Prime * y1Prime;

        var radiiScale = x1Sq / rxSq + y1Sq / rySq;
        if (radiiScale > 1)
        {
            var scale = Math.Sqrt(radiiScale);
            rx *= scale;
            ry *= scale;
            rxSq = rx * rx;
            rySq = ry * ry;
        }

        var sign = largeArc == sweep ? -1d : 1d;
        var numerator = rxSq * rySq - rxSq * y1Sq - rySq * x1Sq;
        var denominator = rxSq * y1Sq + rySq * x1Sq;
        var centerFactor = denominator == 0 ? 0 : sign * Math.Sqrt(Math.Max(0, numerator / denominator));
        var cxPrime = centerFactor * (rx * y1Prime / ry);
        var cyPrime = centerFactor * (-ry * x1Prime / rx);

        var cx = cosPhi * cxPrime - sinPhi * cyPrime + (start.X + end.X) / 2d;
        var cy = sinPhi * cxPrime + cosPhi * cyPrime + (start.Y + end.Y) / 2d;

        var theta1 = VectorAngle(1, 0, (x1Prime - cxPrime) / rx, (y1Prime - cyPrime) / ry);
        var deltaTheta = VectorAngle(
            (x1Prime - cxPrime) / rx,
            (y1Prime - cyPrime) / ry,
            (-x1Prime - cxPrime) / rx,
            (-y1Prime - cyPrime) / ry);

        if (!sweep && deltaTheta > 0)
        {
            deltaTheta -= Math.PI * 2;
        }
        else if (sweep && deltaTheta < 0)
        {
            deltaTheta += Math.PI * 2;
        }

        var segmentCount = Math.Max(1, (int)Math.Ceiling(Math.Abs(deltaTheta) / (Math.PI / 2d)));
        var segmentDelta = deltaTheta / segmentCount;
        var result = new List<CubicBezierTo>(segmentCount);

        for (var i = 0; i < segmentCount; i++)
        {
            var t1 = theta1 + i * segmentDelta;
            var t2 = t1 + segmentDelta;
            result.Add(ArcSegmentToCubic(cx, cy, rx, ry, phi, t1, t2));
        }

        return result;
    }

    private static CubicBezierTo ArcSegmentToCubic(double cx, double cy, double rx, double ry, double phi, double t1, double t2)
    {
        var delta = t2 - t1;
        var alpha = Math.Sin(delta) * (Math.Sqrt(4 + 3 * Math.Pow(Math.Tan(delta / 2), 2)) - 1) / 3d;

        var p1 = EllipsePoint(cx, cy, rx, ry, phi, t1);
        var p2 = EllipsePoint(cx, cy, rx, ry, phi, t2);
        var d1 = EllipseDerivative(rx, ry, phi, t1);
        var d2 = EllipseDerivative(rx, ry, phi, t2);

        return new CubicBezierTo(
            new SvgPoint(p1.X + alpha * d1.X, p1.Y + alpha * d1.Y),
            new SvgPoint(p2.X - alpha * d2.X, p2.Y - alpha * d2.Y),
            p2);
    }

    private static SvgPoint EllipsePoint(double cx, double cy, double rx, double ry, double phi, double theta)
    {
        var cosPhi = Math.Cos(phi);
        var sinPhi = Math.Sin(phi);
        var cosTheta = Math.Cos(theta);
        var sinTheta = Math.Sin(theta);
        return new SvgPoint(
            cx + rx * cosPhi * cosTheta - ry * sinPhi * sinTheta,
            cy + rx * sinPhi * cosTheta + ry * cosPhi * sinTheta);
    }

    private static SvgPoint EllipseDerivative(double rx, double ry, double phi, double theta)
    {
        var cosPhi = Math.Cos(phi);
        var sinPhi = Math.Sin(phi);
        var cosTheta = Math.Cos(theta);
        var sinTheta = Math.Sin(theta);
        return new SvgPoint(
            -rx * cosPhi * sinTheta - ry * sinPhi * cosTheta,
            -rx * sinPhi * sinTheta + ry * cosPhi * cosTheta);
    }

    private static double VectorAngle(double ux, double uy, double vx, double vy)
    {
        var sign = ux * vy - uy * vx < 0 ? -1d : 1d;
        var dot = ux * vx + uy * vy;
        var length = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
        if (length == 0)
        {
            return 0;
        }

        return sign * Math.Acos(Math.Max(-1, Math.Min(1, dot / length)));
    }

    private static double Distance(SvgPoint a, SvgPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static SvgPoint Reflect(SvgPoint control, SvgPoint current) =>
        new(current.X * 2 - control.X, current.Y * 2 - control.Y);

    private static SvgPoint ReadPoint(IReadOnlyList<string> tokens, ref int index) =>
        new(ReadNumber(tokens, ref index), ReadNumber(tokens, ref index));

    private static double ReadNumber(IReadOnlyList<string> tokens, ref int index)
    {
        if (index >= tokens.Count || IsCommand(tokens[index]))
        {
            throw new FormatException("Unexpected end of SVG path number list.");
        }

        return double.Parse(tokens[index++], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static bool HasNumber(IReadOnlyList<string> tokens, int index) =>
        index < tokens.Count && !IsCommand(tokens[index]);

    private static bool IsCommand(string token) => token.Length == 1 && char.IsLetter(token[0]);

    private static IReadOnlyList<string> Tokenize(string pathData) =>
        PathTokenRegex().Matches(pathData).Select(match => match.Value).ToList();

    [GeneratedRegex(@"[AaCcHhLlMmQqSsTtVvZz]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", RegexOptions.Compiled)]
    private static partial Regex PathTokenRegex();
}
