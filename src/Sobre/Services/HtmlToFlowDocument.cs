using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using HtmlAgilityPack;

namespace Sobre.Services
{
    /// <summary>
    /// Converts untrusted mail HTML into a WPF FlowDocument. Using a FlowDocument (rather than
    /// an embedded browser) keeps the attack surface tiny, gives proper text selection, and
    /// inherits theme colours automatically.
    /// </summary>
    public static class HtmlToFlowDocument
    {
        public static FlowDocument Convert(string? html, bool plainText, bool dark, double fontSize, Color accent)
        {
            var doc = new FlowDocument
            {
                // PagePadding keeps text off the pane edges; PageWidth = NaN lets the
                // document reflow to the viewer width (wrapped text) while still allowing
                // a horizontal scrollbar for truly wide tables/code via the viewer.
                PagePadding = new Thickness(20, 2, 20, 26),
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = fontSize,
                LineHeight = fontSize * 1.58,
                TextAlignment = TextAlignment.Left,
                PageWidth = double.NaN
            };

            var textBrush = new SolidColorBrush(dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1B, 0x1B, 0x1B));
            textBrush.Freeze();
            doc.Foreground = textBrush;

            if (plainText || string.IsNullOrWhiteSpace(html))
            {
                AddPlainText(doc, System.Net.WebUtility.HtmlDecode(html ?? ""));
                return doc;
            }

            try
            {
                var hdoc = new HtmlDocument();
                hdoc.LoadHtml(html);
                var body = hdoc.DocumentNode.SelectSingleNode("//body") ?? hdoc.DocumentNode;

                foreach (var node in body.ChildNodes)
                    AddBlock(doc, node, dark, fontSize, accent);
            }
            catch
            {
                AddPlainText(doc, MailService.HtmlToText(html ?? ""));
            }

            return doc;
        }

