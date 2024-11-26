using System.Text.RegularExpressions;
using Oxide.CSharp.CompilerStream;
using Oxide.CSharp.Interfaces;

namespace Oxide.CSharp.Common
{
    internal static class Constants
    {
        //TODO: Move to Oxide.Common & optimize this is a temporary solution
        internal static readonly ISerializer Serializer = new Serializer();

        internal const string CompilerDownloadUrl = "https://downloads.oxidemod.com/artifacts/Oxide.Compiler/{0}/";
        internal const string CompilerBasicArguments = "-unsafe true --setting:Force true -ms true";

        internal static readonly Regex FileErrorRegex = new Regex(@"^\[(?'Severity'\S+)\]\[(?'Code'\S+)\]\[(?'File'\S+)\] (?'Message'.+)$",
            RegexOptions.Compiled);

        internal static readonly Regex BlankLineRegex = new Regex(@"^\s*\{?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static readonly Regex CustomAttributeRegex =
            new Regex(@"^\s*\[", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static readonly Regex MainPluginClassNameRegex =
            new Regex(@"^\s*(?:public|private|protected|internal)?\s*class\s+(\S+)\s+\:\s+\S+Plugin\s*$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static readonly Regex RequiresTextRegex = new Regex(@"^//\s*Requires:\s*(\S+?)(\.cs)?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static readonly Regex ReferenceTextRegex = new Regex(@"^//\s*Reference:\s*(\S+)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static readonly Regex ImplicitReferenceTextRegex =
            new Regex(@"^\s*using\s+(Oxide\.(?:Core|Ext|Game)\.(?:[^\.]+))[^;]*;.*$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static readonly Regex PluginNameRegex = new Regex(@"Oxide\\.[\\w]+\\.([\\w]+)",
            RegexOptions.Compiled);

        internal static readonly Regex NamespaceRegex = new Regex(@"^\s*namespace Oxide\.Plugins\s*(\{\s*)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static readonly Regex PluginReferenceRegex = new Regex(@"^(Oxide\.(?:Ext|Game)\.(.+))$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static readonly Regex IncludeRegex = new Regex(@"\\include\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static readonly Regex SymbolEscapeRegex = new Regex(@"[^\w\d]", RegexOptions.Compiled);
    }
}
