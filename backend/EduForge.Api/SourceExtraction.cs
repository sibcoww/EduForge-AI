using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using EduForge.Application;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace EduForge.Api;

public static class SourceExtraction
{
    public const int MaxCharacters = 300000;
    public static List<SourceSection> Extract(string name, byte[] bytes, CancellationToken ct)
    {
        var sections = new List<SourceSection>(); var characters = 0;
        void Add(string text, string label) {
            text = text.Replace("\r\n", "\n").Replace('\0', ' ').Trim();
            for (var offset = 0; offset < text.Length; offset += 3500) {
                var piece = text.Substring(offset, Math.Min(3500, text.Length - offset));
                if (string.IsNullOrWhiteSpace(piece)) continue;
                characters += piece.Length;
                if (characters > MaxCharacters || sections.Count >= 300) throw new InvalidDataException("Источник слишком большой. Загрузите отдельную главу: до 300 000 символов и 300 фрагментов.");
                sections.Add(new(Guid.NewGuid(), label + (text.Length > 3500 ? $" · часть {offset / 3500 + 1}" : ""), piece));
            }
        }
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext == ".pdf") {
            if (bytes.Length < 5 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-") throw new InvalidDataException("Файл не является PDF.");
            using var pdf = PdfDocument.Open(bytes);
            if (pdf.NumberOfPages > 200) throw new InvalidDataException("Загрузите часть PDF объёмом не более 200 страниц.");
            foreach (var page in pdf.GetPages()) { ct.ThrowIfCancellationRequested(); Add(ContentOrderTextExtractor.GetText(page), $"Страница {page.Number}"); }
        } else if (ext == ".docx") {
            using var stream = new MemoryStream(bytes); using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count > 2000 || archive.Entries.Sum(e => e.Length) > 30 * 1024 * 1024) throw new InvalidDataException("Распакованный DOCX слишком большой.");
            var entry = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("Не найден текст DOCX.");
            if (entry.Length > 10 * 1024 * 1024) throw new InvalidDataException("Текст DOCX слишком большой.");
            using var input = entry.Open();
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 10 * 1024 * 1024 });
            var xml = XDocument.Load(reader); XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var buffer = new StringBuilder(); var part = 1;
            foreach (var paragraph in xml.Descendants(w + "p")) {
                ct.ThrowIfCancellationRequested();
                var line = string.Concat(paragraph.Descendants().Select(n => n.Name == w + "t" ? n.Value : n.Name == w + "tab" ? "\t" : n.Name == w + "br" ? "\n" : ""));
                if (buffer.Length + line.Length > 3000 && buffer.Length > 0) { Add(buffer.ToString(), $"Фрагмент {part++}"); buffer.Clear(); }
                buffer.AppendLine(line);
            }
            Add(buffer.ToString(), $"Фрагмент {part}");
        } else if (ext is ".txt" or ".md") {
            var encoding = new UTF8Encoding(false, true);
            var text = encoding.GetString(bytes).TrimStart('\uFEFF');
            Add(text, "Текст");
        } else throw new InvalidDataException("Поддерживаются PDF с текстовым слоем, DOCX, TXT и Markdown в UTF-8.");
        if (sections.Count == 0) throw new InvalidDataException("Текст не найден. PDF-скан без текстового слоя пока не поддерживается: загрузите распознанный текст или DOCX.");
        return sections;
    }
}
