using Oxide.Core;
using System.Text.RegularExpressions;

namespace Oxide.CSharp.Comments
{
    internal class CS0012 : CompilerComment
    {
        private readonly Regex typeReg = new Regex(@"^The type '(?'Type'\S+)'", RegexOptions.Compiled);
        private readonly Regex RefReg = new Regex(@"reference to assembly '(?'Assembly'\S+),", RegexOptions.Compiled);
        private readonly Regex FileReg = new Regex(@"SourceFile\((?'File'\S+.cs)\[", RegexOptions.Compiled);
        public override void ProcessMessage(string message)
        {
            Match type = typeReg.Match(message);
            Match r = RefReg.Match(message);
            Match file = FileReg.Match(message);

            if (!type.Success || !r.Success || !file.Success)
                return;

            string t = type.Groups["Type"].Value;
            string reff = r.Groups["Assembly"].Value;
            string fi = file.Groups["File"].Value;

            Interface.Oxide.LogError($"Plugin {fi} is using Type '{t}' from {reff}, but is missing a // Reference: {reff}");
        }
    }
}
