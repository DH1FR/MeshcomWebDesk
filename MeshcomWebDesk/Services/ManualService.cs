using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MeshcomWebDesk.Services;

/// <summary>One chapter of the manual (file name without extension, e.g. <c>01-quickstart</c>, and its title).</summary>
public sealed record ManualChapter(string Slug, string Title);

/// <summary>
/// The user manual. The Markdown source lives in <c>docs/manual/&lt;lang&gt;/*.md</c> (readable on GitHub as well) and is
/// embedded into the assembly, so it is available offline and in every package. Chapters are the files of the
/// language folder in name order; the title is the first <c>#</c> heading. Languages: <c>de</c>, everything else
/// falls back to <c>en</c>.
/// <para>Links between chapters (<c>other.md</c>) and images (<c>../img/x.png</c>) are rewritten to the in-app
/// routes <c>/help/other</c> and <c>/help-img/x.png</c>.</para>
/// </summary>
public sealed class ManualService
{
    private const string Prefix = "manual/";

    private static readonly Regex SafeImageName = new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    // No raw HTML from the Markdown source: only what Markdown itself can express.
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    private readonly Assembly _asm = typeof(ManualService).Assembly;
    private readonly ConcurrentDictionary<string, string> _html = new();
    private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<ManualChapter>>> _chapters;

    public ManualService()
    {
        _chapters = new Lazy<IReadOnlyDictionary<string, IReadOnlyList<ManualChapter>>>(LoadChapters);
    }

    /// <summary>Manual language for a UI language: <c>de</c> or <c>en</c>.</summary>
    public static string ManualLanguage(string? uiLang) => uiLang == "de" ? "de" : "en";

    /// <summary>True when the manual exists in the UI language itself (otherwise English is shown).</summary>
    public static bool IsNative(string? uiLang) => uiLang is "de" or "en";

    public IReadOnlyList<ManualChapter> GetChapters(string? uiLang) =>
        _chapters.Value.TryGetValue(ManualLanguage(uiLang), out var list) ? list : [];

    /// <summary>Rendered HTML of a chapter, or null when there is no such chapter.</summary>
    public string? GetChapterHtml(string? uiLang, string slug)
    {
        var lang = ManualLanguage(uiLang);
        if (!GetChapters(uiLang).Any(c => c.Slug == slug)) return null;
        return _html.GetOrAdd($"{lang}/{slug}", _ =>
        {
            var md = ReadText($"{Prefix}{lang}/{slug}.md") ?? string.Empty;
            return Render(md);
        });
    }

    /// <summary>An embedded manual image, or null when unknown or the name is not a plain file name.</summary>
    public (Stream Stream, string ContentType)? OpenImage(string name)
    {
        if (!SafeImageName.IsMatch(name)) return null;
        var stream = _asm.GetManifestResourceStream($"{Prefix}img/{name}");
        if (stream is null) return null;
        var type = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".png"            => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif"            => "image/gif",
            ".webp"           => "image/webp",
            ".svg"            => "image/svg+xml",
            _                 => "application/octet-stream"
        };
        return (stream, type);
    }

    // ── internals ────────────────────────────────────────────────────────

    private string? ReadText(string resource)
    {
        using var s = _asm.GetManifestResourceStream(resource);
        if (s is null) return null;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private IReadOnlyDictionary<string, IReadOnlyList<ManualChapter>> LoadChapters()
    {
        var result = new Dictionary<string, IReadOnlyList<ManualChapter>>();
        foreach (var lang in new[] { "de", "en" })
        {
            var folder = $"{Prefix}{lang}/";
            var list = _asm.GetManifestResourceNames()
                .Where(n => n.StartsWith(folder, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal)
                .Select(n =>
                {
                    var slug = n[folder.Length..^3];
                    return new ManualChapter(slug, TitleOf(ReadText(n) ?? string.Empty, slug));
                })
                .ToList();
            result[lang] = list;
        }
        return result;
    }

    private static string TitleOf(string markdown, string fallback)
    {
        foreach (var line in markdown.Split('\n'))
        {
            var t = line.TrimEnd('\r');
            if (t.StartsWith("# ", StringComparison.Ordinal)) return t[2..].Trim();
        }
        return fallback;
    }

    private static string Render(string markdown)
    {
        var doc = Markdown.Parse(markdown, Pipeline);
        foreach (var link in doc.Descendants<LinkInline>())
        {
            var url = link.Url ?? string.Empty;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var attrs = link.GetAttributes();
                attrs.AddProperty("target", "_blank");
                attrs.AddProperty("rel", "noopener");
            }
            else if (link.IsImage)
            {
                link.Url = "/help-img/" + Path.GetFileName(url);
            }
            else if (url.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !url.StartsWith('/'))
            {
                link.Url = "/help/" + Path.GetFileNameWithoutExtension(url);
            }
        }
        return doc.ToHtml(Pipeline);
    }
}
