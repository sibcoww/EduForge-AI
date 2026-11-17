using System.Text.RegularExpressions;
namespace EduForge.Application;
public static partial class AiText
{
    // Keep fenced/inline code and Python ** operators intact.
    public static string Plain(string text) => string.Concat(Code().Split(text).Select((part,i) => i % 2 == 1 ? part : Bold().Replace(part,"$1")));
    [GeneratedRegex("(```[\\s\\S]*?```|`[^`\\r\\n]*`)")]
    private static partial Regex Code();
    [GeneratedRegex(@"(?<![\w*])\*\*(\S(?:[^\r\n]*?\S)?)\*\*(?![\w*])")]
    private static partial Regex Bold();
}
