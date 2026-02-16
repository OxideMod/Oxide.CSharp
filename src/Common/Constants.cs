using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Oxide.Core;

namespace Oxide.CSharp.Common
{
    internal static class Constants
    {
        internal static readonly Serializer Serializer = new();
        internal static readonly UTF8Encoding CompilerEncoding = new(false);

        internal const string CompilerDownloadUrl = "https://downloads.oxidemod.com/artifacts/Oxide.Compiler/{0}/";
        internal const string CompilerBasicArguments = "-unsafe true --setting:Force true -ms true";
        internal const string OxideNamespace = "namespace Oxide.Plugins";
        internal const string UmodNamespace = "namespace uMod.Plugins";

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

        internal static readonly Regex PluginReferenceRegex = new Regex(@"^(Oxide\.(?:Ext|Game)\.(.+))$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static readonly Regex IncludeRegex = new Regex(@"\\include\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static readonly Regex SymbolEscapeRegex = new Regex(@"[^\w\d]", RegexOptions.Compiled);

        internal static readonly string IncludePath = Path.Combine(Interface.Oxide.PluginDirectory, "include");
        internal static readonly string CSharpPath = Path.Combine(Interface.Oxide.ExtensionDirectory, "Oxide.CSharp.dll");
    }
}