        private static void AddPlainText(FlowDocument doc, string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            var para = new Paragraph { Margin = new Thickness(0, 0, 0, 0) };
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) para.Inlines.Add(new LineBreak());
                para.Inlines.Add(new Run(lines[i]));
            }
            doc.Blocks.Add(para);
        }

        private static bool IsBlockTag(string name) => name is "p" or "div" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
            or "ul" or "ol" or "li" or "table" or "blockquote" or "pre" or "hr" or "center" or "section" or "article"
            or "header" or "footer" or "tr" or "td" or "th" or "tbody" or "thead" or "figure" or "figcaption" or "font";

        private static void AddBlock(FlowDocument doc, HtmlNode node, bool dark, double fontSize, Color accent)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                var t = HtmlEntity.DeEntitize(node.InnerText);
                if (!string.IsNullOrWhiteSpace(t))
                    doc.Blocks.Add(new Paragraph(new Run(t.Trim())) { Margin = new Thickness(0, 0, 0, fontSize * 0.55) });
                return;
            }
            if (node.NodeType != HtmlNodeType.Element) return;

            var name = node.Name.ToLowerInvariant();

            switch (name)
            {
                case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                {
                    var level = name[1] - '0';
                    var p = new Paragraph
                    {
                        FontWeight = FontWeights.SemiBold,
                        FontSize = fontSize * (level == 1 ? 1.55 : level == 2 ? 1.32 : 1.15),
                        Margin = new Thickness(0, fontSize * 0.5, 0, fontSize * 0.35)
                    };
                    AddInlines(p, node, dark, fontSize, accent);
                    doc.Blocks.Add(p);
                    return;
                }

                case "ul": case "ol":
                {
                    var list = new List
                    {
                        MarkerStyle = name == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Margin = new Thickness(0, fontSize * 0.2, 0, fontSize * 0.6),
                        Padding = new Thickness(fontSize * 1.2, 0, 0, 0)
                    };
                    foreach (var li in node.Descendants("li").Where(x => x.ParentNode == node))
                    {
                        var item = new ListItem();
                        var p = new Paragraph { Margin = new Thickness(0, 0, 0, fontSize * 0.28) };
                        AddInlines(p, li, dark, fontSize, accent);
                        item.Blocks.Add(p);
                        list.ListItems.Add(item);
                    }
                    doc.Blocks.Add(list);
                    return;
                }

                case "blockquote":
                {
                    var p = new Paragraph
                    {
                        Margin = new Thickness(0, fontSize * 0.4, 0, fontSize * 0.6),
                        Padding = new Thickness(fontSize * 0.9, 0, 0, 0),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        BorderBrush = new SolidColorBrush(accent),
                        Foreground = new SolidColorBrush(dark ? Color.FromRgb(0xA6, 0xA6, 0xA6) : Color.FromRgb(0x5C, 0x5C, 0x5C))
                    };
                    AddInlines(p, node, dark, fontSize, accent);
                    doc.Blocks.Add(p);
                    return;
                }

                case "pre":
                {
                    var p = new Paragraph
                    {
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        FontSize = fontSize * 0.94,
                        Margin = new Thickness(0, fontSize * 0.3, 0, fontSize * 0.6),
                        Padding = new Thickness(10),
                        Background = new SolidColorBrush(dark ? Color.FromArgb(0x14, 255, 255, 255) : Color.FromArgb(0x0D, 0, 0, 0))
                    };
                    p.Inlines.Add(new Run(HtmlEntity.DeEntitize(node.InnerText)));
                    doc.Blocks.Add(p);
                    return;
                }

                case "hr":
                    doc.Blocks.Add(new Paragraph
                    {
                        Margin = new Thickness(0, fontSize, 0, fontSize),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x24, 255, 255, 255) : Color.FromArgb(0x1F, 0, 0, 0))
                    });
                    return;

                case "table":
                    doc.Blocks.Add(BuildTable(node, dark, fontSize, accent));
                    return;

                case "img":
                {
                    var p = new Paragraph { Margin = new Thickness(0, 2, 0, 6) };
                    p.Inlines.Add(new Run("[ remote image blocked ]")
                    {
                        Foreground = new SolidColorBrush(dark ? Color.FromRgb(0x9A, 0x9A, 0x9A) : Color.FromRgb(0x74, 0x74, 0x74)),
                        FontSize = fontSize * 0.86,
                        FontStyle = FontStyles.Italic
                    });
                    doc.Blocks.Add(p);
                    return;
                }

                case "br":
                    return;
            }

            // Generic container: recurse into children if it holds block-level content,
            // otherwise treat the whole node as one paragraph.
            bool hasBlockChild = node.Descendants().Any(d => IsBlockTag(d.Name.ToLowerInvariant()));
            if (hasBlockChild || name is "div" or "center" or "section" or "article" or "body" or "html" or "font" or "span")
            {
                foreach (var child in node.ChildNodes)
                    AddBlock(doc, child, dark, fontSize, accent);
                return;
            }

            var para = new Paragraph { Margin = new Thickness(0, 0, 0, fontSize * 0.55) };
            AddInlines(para, node, dark, fontSize, accent);
            if (para.Inlines.Count > 0) doc.Blocks.Add(para);
        }

        private static Table BuildTable(HtmlNode tableNode, bool dark, double fontSize, Color accent)
        {
            var table = new Table
            {
                CellSpacing = 0,
                Margin = new Thickness(0, fontSize * 0.4, 0, fontSize * 0.6),
                BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x1F, 255, 255, 255) : Color.FromArgb(0x14, 0, 0, 0)),
                BorderThickness = new Thickness(1)
            };

            var rows = tableNode.Descendants("tr").ToList();
            int colCount = Math.Max(1, rows.Select(r => r.Elements("td").Count() + r.Elements("th").Count()).DefaultIfEmpty(1).Max());
            for (int i = 0; i < colCount; i++) table.Columns.Add(new TableColumn());

            var group = new TableRowGroup();
            table.RowGroups.Add(group);

            foreach (var tr in rows)
            {
                var row = new TableRow();
                foreach (var cellNode in tr.Elements("td").Concat(tr.Elements("th")))
                {
                    bool header = cellNode.Name.Equals("th", StringComparison.OrdinalIgnoreCase);
                    var para = new Paragraph { Margin = new Thickness(8, 6, 8, 6), FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal };
                    AddInlines(para, cellNode, dark, fontSize, accent);
                    row.Cells.Add(new TableCell(para)
                    {
                        BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x1F, 255, 255, 255) : Color.FromArgb(0x14, 0, 0, 0)),
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Padding = new Thickness(0),
                        Background = header
                            ? new SolidColorBrush(dark ? Color.FromArgb(0x14, 255, 255, 255) : Color.FromArgb(0x08, 0, 0, 0))
                            : Brushes.Transparent
                    });
                }
                if (row.Cells.Count > 0) group.Rows.Add(row);
            }

            return table;
        }

        private static void AddInlines(Paragraph para, HtmlNode node, bool dark, double fontSize, Color accent)
        {
            foreach (var child in node.ChildNodes)
                AddInline(para, child, dark, fontSize, accent, null);
        }

        private static void AddInline(Paragraph para, HtmlNode node, bool dark, double fontSize, Color accent,
            TextDecorationCollection? decorations)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                var raw = HtmlEntity.DeEntitize(node.InnerText);
                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"[ \t\r\n]+", " ");
                if (raw.Length == 0) return;
                var run = new Run(raw);
                if (decorations != null) run.TextDecorations = decorations;
                para.Inlines.Add(run);
                return;
            }
            if (node.NodeType != HtmlNodeType.Element) return;

            var tag = node.Name.ToLowerInvariant();
            switch (tag)
            {
                case "br":
                    para.Inlines.Add(new LineBreak());
                    return;
                case "a":
                {
                    var href = node.GetAttributeValue("href", "");
                    var link = new Hyperlink { Foreground = new SolidColorBrush(accent), TextDecorations = null };
                    var inner = new List<Inline>();
                    var tmp = new Paragraph();
                    foreach (var c in node.ChildNodes) AddInline(tmp, c, dark, fontSize, accent, decorations);
                    foreach (var inl in tmp.Inlines.ToList()) { tmp.Inlines.Remove(inl); link.Inlines.Add(inl); }
                    if (!string.IsNullOrWhiteSpace(href) && (href.StartsWith("http", StringComparison.OrdinalIgnoreCase) || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)))
                    {
                        try { link.NavigateUri = new Uri(href); } catch { }
                        link.ToolTip = href;
                    }
                    if (link.Inlines.Count == 0) link.Inlines.Add(new Run(node.InnerText));
                    para.Inlines.Add(link);
                    return;
                }
                case "b": case "strong":
                {
                    var tmp = new Paragraph();
                    foreach (var c in node.ChildNodes) AddInline(tmp, c, dark, fontSize, accent, decorations);
                    var b = new Bold(); foreach (var inl in tmp.Inlines.ToList()) { tmp.Inlines.Remove(inl); b.Inlines.Add(inl); }
                    para.Inlines.Add(b);
                    return;
                }
                case "i": case "em":
                {
                    var tmp = new Paragraph();
                    foreach (var c in node.ChildNodes) AddInline(tmp, c, dark, fontSize, accent, decorations);
                    var it = new Italic(); foreach (var inl in tmp.Inlines.ToList()) { tmp.Inlines.Remove(inl); it.Inlines.Add(inl); }
                    para.Inlines.Add(it);
                    return;
                }
                case "u":
                {
                    var tmp = new Paragraph();
                    foreach (var c in node.ChildNodes) AddInline(tmp, c, dark, fontSize, accent, TextDecorations.Underline);
                    foreach (var inl in tmp.Inlines.ToList()) { tmp.Inlines.Remove(inl); para.Inlines.Add(inl); }
                    return;
                }
                case "code": case "kbd": case "samp":
                {
                    var run = new Run(node.InnerText)
                    {
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        FontSize = fontSize * 0.94
                    };
                    para.Inlines.Add(run);
                    return;
                }
                case "img":
                    para.Inlines.Add(new Run("[ remote image blocked ]")
                    {
                        FontStyle = FontStyles.Italic,
                        FontSize = fontSize * 0.86,
                        Foreground = new SolidColorBrush(dark ? Color.FromRgb(0x9A, 0x9A, 0x9A) : Color.FromRgb(0x74, 0x74, 0x74))
                    });
                    return;
                case "style": case "script": case "head": case "title":
                    return;
                default:
                    foreach (var c in node.ChildNodes) AddInline(para, c, dark, fontSize, accent, decorations);
                    return;
            }
        }
    }
}
