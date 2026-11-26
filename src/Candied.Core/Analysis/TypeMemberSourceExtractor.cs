using System;
using System.Collections.Generic;
using System.Text;

namespace Candied.Core.Analysis;

/// <summary>
/// Pure helper that extracts member blocks matching a case-insensitive substring filter
/// from a type's raw source code without requiring external parsers or disk I/O.
/// </summary>
public static class TypeMemberSourceExtractor
{
    private static readonly string[] LineSeparators = ["\r\n", "\r", "\n"];

    public static string ExtractMatchingMembers(string rawSourceCode, string memberFilter)
    {
        if (string.IsNullOrWhiteSpace(rawSourceCode) || string.IsNullOrWhiteSpace(memberFilter))
        {
            return rawSourceCode;
        }

        var lines = rawSourceCode.Split(LineSeparators, StringSplitOptions.None);
        var matchedBlocks = new List<string>();
        int typeBraceDepth = -1;
        int currentDepth = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int depthBefore = currentDepth;
            int netDelta = CountBraceDelta(line);
            currentDepth += netDelta;

            if (typeBraceDepth < 0)
            {
                if (currentDepth > 0)
                {
                    typeBraceDepth = currentDepth;
                }

                continue;
            }

            if (depthBefore == typeBraceDepth &&
                line.Contains(memberFilter, StringComparison.OrdinalIgnoreCase))
            {
                int startIdx = i;
                while (startIdx > 0 && lines[startIdx - 1].TrimStart().StartsWith('['))
                {
                    startIdx--;
                }

                string trimmed = line.TrimEnd();
                if (currentDepth == typeBraceDepth && trimmed.EndsWith(';'))
                {
                    matchedBlocks.Add(JoinLines(lines, startIdx, i));
                    continue;
                }

                int endIdx = i;
                bool enteredBlock = currentDepth > typeBraceDepth;

                while (endIdx + 1 < lines.Length)
                {
                    if (enteredBlock && currentDepth <= typeBraceDepth)
                    {
                        break;
                    }

                    endIdx++;
                    int delta = CountBraceDelta(lines[endIdx]);
                    currentDepth += delta;
                    if (currentDepth > typeBraceDepth)
                    {
                        enteredBlock = true;
                    }

                    if (!enteredBlock && lines[endIdx].TrimEnd().EndsWith(';'))
                    {
                        break;
                    }
                }

                matchedBlocks.Add(JoinLines(lines, startIdx, endIdx));
                i = endIdx;
            }
        }

        if (matchedBlocks.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine + Environment.NewLine, matchedBlocks);
    }

    private static string JoinLines(string[] lines, int startIdx, int endIdx)
    {
        var sb = new StringBuilder();
        for (int idx = startIdx; idx <= endIdx; idx++)
        {
            if (idx > startIdx)
            {
                sb.AppendLine();
            }

            sb.Append(lines[idx]);
        }

        return sb.ToString();
    }

    private static int CountBraceDelta(string line)
    {
        int delta = 0;
        bool inString = false;
        bool inChar = false;
        bool escaped = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && (inString || inChar))
            {
                escaped = true;
                continue;
            }

            if (!inString && !inChar && c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                break;
            }

            if (!inChar && c == '"')
            {
                inString = !inString;
                continue;
            }

            if (!inString && c == '\'')
            {
                inChar = !inChar;
                continue;
            }

            if (!inString && !inChar)
            {
                if (c == '{')
                {
                    delta++;
                }
                else if (c == '}')
                {
                    delta--;
                }
            }
        }

        return delta;
    }
}
