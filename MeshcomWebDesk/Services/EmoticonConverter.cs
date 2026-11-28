using System.Text.RegularExpressions;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Turns text emoticons such as <c>;-)</c> into emoji (optional, Settings → Chat &amp; Monitor).
/// Only whole, whitespace-delimited words are converted, so "http://x", "8)" in a list or ":Dank" stay untouched.
/// The same table is handed to chat.js for the live conversion while typing (see <c>setEmoticons</c>);
/// <see cref="Convert"/> runs again when the message is sent, so the last word is converted too.
/// </summary>
public static class EmoticonConverter
{
    /// <summary>Emoticon → emoji. Case-sensitive: ":P" and ":p" are listed separately where both are common.</summary>
    public static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [":)"]  = "🙂", [":-)"]  = "🙂",
        [";)"]  = "😉", [";-)"]  = "😉",
        [":D"]  = "😀", [":-D"]  = "😀",
        ["xD"]  = "😆", ["XD"]   = "😆",
        [":("]  = "🙁", [":-("]  = "🙁",
        [":'("] = "😢", [":,("]  = "😢",
        [":P"]  = "😛", [":-P"]  = "😛", [":p"] = "😛", [":-p"] = "😛",
        [";P"]  = "😜", [";-P"]  = "😜",
        [":O"]  = "😮", [":-O"]  = "😮", [":o"] = "😮", [":-o"] = "😮",
        [":*"]  = "😘", [":-*"]  = "😘",
        [":/"]  = "😕", [":-/"]  = "😕",
        [":|"]  = "😐", [":-|"]  = "😐",
        ["B)"]  = "😎", ["B-)"]  = "😎",
        [">:("] = "😠", [">:-("] = "😠",
        ["<3"]  = "❤️",
        ["</3"] = "💔",
    };

    private static readonly Regex Token = new(@"(?<![^\s])\S+(?![^\s])", RegexOptions.Compiled);

    /// <summary>Replaces every whole-word emoticon in <paramref name="text"/>.</summary>
    public static string Convert(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Token.Replace(text, m => Map.TryGetValue(m.Value, out var emoji) ? emoji : m.Value);
    }
}
