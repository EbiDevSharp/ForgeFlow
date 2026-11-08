using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForgeFlow.Web.Tests.Analysis;

/// <summary>
/// A narrow source check for static UI text in Razor, C# validation/display sinks,
/// and JavaScript DOM text/accessible-name sinks. This is not data-flow analysis.
/// </summary>
public static class HardcodedDisplayTextScanner
{
    private static readonly HashSet<string> DisplayAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "placeholder", "alt", "aria-label", "aria-description", "aria-roledescription",
        "aria-valuetext", "aria-placeholder", "label", "summary", "value"
    };

    private static readonly HashSet<string> DisplayProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "textContent", "innerText", "innerHTML", "outerHTML", "title", "placeholder", "ariaLabel", "value"
    };

    public sealed record Finding(string File, int Line, int Column, string Text, string Rule);
    public sealed record ExceptionEntry(string File, int Line, int Column, string Text, string Symbol, string Reason, string Reviewer);
    public sealed record ScanResult(IReadOnlyList<Finding> Findings, IReadOnlyList<ExceptionEntry> UnusedExceptions);

    public static IReadOnlyList<ExceptionEntry> ReadExceptions(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("exceptions").EnumerateArray().Select(item =>
        {
            var entry = new ExceptionEntry(
                item.GetProperty("file").GetString() ?? "",
                item.GetProperty("line").GetInt32(),
                item.GetProperty("column").GetInt32(),
                item.GetProperty("text").GetString() ?? "",
                item.GetProperty("symbol").GetString() ?? "",
                item.GetProperty("reason").GetString() ?? "",
                item.GetProperty("reviewer").GetString() ?? "");
            if (string.IsNullOrWhiteSpace(entry.File) || entry.Line < 1 || entry.Column < 1 || string.IsNullOrWhiteSpace(entry.Text) ||
                string.IsNullOrWhiteSpace(entry.Symbol) || string.IsNullOrWhiteSpace(entry.Reason) || string.IsNullOrWhiteSpace(entry.Reviewer))
                throw new InvalidDataException("Every display-text exception requires file, positive line and column, exact text, symbol, reason and reviewer.");
            return entry with { File = NormalizePath(entry.File) };
        }).ToArray();
    }

    public static ScanResult ScanDirectory(string sourceRoot, IReadOnlyList<ExceptionEntry>? exceptions = null)
    {
        exceptions ??= [];
        var all = new List<Finding>();
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                     .Where(path => Path.GetExtension(path) is ".razor" or ".cs" or ".mjs")
                     .Where(path => !HasGeneratedOrDependencySegment(Path.GetRelativePath(sourceRoot, path))))
        {
            var relative = NormalizePath(Path.GetRelativePath(sourceRoot, path));
            all.AddRange(ScanFile(relative, File.ReadAllText(path)));
        }

        var seen = new HashSet<(string File, int Line, int Column, string Text, string Rule)>();
        all = all.Where(finding => seen.Add((finding.File, finding.Line, finding.Column, finding.Text, finding.Rule)))
            .OrderBy(finding => finding.File, StringComparer.Ordinal)
            .ThenBy(finding => finding.Line)
            .ToList();
        return ApplyExceptions(all, exceptions);
    }

    public static ScanResult ApplyExceptions(IEnumerable<Finding> findings, IReadOnlyList<ExceptionEntry> exceptions)
    {
        var matched = new HashSet<ExceptionEntry>();
        var remaining = new List<Finding>();
        foreach (var finding in findings)
        {
            var exception = exceptions.FirstOrDefault(entry => entry.File == finding.File && entry.Line == finding.Line &&
                entry.Column == finding.Column && entry.Text == finding.Text && entry.Symbol == finding.Rule);
            if (exception is null) remaining.Add(finding);
            else matched.Add(exception);
        }
        return new ScanResult(remaining, exceptions.Where(entry => !matched.Contains(entry)).ToArray());
    }

    public static IReadOnlyList<Finding> ScanFile(string relativePath, string source)
    {
        relativePath = NormalizePath(relativePath);
        var findings = new List<Finding>();
        switch (Path.GetExtension(relativePath))
        {
            case ".razor": ScanRazor(relativePath, source, findings); break;
            case ".cs": ScanCSharp(relativePath, source, findings); break;
            case ".mjs": ScanJavaScript(relativePath, source, findings); break;
        }
        return findings;
    }

    private static void ScanRazor(string file, string source, List<Finding> findings)
    {
        var textStart = 0;
        var index = 0;
        while (index < source.Length)
        {
            if (source.AsSpan(index).StartsWith("<!--", StringComparison.Ordinal))
            {
                AddMarkupText(file, source, textStart, index, findings);
                var commentEnd = source.IndexOf("-->", index + 4, StringComparison.Ordinal);
                index = commentEnd < 0 ? source.Length : commentEnd + 3;
                textStart = index;
                continue;
            }
            if (source[index] == '<' && index + 1 < source.Length && (char.IsLetter(source[index + 1]) || source[index + 1] == '/'))
            {
                AddMarkupText(file, source, textStart, index, findings);
                var end = FindTagEnd(source, index);
                ScanTag(file, source, index, end, findings);
                index = end + 1;
                textStart = index;
                continue;
            }
            if (source[index] == '@')
            {
                AddMarkupText(file, source, textStart, index, findings);
                index = ConsumeRazorTransition(file, source, index, findings);
                textStart = index;
                continue;
            }
            index++;
        }
        AddMarkupText(file, source, textStart, source.Length, findings);
    }

    private static int ConsumeRazorTransition(string file, string source, int start, List<Finding> findings)
    {
        if (start + 1 >= source.Length) return start + 1;
        if (source[start + 1] == '*')
        {
            var close = source.IndexOf("*@", start + 2, StringComparison.Ordinal);
            return close < 0 ? source.Length : close + 2;
        }
        if (source[start + 1] == '@') return start + 2;

        var wordStart = start + 1;
        while (wordStart < source.Length && char.IsWhiteSpace(source[wordStart])) wordStart++;
        var wordEnd = wordStart;
        while (wordEnd < source.Length && (char.IsLetterOrDigit(source[wordEnd]) || source[wordEnd] == '_')) wordEnd++;
        var word = source[wordStart..wordEnd];

        if (word is "code" or "functions")
        {
            var brace = source.IndexOf('{', wordEnd);
            if (brace < 0) return source.Length;
            var close = FindBalanced(source, brace, '{', '}');
            if (close < 0) close = source.Length - 1;
            ScanCSharp(file, source[(brace + 1)..close], findings, source, brace + 1);
            return Math.Min(close + 1, source.Length);
        }

        if (word is "page" or "using" or "inject" or "implements" or "namespace" or "layout" or "attribute" or "typeparam" or "rendermode")
            return EndOfLine(source, wordEnd);

        if (word is "if" or "else" or "for" or "foreach" or "while" or "switch" or "lock" or "try" or "catch" or "finally")
        {
            var cursor = wordEnd;
            while (cursor < source.Length && char.IsWhiteSpace(source[cursor])) cursor++;
            if (cursor < source.Length && source[cursor] == '(')
            {
                var close = FindBalanced(source, cursor, '(', ')');
                return close < 0 ? source.Length : close + 1;
            }
            return cursor;
        }

        if (source[start + 1] == '(')
        {
            var close = FindBalanced(source, start + 1, '(', ')');
            if (close < 0) return source.Length;
            ScanInlineExpression(file, source[(start + 2)..close], start + 2, source, findings, "markup text");
            return close + 1;
        }

        var expressionEnd = ConsumeImplicitExpression(source, start + 1);
        if (expressionEnd <= start + 1) return start + 1;
        ScanInlineExpression(file, source[(start + 1)..expressionEnd], start + 1, source, findings, "markup text");
        return expressionEnd;
    }

    private static void ScanInlineExpression(string file, string expression, int offset, string original, List<Finding> findings, string sink)
    {
        var parsed = SyntaxFactory.ParseExpression(expression);
        var unwrapped = parsed is ParenthesizedExpressionSyntax parentheses ? parentheses.Expression : parsed;
        if (unwrapped is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            AddFinding(file, original, offset + literal.SpanStart, literal.Token.ValueText, "Razor " + sink, findings);
        else if (unwrapped is InterpolatedStringExpressionSyntax interpolated && interpolated.Contents.OfType<InterpolatedStringTextSyntax>().Any())
            AddFinding(file, original, offset + interpolated.SpanStart, interpolated.ToString(), "Razor " + sink, findings);
        ScanCSharp(file, expression, findings, original, offset);
    }

    private static int ConsumeImplicitExpression(string source, int start)
    {
        var index = start;
        var parens = 0;
        var brackets = 0;
        var inString = '\0';
        var escaped = false;
        while (index < source.Length)
        {
            var c = source[index];
            if (inString != '\0')
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == inString) inString = '\0';
                index++;
                continue;
            }
            if (c is '"' or '\'') { inString = c; index++; continue; }
            if (c == '(') parens++;
            else if (c == ')') { if (parens == 0) break; parens--; }
            else if (c == '[') brackets++;
            else if (c == ']') { if (brackets == 0) break; brackets--; }
            if (parens == 0 && brackets == 0 && (char.IsWhiteSpace(c) || c is '<' or '>' or ';' or '{' or '}')) break;
            index++;
        }
        return index;
    }

    private static void AddMarkupText(string file, string source, int start, int end, List<Finding> findings)
    {
        var segmentStart = start;
        for (var index = start; index <= end; index++)
        {
            if (index != end && source[index] != '@') continue;
            var value = source[segmentStart..index].Trim();
            if (LooksLikeDisplayText(value)) AddFinding(file, source, segmentStart, value, "Razor markup text", findings);
            segmentStart = index + 1;
        }
    }

    private static int FindTagEnd(string source, int start)
    {
        var quote = '\0';
        for (var index = start + 1; index < source.Length; index++)
        {
            if (quote != '\0') { if (source[index] == quote) quote = '\0'; continue; }
            if (source[index] is '"' or '\'') quote = source[index];
            else if (source[index] == '>') return index;
        }
        return source.Length - 1;
    }

    private static void ScanTag(string file, string source, int start, int end, List<Finding> findings)
    {
        var index = start + 1;
        if (index < end && source[index] == '/') index++;
        while (index < end && !char.IsWhiteSpace(source[index]) && source[index] is not '/' and not '>') index++;
        while (index < end)
        {
            while (index < end && (char.IsWhiteSpace(source[index]) || source[index] == '/')) index++;
            var nameStart = index;
            while (index < end && !char.IsWhiteSpace(source[index]) && source[index] is not '=' and not '/' and not '>') index++;
            if (index == nameStart) { index++; continue; }
            var name = source[nameStart..index];
            while (index < end && char.IsWhiteSpace(source[index])) index++;
            if (index >= end || source[index] != '=') continue;
            index++;
            while (index < end && char.IsWhiteSpace(source[index])) index++;
            if (index >= end) break;
            var valueStart = index;
            var valueEnd = index;
            if (source[index] is '"' or '\'')
            {
                var quote = source[index++];
                valueStart = index;
                while (index < end && source[index] != quote) index++;
                valueEnd = index;
                if (index < end) index++;
            }
            else
            {
                while (index < end && !char.IsWhiteSpace(source[index]) && source[index] != '>') index++;
                valueEnd = index;
            }
            if (!DisplayAttributes.Contains(name)) continue;
            var value = source[valueStart..valueEnd];
            if (value.StartsWith('@'))
            {
                var expression = value.Length > 1 && value[1] == '(' ? value[2..^1] : value[1..];
                ScanInlineExpression(file, expression, valueStart + (value.Length > 1 && value[1] == '(' ? 2 : 1), source, findings, $"attribute {name}");
            }
            else if (LooksLikeDisplayText(value)) AddFinding(file, source, valueStart, value, $"Razor attribute {name}", findings);
        }
    }

    private static void ScanCSharp(string file, string source, List<Finding> findings, string? original = null, int offset = 0)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var baseSource = original ?? source;
        foreach (var conditional in root.DescendantNodes().OfType<ConditionalExpressionSyntax>().Where(node => IsLanguageSelector(node.Condition)))
        {
            AddSelectedLiterals(file, baseSource, offset, conditional.WhenTrue, findings, "C# language-dependent selection");
            AddSelectedLiterals(file, baseSource, offset, conditional.WhenFalse, findings, "C# language-dependent selection");
        }
        foreach (var conditional in root.DescendantNodes().OfType<IfStatementSyntax>().Where(node => IsLanguageSelector(node.Condition)))
        {
            AddSelectedLiterals(file, baseSource, offset, conditional.Statement, findings, "C# language-dependent selection");
            if (conditional.Else is not null) AddSelectedLiterals(file, baseSource, offset, conditional.Else.Statement, findings, "C# language-dependent selection");
        }
        foreach (var selection in root.DescendantNodes().OfType<SwitchExpressionSyntax>().Where(node => IsLanguageSelector(node.GoverningExpression)))
            foreach (var arm in selection.Arms) AddSelectedLiterals(file, baseSource, offset, arm.Expression, findings, "C# language-dependent selection");

        foreach (var argument in root.DescendantNodes().OfType<AttributeArgumentSyntax>())
        {
            var name = argument.NameEquals?.Name.Identifier.ValueText;
            if (name is "ErrorMessage" or "Prompt" or "DisplayName" or "Name" or "Description" or "Placeholder" or "Title")
                AddSelectedLiterals(file, baseSource, offset, argument.Expression, findings, "C# display/validation attribute");
        }
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var name = assignment.Left switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                _ => ""
            };
            if (name is "ErrorMessage" or "Prompt" or "DisplayName" or "Placeholder" or "Title" or "Label")
                AddSelectedLiterals(file, baseSource, offset, assignment.Right, findings, "C# display/validation assignment");
        }
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var method = invocation.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                _ => ""
            };
            var enclosingName = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "";
            var isValidationSink = method.Contains("Validation", StringComparison.OrdinalIgnoreCase) ||
                                   method.Contains("ErrorMessage", StringComparison.OrdinalIgnoreCase) ||
                                   method is "AddModelError" or "AddError" ||
                                   method == "Add" && (enclosingName.Contains("Validate", StringComparison.OrdinalIgnoreCase) ||
                                                         invocation.Expression.ToString().Contains("message", StringComparison.OrdinalIgnoreCase));
            if (isValidationSink)
                foreach (var argument in invocation.ArgumentList.Arguments.Skip(1))
                    AddSelectedLiterals(file, baseSource, offset, argument.Expression, findings, "C# validation message");
        }
        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                     .Where(node => node.Type.ToString() is "ValidationException" or "ValidationResult" or "ArgumentException"))
            if (creation.ArgumentList is not null)
                foreach (var argument in creation.ArgumentList.Arguments)
                    AddSelectedLiterals(file, baseSource, offset, argument.Expression, findings, "C# validation exception");
    }

    private static void AddSelectedLiterals(string file, string original, int offset, SyntaxNode node, List<Finding> findings, string rule)
    {
        foreach (var literal in node.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>().Where(item => item.IsKind(SyntaxKind.StringLiteralExpression)))
        {
            var value = literal.Token.ValueText;
            if (IsResourceKey(literal) || !LooksLikeDisplayText(value) ||
                rule.Contains("language-dependent", StringComparison.Ordinal) && IsTechnicalIdentifierString(value)) continue;
            AddFinding(file, original, offset + literal.SpanStart, value, rule, findings);
        }
    }

    private static bool IsResourceKey(LiteralExpressionSyntax literal)
        => literal.AncestorsAndSelf().OfType<ElementAccessExpressionSyntax>().Any(access =>
            access.ArgumentList.Arguments.Any(argument => argument.Span.Contains(literal.Span)) &&
            (access.Expression.ToString().Contains("Text", StringComparison.OrdinalIgnoreCase) ||
             access.Expression.ToString().Contains("localizer", StringComparison.OrdinalIgnoreCase) ||
             access.Expression.ToString().Contains("resources", StringComparison.OrdinalIgnoreCase)));

    private static bool IsLanguageSelector(SyntaxNode condition)
        => condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(identifier =>
        {
            var name = identifier.Identifier.ValueText;
            return name.Contains("culture", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("language", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("locale", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("persian", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("english", StringComparison.OrdinalIgnoreCase);
        });

    private static void ScanJavaScript(string file, string source, List<Finding> findings)
    {
        var tokens = TokenizeJavaScript(source);
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Kind != "identifier") continue;
            if (tokens[index].Value == "setAttribute" && index > 0 && tokens[index - 1].Value == ".")
            {
                var open = index + 1;
                if (open >= tokens.Count || tokens[open].Value != "(") continue;
                var first = NextSignificant(tokens, open + 1);
                if (first < tokens.Count && tokens[first].Kind == "string" && DisplayAttributes.Contains(tokens[first].Value))
                {
                    var comma = NextSignificant(tokens, first + 1);
                    if (comma < tokens.Count && tokens[comma].Value == ",")
                    {
                        var valueStart = NextSignificant(tokens, comma + 1);
                        var valueEnd = FindCallArgumentEnd(tokens, valueStart);
                        AddJavaScriptText(file, source, tokens, valueStart, valueEnd, findings, "JavaScript setAttribute " + tokens[first].Value);
                    }
                }
            }
            if (DisplayProperties.Contains(tokens[index].Value) && index > 0 && tokens[index - 1].Value == ".")
            {
                var equals = NextSignificant(tokens, index + 1);
                if (equals < tokens.Count && tokens[equals].Value is "=" or "+=")
                {
                    var valueStart = NextSignificant(tokens, equals + 1);
                    var valueEnd = FindExpressionEnd(tokens, valueStart);
                    AddJavaScriptText(file, source, tokens, valueStart, valueEnd, findings, "JavaScript DOM assignment " + tokens[index].Value);
                }
            }
            if (tokens[index].Value is "insertAdjacentText" or "insertAdjacentHTML" && index > 0 && tokens[index - 1].Value == ".")
            {
                var open = index + 1;
                var firstComma = FindTopLevelToken(tokens, open + 1, ",");
                if (firstComma >= 0)
                {
                    var valueStart = NextSignificant(tokens, firstComma + 1);
                    var valueEnd = FindCallArgumentEnd(tokens, valueStart);
                    AddJavaScriptText(file, source, tokens, valueStart, valueEnd, findings, "JavaScript DOM insertion");
                }
            }
        }
    }

    private static void AddJavaScriptText(string file, string source, List<JsToken> tokens, int start, int end, List<Finding> findings, string rule)
    {
        if (start >= end || start >= tokens.Count) return;
        var valueTokens = tokens.Skip(start).Take(end - start).Select((token, relativeIndex) => (token, tokenIndex: start + relativeIndex))
            .Where(item => item.token.Kind is "string" or "template")
            .Where(item => !IsJavaScriptResourceKey(tokens, item.tokenIndex))
            .Select(item => item.token).ToArray();
        foreach (var token in valueTokens)
        {
            if (!LooksLikeDisplayText(token.Value)) continue;
            AddFinding(file, source, token.Start, token.Value, rule, findings);
        }
        var expression = string.Join(" ", tokens.Skip(start).Take(end - start).Select(token => token.Value));
        if (expression.Contains('?') && IsLanguageSelectorText(expression))
        {
            foreach (var token in valueTokens.Where(token => LooksLikeDisplayText(token.Value) && !IsTechnicalIdentifierString(token.Value)))
                AddFinding(file, source, token.Start, token.Value, "JavaScript language-dependent selection", findings);
        }
    }

    private sealed record JsToken(string Kind, string Value, int Start, int End);

    private static bool IsJavaScriptResourceKey(List<JsToken> tokens, int index)
    {
        if (index >= 2 && tokens[index].Kind == "string" && tokens[index - 1].Value == "[" &&
            tokens[index - 2].Kind == "identifier" &&
            tokens[index - 2].Value is "Text" or "text" or "messages" or "resources" or "localizer" or "strings") return true;
        return index >= 4 && tokens[index].Kind == "string" && tokens[index - 1].Value == "(" &&
               tokens[index - 2].Kind == "identifier" && tokens[index - 2].Value is "getString" or "get" or "t" &&
               tokens[index - 3].Value == "." && tokens[index - 4].Kind == "identifier" &&
               tokens[index - 4].Value is "Text" or "text" or "messages" or "resources" or "localizer" or "strings";
    }

    private static List<JsToken> TokenizeJavaScript(string source)
    {
        var tokens = new List<JsToken>();
        var index = 0;
        while (index < source.Length)
        {
            var start = index;
            var c = source[index];
            if (c == '\n') { tokens.Add(new JsToken("newline", "\n", index, ++index)); continue; }
            if (char.IsWhiteSpace(c)) { index++; continue; }
            if (c == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                index += 2; while (index < source.Length && source[index] != '\n') index++; continue;
            }
            if (c == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                var close = source.IndexOf("*/", index + 2, StringComparison.Ordinal); index = close < 0 ? source.Length : close + 2; continue;
            }
            if (c is '\'' or '"' or '`')
            {
                var quote = c; index++;
                var valueStart = index;
                var escaped = false;
                while (index < source.Length)
                {
                    if (escaped) { escaped = false; index++; continue; }
                    if (source[index] == '\\') { escaped = true; index++; continue; }
                    if (source[index] == quote) break;
                    index++;
                }
                var value = source[valueStart..index];
                if (index < source.Length) index++;
                tokens.Add(new JsToken(quote == '`' ? "template" : "string", value, start, index));
                continue;
            }
            if (char.IsLetter(c) || c is '_' or '$')
            {
                index++; while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '_' or '$')) index++;
                tokens.Add(new JsToken("identifier", source[start..index], start, index)); continue;
            }
            if (char.IsDigit(c))
            {
                index++; while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] == '.')) index++;
                tokens.Add(new JsToken("number", source[start..index], start, index)); continue;
            }
            var op = source.Substring(index, Math.Min(3, source.Length - index));
            var length = op is "===" or "!==" or "==>" ? 3 : source.Substring(index, Math.Min(2, source.Length - index)) is "==" or "!=" or "=>" or "?." or "&&" or "||" or "??" or "+=" ? 2 : 1;
            tokens.Add(new JsToken("punct", source.Substring(index, length), index, index + length)); index += length;
        }
        return tokens;
    }

    private static bool IsLanguageSelectorText(string text)
        => text.Contains("culture", StringComparison.OrdinalIgnoreCase) || text.Contains("language", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("locale", StringComparison.OrdinalIgnoreCase) || text.Contains("persian", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("english", StringComparison.OrdinalIgnoreCase);

    private static int FindExpressionEnd(List<JsToken> tokens, int start)
    {
        var paren = 0; var bracket = 0; var brace = 0; var index = start;
        for (; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Kind == "newline" && paren == 0 && bracket == 0 && brace == 0 || token.Value == ";" && paren == 0 && bracket == 0 && brace == 0) break;
            switch (token.Value)
            {
                case "(": paren++; break; case ")": if (paren == 0) return index; paren--; break;
                case "[": bracket++; break; case "]": if (bracket == 0) return index; bracket--; break;
                case "{": brace++; break; case "}": if (brace == 0) return index; brace--; break;
            }
        }
        return index;
    }

    private static int FindCallArgumentEnd(List<JsToken> tokens, int start)
    {
        var paren = 0; var bracket = 0; var brace = 0;
        for (var index = start; index < tokens.Count; index++)
        {
            switch (tokens[index].Value)
            {
                case "(": paren++; break; case ")": if (paren == 0) return index; paren--; break;
                case "[": bracket++; break; case "]": bracket--; break;
                case "{": brace++; break; case "}": brace--; break;
                case "," when paren == 0 && bracket == 0 && brace == 0: return index;
            }
        }
        return tokens.Count;
    }

    private static int FindTopLevelToken(List<JsToken> tokens, int start, string expected)
    {
        var depth = 0;
        for (var index = start; index < tokens.Count; index++)
        {
            if (tokens[index].Value == expected && depth == 1) return index;
            if (tokens[index].Value is "(" or "[" or "{") depth++;
            else if (tokens[index].Value is ")" or "]" or "}") { if (depth == 1) break; depth--; }
        }
        return -1;
    }

    private static int NextSignificant(List<JsToken> tokens, int index)
    {
        while (index < tokens.Count && tokens[index].Kind == "newline") index++;
        return index;
    }

    private static int FindBalanced(string source, int start, char open, char close)
    {
        var depth = 0; var quote = '\0'; var escaped = false; var lineComment = false; var blockComment = false;
        for (var index = start; index < source.Length; index++)
        {
            var c = source[index]; var next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (lineComment) { if (c == '\n') lineComment = false; continue; }
            if (blockComment) { if (c == '*' && next == '/') { blockComment = false; index++; } continue; }
            if (quote != '\0')
            {
                if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == quote) quote = '\0';
                continue;
            }
            if (c == '/' && next == '/') { lineComment = true; index++; continue; }
            if (c == '/' && next == '*') { blockComment = true; index++; continue; }
            if (c is '"' or '\'' or '`') { quote = c; continue; }
            if (c == open) depth++;
            else if (c == close && --depth == 0) return index;
        }
        return -1;
    }

    private static int EndOfLine(string source, int start)
    {
        var end = source.IndexOf('\n', start);
        return end < 0 ? source.Length : end + 1;
    }

    private static bool LooksLikeDisplayText(string text)
        => !string.IsNullOrWhiteSpace(text) && text.Any(char.IsLetter);

    private static bool IsTechnicalIdentifierString(string text)
    {
        return text is "rtl" or "ltr" or "fa-IR" or "en-US" or "CultureSwitchRejected" ||
               text.StartsWith("/", StringComparison.Ordinal) || text.StartsWith('#');
    }

    private static void AddFinding(string file, string source, int offset, string text, string rule, List<Finding> findings)
    {
        text = text.Trim();
        if (!LooksLikeDisplayText(text)) return;
        var safeOffset = Math.Clamp(offset, 0, source.Length);
        var prefix = source[..safeOffset];
        var line = 1 + prefix.Count(character => character == '\n');
        var lastLineBreak = prefix.LastIndexOf('\n');
        var column = safeOffset - lastLineBreak;
        findings.Add(new Finding(NormalizePath(file), line, column, text, rule));
    }

    private static bool HasGeneratedOrDependencySegment(string relativePath)
        => NormalizePath(relativePath).Split('/').Any(segment => segment is "obj" or "bin" or "node_modules" or ".git" or ".tools" or
            "packages" or "vendor" or "third_party" or "artifacts" or ".nuget");

    private static string NormalizePath(string path) => path.Replace('\\', '/');

}
