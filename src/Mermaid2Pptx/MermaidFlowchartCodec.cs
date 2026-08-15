using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Mermaid2Pptx;

public sealed class MermaidFlowchartParser
{
    private static readonly Regex InitDirective = new(@"%%\{[\s\S]*?\}%%", RegexOptions.CultureInvariant);
    private static readonly Regex Header = new(
        @"^\s*(flowchart|graph)\s+(TD|TB|BT|RL|LR)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OtherDiagram = new(
        @"^\s*(sequenceDiagram|classDiagram|erDiagram|stateDiagram(?:-v2)?|mindmap|architecture-beta|gantt|pie|gitGraph|journey|timeline|sankey-beta|quadrantChart|requirementDiagram|C4Context)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public IReadOnlyList<FlowchartGraph> ParseAll(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Mermaid source is required.", nameof(source));
        }

        var text = InitDirective.Replace(source.Replace("\r\n", "\n", StringComparison.Ordinal), "\n");
        var graphs = new List<FlowchartGraph>();
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = StripLineComment(lines[index]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var other = OtherDiagram.Match(line);
            if (other.Success)
            {
                throw new InvalidOperationException(
                    $"Mermaid to draw.io currently supports flowchart/graph diagrams only, not '{other.Groups[1].Value}'.");
            }

            var header = Header.Match(line);
            if (!header.Success)
            {
                continue;
            }

            var direction = NormalizeDirection(header.Groups[2].Value);
            var body = new StringBuilder();
            var remainder = line[header.Length..].Trim().TrimStart(';');
            if (remainder.Length > 0)
            {
                body.AppendLine(remainder);
            }

            index++;
            for (; index < lines.Length; index++)
            {
                var candidate = StripLineComment(lines[index]);
                if (Header.IsMatch(candidate) || OtherDiagram.IsMatch(candidate.Trim()))
                {
                    index--;
                    break;
                }

                body.AppendLine(candidate);
            }

            graphs.Add(ParseBody(body.ToString(), direction, $"Page-{graphs.Count + 1}"));
        }

        if (graphs.Count == 0)
        {
            throw new InvalidOperationException("No Mermaid flowchart/graph diagram was found.");
        }

        return graphs;
    }

    public FlowchartGraph Parse(string source) => ParseAll(source)[0];

    private static FlowchartGraph ParseBody(string body, string direction, string title)
    {
        var graph = new FlowchartGraph { Direction = direction, Title = title };
        var subgraphDepth = 0;
        foreach (var statement in Statements(body))
        {
            if (statement.StartsWith("subgraph ", StringComparison.OrdinalIgnoreCase) ||
                statement.Equals("subgraph", StringComparison.OrdinalIgnoreCase))
            {
                subgraphDepth++;
                continue;
            }

            if (statement.Equals("end", StringComparison.OrdinalIgnoreCase) && subgraphDepth > 0)
            {
                subgraphDepth--;
                continue;
            }

            if (statement.StartsWith("direction ", StringComparison.OrdinalIgnoreCase))
            {
                graph.Direction = NormalizeDirection(statement["direction ".Length..].Trim());
                continue;
            }

            if (ShouldSkip(statement))
            {
                continue;
            }

            try
            {
                ParseStatement(graph, statement);
            }
            catch (Exception exception)
            {
                graph.Warnings.Add($"Skipped '{statement}': {exception.Message}");
            }
        }

        if (graph.Nodes.Count == 0)
        {
            throw new InvalidOperationException("Mermaid flowchart did not contain any nodes.");
        }

        return graph;
    }

    private static void ParseStatement(FlowchartGraph graph, string statement)
    {
        var cursor = new Cursor(statement);
        var sources = ParseNodeList(graph, cursor);
        if (sources.Count == 0)
        {
            return;
        }

        while (TryParseLink(cursor, out var link))
        {
            var targets = ParseNodeList(graph, cursor);
            if (targets.Count == 0)
            {
                throw new InvalidOperationException("Edge is missing a target node.");
            }

            foreach (var source in sources)
            {
                foreach (var target in targets)
                {
                    graph.Edges.Add(new FlowchartEdge
                    {
                        SourceId = source.Id,
                        TargetId = target.Id,
                        Label = link.Label,
                        Dashed = link.Dashed,
                        Arrow = link.Arrow
                    });
                }
            }

            sources = targets;
        }
    }

    private static List<FlowchartNode> ParseNodeList(FlowchartGraph graph, Cursor cursor)
    {
        var nodes = new List<FlowchartNode>();
        cursor.SkipSpaces();
        if (cursor.Ended)
        {
            return nodes;
        }

        nodes.Add(ParseNode(graph, cursor));
        cursor.SkipSpaces();
        while (cursor.Peek() == '&')
        {
            cursor.Index++;
            nodes.Add(ParseNode(graph, cursor));
            cursor.SkipSpaces();
        }

        return nodes;
    }

    private static FlowchartNode ParseNode(FlowchartGraph graph, Cursor cursor)
    {
        cursor.SkipSpaces();
        var id = ReadId(cursor);
        if (id.Length == 0)
        {
            throw new InvalidOperationException("Expected a node id.");
        }

        var node = graph.GetOrAddNode(id);
        cursor.SkipSpaces();
        if (TryReadShape(cursor, out var kind, out var label))
        {
            node.Kind = kind;
            if (!string.IsNullOrWhiteSpace(label))
            {
                node.Label = WebUtility.HtmlDecode(label.Trim());
            }
        }
        else if (string.Equals(node.Label, node.Id, StringComparison.Ordinal))
        {
            node.Label = id;
        }

        return node;
    }

    private static bool TryReadShape(Cursor cursor, out FlowchartNodeKind kind, out string label)
    {
        kind = FlowchartNodeKind.Rectangle;
        label = string.Empty;
        if (cursor.StartsWith("[["))
        {
            kind = FlowchartNodeKind.Subroutine;
            label = ReadDelimited(cursor, 2, "]]");
            return true;
        }

        if (cursor.StartsWith("[("))
        {
            kind = FlowchartNodeKind.Cylinder;
            label = ReadDelimited(cursor, 2, ")]");
            return true;
        }

        if (cursor.StartsWith("(("))
        {
            kind = FlowchartNodeKind.Circle;
            label = ReadDelimited(cursor, 2, "))");
            return true;
        }

        if (cursor.StartsWith("(["))
        {
            kind = FlowchartNodeKind.Stadium;
            label = ReadDelimited(cursor, 2, "])");
            return true;
        }

        if (cursor.StartsWith("{{"))
        {
            kind = FlowchartNodeKind.Hexagon;
            label = ReadDelimited(cursor, 2, "}}");
            return true;
        }

        if (cursor.StartsWith("[/"))
        {
            kind = FlowchartNodeKind.Parallelogram;
            label = ReadDelimited(cursor, 2, "/]");
            return true;
        }

        if (cursor.StartsWith("[\\"))
        {
            kind = FlowchartNodeKind.Parallelogram;
            label = ReadDelimited(cursor, 2, "\\]");
            return true;
        }

        if (cursor.Peek() == '[')
        {
            kind = FlowchartNodeKind.Rectangle;
            label = ReadDelimited(cursor, 1, "]");
            return true;
        }

        if (cursor.Peek() == '(')
        {
            kind = FlowchartNodeKind.Rounded;
            label = ReadDelimited(cursor, 1, ")");
            return true;
        }

        if (cursor.Peek() == '{')
        {
            kind = FlowchartNodeKind.Diamond;
            label = ReadDelimited(cursor, 1, "}");
            return true;
        }

        if (cursor.Peek() == '>')
        {
            kind = FlowchartNodeKind.Rectangle;
            label = ReadDelimited(cursor, 1, "]");
            return true;
        }

        return false;
    }

    private static string ReadDelimited(Cursor cursor, int openLength, string close)
    {
        cursor.Index += openLength;
        cursor.SkipSpaces();
        if (cursor.Peek() == '"')
        {
            var quoted = ReadQuoted(cursor);
            cursor.SkipSpaces();
            if (!cursor.StartsWith(close))
            {
                throw new InvalidOperationException($"Shape is missing closing '{close}'.");
            }

            cursor.Index += close.Length;
            return quoted;
        }

        var start = cursor.Index;
        var closeAt = cursor.Text.IndexOf(close, cursor.Index, StringComparison.Ordinal);
        if (closeAt < 0)
        {
            throw new InvalidOperationException($"Shape is missing closing '{close}'.");
        }

        var value = cursor.Text[start..closeAt];
        cursor.Index = closeAt + close.Length;
        return value;
    }

    private static string ReadQuoted(Cursor cursor)
    {
        cursor.Index++;
        var start = cursor.Index;
        while (!cursor.Ended && cursor.Peek() != '"')
        {
            cursor.Index++;
        }

        var value = cursor.Text[start..cursor.Index];
        if (cursor.Peek() == '"')
        {
            cursor.Index++;
        }

        return value;
    }

    private static bool TryParseLink(Cursor cursor, out Link link)
    {
        cursor.SkipSpaces();
        link = new Link(string.Empty, false, true);
        if (cursor.Ended)
        {
            return false;
        }

        if (TryPipeLink(cursor, "-->|", dashed: false, arrow: true, out link) ||
            TryPipeLink(cursor, "---|", dashed: false, arrow: false, out link) ||
            TryPipeLink(cursor, "-.->|", dashed: true, arrow: true, out link) ||
            TryPipeLink(cursor, "-.-|", dashed: true, arrow: false, out link) ||
            TryPipeLink(cursor, "==>|", dashed: false, arrow: true, out link) ||
            TryPipeLink(cursor, "===|", dashed: false, arrow: false, out link))
        {
            return true;
        }

        if (cursor.StartsWith("-->") || cursor.StartsWith("--x") || cursor.StartsWith("--o"))
        {
            cursor.Index += 3;
            link = new Link(string.Empty, false, true);
            return true;
        }

        if (cursor.StartsWith("---"))
        {
            cursor.Index += 3;
            link = new Link(string.Empty, false, false);
            return true;
        }

        if (cursor.StartsWith("-.->"))
        {
            cursor.Index += 4;
            link = new Link(string.Empty, true, true);
            return true;
        }

        if (cursor.StartsWith("-.-"))
        {
            cursor.Index += 3;
            link = new Link(string.Empty, true, false);
            return true;
        }

        if (cursor.StartsWith("==>"))
        {
            cursor.Index += 3;
            link = new Link(string.Empty, false, true);
            return true;
        }

        if (cursor.StartsWith("==="))
        {
            cursor.Index += 3;
            link = new Link(string.Empty, false, false);
            return true;
        }

        if (cursor.StartsWith("-."))
        {
            return TrySpacedLink(cursor, "-.", ".->", dashed: true, arrow: true, out link) ||
                   TrySpacedLink(cursor, "-.", ".-", dashed: true, arrow: false, out link);
        }

        if (cursor.StartsWith("--"))
        {
            return TrySpacedLink(cursor, "--", "-->", dashed: false, arrow: true, out link) ||
                   TrySpacedLink(cursor, "--", "---", dashed: false, arrow: false, out link);
        }

        if (cursor.StartsWith("=="))
        {
            return TrySpacedLink(cursor, "==", "==>", dashed: false, arrow: true, out link);
        }

        return false;
    }

    private static bool TryPipeLink(Cursor cursor, string open, bool dashed, bool arrow, out Link link)
    {
        link = new Link(string.Empty, dashed, arrow);
        if (!cursor.StartsWith(open))
        {
            return false;
        }

        cursor.Index += open.Length;
        var close = cursor.Text.IndexOf('|', cursor.Index);
        if (close < 0)
        {
            throw new InvalidOperationException("Edge label is missing a closing '|'.");
        }

        var label = cursor.Text[cursor.Index..close].Trim();
        cursor.Index = close + 1;
        while (cursor.Peek() is '-' or '=' or '.' or '>' or 'x' or 'o')
        {
            cursor.Index++;
        }

        link = new Link(label, dashed, arrow);
        return true;
    }

    private static bool TrySpacedLink(Cursor cursor, string open, string close, bool dashed, bool arrow, out Link link)
    {
        link = new Link(string.Empty, dashed, arrow);
        if (!cursor.StartsWith(open))
        {
            return false;
        }

        var searchFrom = cursor.Index + open.Length;
        var closeAt = cursor.Text.IndexOf(close, searchFrom, StringComparison.Ordinal);
        if (closeAt < 0)
        {
            return false;
        }

        var label = cursor.Text[searchFrom..closeAt].Trim();
        if (label.Length == 0)
        {
            return false;
        }

        cursor.Index = closeAt + close.Length;
        link = new Link(label, dashed, arrow);
        return true;
    }

    private static string ReadId(Cursor cursor)
    {
        cursor.SkipSpaces();
        var start = cursor.Index;
        if (cursor.Ended || !IsIdStart(cursor.Peek()))
        {
            return string.Empty;
        }

        cursor.Index++;
        while (!cursor.Ended && IsIdPart(cursor.Peek()))
        {
            cursor.Index++;
        }

        return cursor.Text[start..cursor.Index];
    }

    private static bool IsIdStart(char value) => char.IsLetter(value) || value is '_';

    private static bool IsIdPart(char value) => char.IsLetterOrDigit(value) || value is '_' or '-';

    private static bool ShouldSkip(string statement)
    {
        return statement.StartsWith("classDef ", StringComparison.OrdinalIgnoreCase) ||
               statement.StartsWith("class ", StringComparison.OrdinalIgnoreCase) ||
               statement.StartsWith("style ", StringComparison.OrdinalIgnoreCase) ||
               statement.StartsWith("click ", StringComparison.OrdinalIgnoreCase) ||
               statement.StartsWith("link ", StringComparison.OrdinalIgnoreCase) ||
               statement.StartsWith("call ", StringComparison.OrdinalIgnoreCase) ||
               statement.StartsWith("accTitle", StringComparison.OrdinalIgnoreCase) ||
               statement.StartsWith("accDescr", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Statements(string body)
    {
        var builder = new StringBuilder();
        var depth = 0;
        var quoted = false;
        foreach (var ch in body)
        {
            if (ch == '"' && depth > 0)
            {
                quoted = !quoted;
            }

            if (!quoted)
            {
                if (ch is '[' or '(' or '{')
                {
                    depth++;
                }
                else if (ch is ']' or ')' or '}')
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (depth == 0 && ch is ';' or '\n')
                {
                    var statement = builder.ToString().Trim();
                    builder.Clear();
                    if (statement.Length > 0)
                    {
                        yield return statement;
                    }

                    continue;
                }
            }

            builder.Append(ch);
        }

        var last = builder.ToString().Trim();
        if (last.Length > 0)
        {
            yield return last;
        }
    }

    private static string StripLineComment(string line)
    {
        var comment = line.IndexOf("%%", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }

    private static string NormalizeDirection(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "TB" or "TD" or "DT" or "" => "TD",
            "LR" => "LR",
            "RL" => "RL",
            "BT" => "BT",
            _ => "TD"
        };
    }

    private sealed class Cursor(string text)
    {
        public string Text { get; } = text;
        public int Index;

        public bool Ended => Index >= Text.Length;
        public char Peek() => Ended ? '\0' : Text[Index];
        public bool StartsWith(string value) => Text.AsSpan(Index).StartsWith(value, StringComparison.Ordinal);

        public void SkipSpaces()
        {
            while (!Ended && char.IsWhiteSpace(Peek()) && Peek() is not '\n')
            {
                Index++;
            }
        }
    }

    private readonly record struct Link(string Label, bool Dashed, bool Arrow);
}

public static class MermaidFlowchartWriter
{
    public static string Write(IReadOnlyList<FlowchartGraph> graphs)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < graphs.Count; index++)
        {
            if (index > 0)
            {
                builder.AppendLine();
            }

            var graph = graphs[index];
            if (graphs.Count > 1)
            {
                builder.AppendLine($"%% page: {graph.Title}");
            }

            builder.Append("flowchart ").AppendLine(graph.Direction);
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var edge in graph.Edges)
            {
                var source = graph.Nodes.First(node => node.Id == edge.SourceId);
                var target = graph.Nodes.First(node => node.Id == edge.TargetId);
                builder.Append("  ")
                    .Append(FormatNode(source, declared))
                    .Append(' ')
                    .Append(FormatLink(edge))
                    .Append(' ')
                    .Append(FormatNode(target, declared))
                    .AppendLine();
            }

            foreach (var node in graph.Nodes)
            {
                if (declared.Contains(node.Id))
                {
                    continue;
                }

                builder.Append("  ").Append(FormatNode(node, declared)).AppendLine();
            }
        }

        return builder.ToString();
    }

    public static string Write(FlowchartGraph graph) => Write([graph]);

    private static string FormatNode(FlowchartNode node, HashSet<string> declared)
    {
        if (!declared.Add(node.Id))
        {
            return node.Id;
        }

        var inner = QuoteLabel(node.Label);
        return node.Kind switch
        {
            FlowchartNodeKind.Rounded => node.Id + "(" + inner + ")",
            FlowchartNodeKind.Stadium => node.Id + "([" + inner + "])",
            FlowchartNodeKind.Diamond => node.Id + "{" + inner + "}",
            FlowchartNodeKind.Circle => node.Id + "((" + inner + "))",
            FlowchartNodeKind.Cylinder => node.Id + "[(" + inner + ")]",
            FlowchartNodeKind.Hexagon => node.Id + "{{" + inner + "}}",
            FlowchartNodeKind.Parallelogram => node.Id + "[/" + inner + "/]",
            FlowchartNodeKind.Subroutine => node.Id + "[[" + inner + "]]",
            _ => node.Id + "[" + inner + "]"
        };
    }

    private static string FormatLink(FlowchartEdge edge)
    {
        if (string.IsNullOrWhiteSpace(edge.Label))
        {
            return (edge.Dashed, edge.Arrow) switch
            {
                (true, true) => "-.->",
                (true, false) => "-.-",
                (false, false) => "---",
                _ => "-->"
            };
        }

        var label = edge.Label.Replace("|", "/", StringComparison.Ordinal);
        return (edge.Dashed, edge.Arrow) switch
        {
            (true, true) => "-.->|" + label + "|",
            (true, false) => "-.-|" + label + "|",
            (false, false) => "---|" + label + "|",
            _ => "-->|" + label + "|"
        };
    }

    private static string QuoteLabel(string label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return "\"\"";
        }

        var needsQuotes = label.Any(ch => !char.IsLetterOrDigit(ch) && ch is not ' ' and not '-' and not '_' and not '/' and not '?');
        return needsQuotes ? "\"" + label.Replace("\"", "'", StringComparison.Ordinal) + "\"" : label;
    }
}
