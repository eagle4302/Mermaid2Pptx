using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Mermaid2Pptx;

public sealed partial class SvgStyleResolver
{
    private static readonly HashSet<string> PresentationAttributes =
    [
        "fill",
        "stroke",
        "stroke-width",
        "stroke-dasharray",
        "opacity",
        "fill-opacity",
        "stroke-opacity",
        "color",
        "background",
        "background-color",
        "font-family",
        "font-size",
        "font-weight",
        "font-style",
        "text-anchor",
        "text-align",
        "display",
        "visibility",
        "marker-start",
        "marker-end"
    ];

    public IReadOnlyList<CssRule> ParseStyleRules(XElement svgRoot)
    {
        var rules = new List<CssRule>();
        var order = 0;
        foreach (var styleElement in svgRoot.Descendants().Where(e => e.Name.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase)))
        {
            var css = CssCommentRegex().Replace(styleElement.Value, string.Empty);
            foreach (Match match in CssRuleRegex().Matches(css))
            {
                var selectorText = match.Groups["selector"].Value;
                var declarations = ParseDeclarations(match.Groups["body"].Value);
                foreach (var selector in selectorText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (declarations.Count > 0)
                    {
                        rules.Add(new CssRule(selector, declarations, Specificity(selector), order++));
                    }
                }
            }
        }

        return rules;
    }

    public SvgStyle Resolve(
        XElement element,
        SvgStyle inherited,
        IReadOnlyList<SvgNodeInfo> ancestors,
        IReadOnlyList<CssRule> rules)
    {
        var current = SvgNodeInfo.From(element);
        var style = inherited.Merge(ParsePresentationAttributes(element));

        foreach (var rule in rules
                     .Where(rule => SelectorMatches(rule.Selector, current, ancestors))
                     .OrderBy(rule => rule.Specificity)
                     .ThenBy(rule => rule.Order))
        {
            style = style.Merge(rule.Declarations);
        }

        style = style.Merge(ParseInlineStyle(element.Attribute("style")?.Value));
        return style;
    }

    public static IReadOnlyDictionary<string, string> ParseInlineStyle(string? style)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return ParseDeclarations(style);
    }

    private static IReadOnlyDictionary<string, string> ParsePresentationAttributes(XElement element)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in element.Attributes())
        {
            var name = attribute.Name.LocalName;
            if (PresentationAttributes.Contains(name))
            {
                result[name] = attribute.Value;
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> ParseDeclarations(string declarations)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in declarations.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = declaration.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = declaration[..colon].Trim();
            var value = StripImportant(declaration[(colon + 1)..].Trim());
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static string StripImportant(string value) =>
        value.EndsWith("!important", StringComparison.OrdinalIgnoreCase)
            ? value[..^10].Trim()
            : value;

    private static bool SelectorMatches(string selector, SvgNodeInfo current, IReadOnlyList<SvgNodeInfo> ancestors)
    {
        var tokens = selector
            .Replace(">", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !token.StartsWith(":", StringComparison.Ordinal))
            .ToArray();

        if (tokens.Length == 0 || !TokenMatches(tokens[^1], current))
        {
            return false;
        }

        var ancestorIndex = ancestors.Count - 1;
        for (var tokenIndex = tokens.Length - 2; tokenIndex >= 0; tokenIndex--)
        {
            var found = false;
            while (ancestorIndex >= 0)
            {
                if (TokenMatches(tokens[tokenIndex], ancestors[ancestorIndex]))
                {
                    found = true;
                    ancestorIndex--;
                    break;
                }

                ancestorIndex--;
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TokenMatches(string token, SvgNodeInfo node)
    {
        token = token.Trim();
        if (token == "*")
        {
            return true;
        }

        var idMatches = IdRegex().Matches(token).Select(m => m.Groups["id"].Value).ToArray();
        if (idMatches.Length > 0 && idMatches.Any(id => !string.Equals(id, node.Id, StringComparison.Ordinal)))
        {
            return false;
        }

        var classMatches = ClassRegex().Matches(token).Select(m => m.Groups["class"].Value).ToArray();
        if (classMatches.Any(className => !node.Classes.Contains(className)))
        {
            return false;
        }

        var tag = TokenTagRegex().Match(token).Groups["tag"].Value;
        return string.IsNullOrWhiteSpace(tag) ||
               string.Equals(tag, node.TagName, StringComparison.OrdinalIgnoreCase);
    }

    private static int Specificity(string selector)
    {
        var ids = IdRegex().Matches(selector).Count;
        var classes = ClassRegex().Matches(selector).Count;
        var tags = selector
            .Replace(">", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(token => TokenTagRegex().Match(token).Success);

        return ids * 100 + classes * 10 + tags;
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex CssCommentRegex();

    [GeneratedRegex(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex CssRuleRegex();

    [GeneratedRegex(@"#(?<id>[A-Za-z_][\w\-:.]*)", RegexOptions.Compiled)]
    private static partial Regex IdRegex();

    [GeneratedRegex(@"\.(?<class>[A-Za-z_][\w\-:]*)", RegexOptions.Compiled)]
    private static partial Regex ClassRegex();

    [GeneratedRegex(@"^(?<tag>[A-Za-z][\w\-]*)", RegexOptions.Compiled)]
    private static partial Regex TokenTagRegex();
}

public sealed record CssRule(
    string Selector,
    IReadOnlyDictionary<string, string> Declarations,
    int Specificity,
    int Order);

public sealed record SvgNodeInfo(string TagName, string? Id, IReadOnlySet<string> Classes)
{
    public static SvgNodeInfo From(XElement element)
    {
        var classes = (element.Attribute("class")?.Value ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        return new SvgNodeInfo(
            element.Name.LocalName,
            element.Attribute("id")?.Value,
            classes);
    }
}
