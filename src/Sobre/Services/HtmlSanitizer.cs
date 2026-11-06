using System;
using System.Text.RegularExpressions;
using System.Web;

namespace Sobre.Services
{
    /// <summary>
    /// Makes untrusted mail HTML safe to render: strips scripts, frames, forms, remote
    /// stylesheets and (by default) remote images, then wraps the result in a shell that
    /// honours the current theme and accent colour.
    /// </summary>
    public static class HtmlSanitizer
    {
        private static readonly RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled;

        private static readonly Regex ScriptBlock = new(@"<script\b[^>]*>.*?</script>", Opts);
        private static readonly Regex StyleBlockNoScope = new(@"<style\b[^>]*>.*?</style>", Opts);
        private static readonly Regex IframeBlock = new(@"<iframe\b[^>]*>.*?</iframe>", Opts);
        private static readonly Regex ObjectBlock = new(@"<(object|embed|applet|frame|frameset)\b[^>]*>.*?</\1>", Opts);
        private static readonly Regex MetaRefresh = new(@"<meta[^>]+http-equiv\s*=\s*[""']?refresh[^>]*>", Opts);
        private static readonly Regex FormTag = new(@"</?form\b[^>]*>", Opts);
        private static readonly Regex InputTag = new(@"<(input|button|textarea|select)\b[^>]*>", Opts);
        private static readonly Regex BaseTag = new(@"<base\b[^>]*>", Opts);
        private static readonly Regex LinkTag = new(@"<link\b[^>]*>", Opts);
        private static readonly Regex EventAttr = new(@"\son[a-z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", Opts);
        private static readonly Regex JsHref = new(@"(href|src)\s*=\s*(""\s*javascript:[^""]*""|'\s*javascript:[^']*'|javascript:[^\s>]+)", Opts);
        private static readonly Regex ImgTag = new(@"<img\b[^>]*>", Opts);
        private static readonly Regex SrcAttr = new(@"\ssrc\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", Opts);
        private static readonly Regex CssUrl = new(@"url\(\s*['""]?(?<u>https?:)?//[^)]*\)", Opts);
        private static readonly Regex ImportRule = new(@"@import[^;]+;", Opts);

        public static int CountRemoteImages(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return 0;
            int n = 0;
            foreach (Match m in ImgTag.Matches(html))
            {
                var src = SrcAttr.Match(m.Value);
                if (src.Success && (src.Value.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                                    src.Value.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
                                    src.Value.Contains("//", StringComparison.Ordinal)))
                    n++;
            }
            return n;
        }

        public static string Sanitize(string? html, bool allowRemoteImages)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";

            var s = ScriptBlock.Replace(html, "");
            s = IframeBlock.Replace(s, "");
            s = ObjectBlock.Replace(s, "");
            s = MetaRefresh.Replace(s, "");
            s = BaseTag.Replace(s, "");
            s = LinkTag.Replace(s, "");
            s = FormTag.Replace(s, "");
            s = InputTag.Replace(s, "");
            s = EventAttr.Replace(s, "");
            s = JsHref.Replace(s, "$1=\"#\"");

            // Keep author styles, but never fetch remote assets through them.
            s = ImportRule.Replace(s, "");
            s = CssUrl.Replace(s, "none");

            if (!allowRemoteImages)
            {
                s = ImgTag.Replace(s, m =>
                {
                    var src = SrcAttr.Match(m.Value);
                    if (!src.Success) return m.Value;
                    var url = src.Value;
                    bool remote = url.Contains("http://", StringComparison.OrdinalIgnoreCase)
                               || url.Contains("https://", StringComparison.OrdinalIgnoreCase)
                               || Regex.IsMatch(url, @"=\s*[""']?\s*//");
                    if (!remote) return m.Value;
                    return "<span style=\"display:inline-block;padding:10px 14px;border-radius:6px;" +
                           "background:rgba(128,128,128,.14);color:inherit;opacity:.75;font:12px 'Segoe UI',sans-serif;\">" +
                           "Remote image blocked</span>";
                });
            }

            return s;
        }

        /// <summary>Wraps sanitized mail HTML in the reader shell (theme + accent aware).</summary>
        public static string BuildDocument(string? bodyHtml, string? bodyText, bool dark, string accentHex, int fontSize, bool plainPreference = false)
        {
            string textColor = dark ? "#F2F2F2" : "#1B1B1B";
            string bg = dark ? "#141414" : "#FFFFFF";
            string link = accentHex;
            string quote = dark ? "#A6A6A6" : "#5C5C5C";
            string rule = dark ? "rgba(255,255,255,.14)" : "rgba(0,0,0,.12)";
            string codeBg = dark ? "rgba(255,255,255,.07)" : "rgba(0,0,0,.05)";

            string content;
            if (plainPreference || string.IsNullOrWhiteSpace(bodyHtml))
            {
                var t = string.IsNullOrWhiteSpace(bodyText) ? "" : bodyText!;
                content = "<pre class='plain'>" + HttpUtility.HtmlEncode(t) + "</pre>";
            }
            else
            {
                var clean = Sanitize(bodyHtml, allowRemoteImages: false);
                content = "<div class='mail'>" + clean + "</div>";
            }

            return $@"<!DOCTYPE html>
<html><head>
<meta charset=""utf-8""/>
<meta name=""viewport"" content=""width=device-width, initial-scale=1""/>
<style>
  :root {{ color-scheme: {(dark ? "dark" : "light")}; }}
  html, body {{ margin:0; padding:0; background:{bg}; }}
  body {{
    color:{textColor}; background:{bg};
    font-family: 'Segoe UI Variable Text','Segoe UI',system-ui,-apple-system,sans-serif;
    font-size:{fontSize}px; line-height:1.58;
    padding:2px 22px 40px 22px;
    -webkit-font-smoothing:antialiased;
    word-wrap:break-word; overflow-wrap:break-word;
  }}
  .mail img {{ max-width:100% !important; height:auto !important; border-radius:6px; }}
  .mail table {{ max-width:100% !important; }}
  .mail a {{ color:{link}; text-decoration:none; }}
  .mail a:hover {{ text-decoration:underline; }}
  .mail blockquote {{
    margin:12px 0; padding:2px 0 2px 14px;
    border-left:3px solid {link}; color:{quote};
  }}
  .mail hr {{ border:0; border-top:1px solid {rule}; margin:18px 0; }}
  .mail code, .mail pre {{
    font-family:'Cascadia Mono',Consolas,monospace; font-size:.92em;
    background:{codeBg}; border-radius:5px; padding:2px 5px;
  }}
  .mail pre.plain {{
    background:transparent; padding:0; white-space:pre-wrap;
    font-family:'Segoe UI Variable Text','Segoe UI',sans-serif; font-size:{fontSize}px; line-height:1.58;
  }}
  .mail h1,.mail h2,.mail h3 {{ line-height:1.25; margin:.7em 0 .4em; }}
  ::selection {{ background:{link}44; }}
  /* Scrollbars tuned for the pane */
  ::-webkit-scrollbar {{ width:10px; height:10px; }}
  ::-webkit-scrollbar-thumb {{ background:{(dark ? "#5A5A5A" : "#C8C8C8")}; border-radius:5px; }}
  ::-webkit-scrollbar-track {{ background:transparent; }}
</style>
</head><body>{content}</body></html>";
        }
    }
}
