using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Oxide.CSharp
{
    public abstract class CompilerComment
    {
        private static readonly Regex codeReader = new Regex(@"^\[(?'Code'CS\d+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static Dictionary<string, CompilerComment> comments = new Dictionary<string, CompilerComment>(StringComparer.OrdinalIgnoreCase);
        public static void Add(string code, CompilerComment comment) => comments[code] = comment;

        public abstract void ProcessMessage(string message);

        public static void Process(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Match match = codeReader.Match(message);

            if (!match.Success)
            {
                return;
            }

            string code = match.Groups["Code"].Captures[0].Value;

            if (comments.TryGetValue(code, out CompilerComment comment))
            {
                comment.ProcessMessage(codeReader.Replace(message, string.Empty).TrimStart());
            }
        }
    }
}
