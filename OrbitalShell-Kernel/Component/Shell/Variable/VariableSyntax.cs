﻿using System;
using System.Linq;
using OrbitalShell.Component.CommandLine.Parsing;
using static OrbitalShell.Component.CommandLine.Parsing.CommandLineSyntax;

namespace OrbitalShell.Component.Shell.Variable
{
    public static class VariableSyntax
    {
        public static int FindEndOfVariableName(char[] text, int beginPos)
        {
            int i = beginPos;
            while (i < text.Length)
            {
                if (!IsVariableNameValidCharacter(text[i]))
                {
                    break;
                }
                i++;
            }
            return i - 1;
        }

        public static bool IsVariableNameValidCharacter(char c)
        {
            // exclude non printable caracters & flow control caracters
            return c > 31
            // exclude top level separators
            && !TopLevelSeparators.Contains(c)
            // exclude variable delimiter
            && c != VariablePrefix
            // exclude common operators
            && !CommonOperators.Contains(c)
            ;
        }

        public static string[] SplitPath(string path)
        {
            return path?.Split(VariableNamePathSeparator);
        }

        public static string GetVariableName(string path)
        {
            if (path == null) return null;
            var p = SplitPath(path);
            if (p.Length == 0) return null;
            return p.Last();
        }

        public static string GetVariableName(ArraySegment<string> path)
        {
            if (path == null) return null;
            if (path.Count == 0) return null;
            return path.Last();
        }

        public static bool HasValidRootNamespace(string name)
        {
            var ns = Enum.GetNames(typeof(VariableNamespace)).Select(x => x + CommandLineSyntax.VariableNamePathSeparator);
            foreach (var n in ns)
                if (name.StartsWith(n)) return true;
            return false;
        }
    }
}
