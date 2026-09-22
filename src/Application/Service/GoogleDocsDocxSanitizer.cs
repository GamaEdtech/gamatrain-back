namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;

    using DocumentFormat.OpenXml;
    using DocumentFormat.OpenXml.Packaging;

    using A = DocumentFormat.OpenXml.Drawing;
    using Ooxml = DocumentFormat.OpenXml.Wordprocessing;
    using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
    using Wps = DocumentFormat.OpenXml.Office2010.Word.DrawingShape;

    /// <summary>
    /// Google Docs' DOCX importer rejects the whole file ("File could not open") when it meets Word's native
    /// vector shapes (<c>wps:wsp</c>, even wrapped in <c>mc:AlternateContent</c>) or other floating non-picture
    /// DrawingML objects. This strips only those objects from the body, headers and footers of an already-built
    /// DOCX - text, tables, cell formatting and pictures (inline or floating) are left untouched, and no other
    /// format conversion happens. Returns the input bytes unchanged when there is nothing to strip.
    /// </summary>
    public static class GoogleDocsDocxSanitizer
    {
#pragma warning disable S1075 // spec-mandated OOXML namespace URIs, not configurable endpoints
        private const string PictureGraphicDataUri = "http://schemas.openxmlformats.org/drawingml/2006/picture";
#pragma warning restore S1075

        public static byte[] StripUnsupportedShapes([NotNull] byte[] docx)
        {
            var removedAny = false;
            using var stream = new MemoryStream();
            stream.Write(docx, 0, docx.Length);
            stream.Position = 0;

            using (var document = WordprocessingDocument.Open(stream, true))
            {
                var mainPart = document.MainDocumentPart;
                if (mainPart is null)
                {
                    return docx;
                }

                var roots = new List<OpenXmlPartRootElement?> { mainPart.Document };
                roots.AddRange(mainPart.HeaderParts.Select(t => (OpenXmlPartRootElement?)t.Header));
                roots.AddRange(mainPart.FooterParts.Select(t => (OpenXmlPartRootElement?)t.Footer));

                foreach (var root in roots.OfType<OpenXmlPartRootElement>().Where(StripFromRoot))
                {
                    removedAny = true;
                    root.Save();
                }
            }

            return removedAny ? stream.ToArray() : docx;
        }

        private static bool StripFromRoot(OpenXmlPartRootElement root)
        {
            var targets = root.Descendants<AlternateContent>().Where(ContainsUnsupportedShape).Cast<OpenXmlElement>()
                .Concat(root.Descendants<Ooxml.Drawing>().Where(t => t.Ancestors<AlternateContent>().All(a => !ContainsUnsupportedShape(a)) && IsUnsupportedDrawing(t)))
                .ToList();
            if (targets.Count == 0)
            {
                return false;
            }

            var parents = targets.Where(t => t.Parent is not null).Select(t => t.Parent).ToList();
            foreach (var target in targets)
            {
                target.Remove();
            }

            // A run/paragraph left holding nothing but its own properties was only a carrier for the removed
            // shape - drop it. A table cell must keep at least one paragraph, so those are never removed.
            foreach (var run in parents.OfType<Ooxml.Run>().Distinct().Where(t => t.Parent is not null && t.ChildElements.All(c => c is Ooxml.RunProperties)).ToList())
            {
                var paragraph = run.Parent;
                run.Remove();
                if (paragraph is Ooxml.Paragraph p && p.Parent is not Ooxml.TableCell && p.ChildElements.All(t => t is Ooxml.ParagraphProperties))
                {
                    p.Remove();
                }
            }

            if (!root.Descendants<AlternateContent>().Any())
            {
                root.RemoveNamespaceDeclaration("mc");
                root.RemoveNamespaceDeclaration("wps");
            }

            return true;
        }

        private static bool ContainsUnsupportedShape(AlternateContent alternateContent) =>
            alternateContent.Descendants<Wps.WordprocessingShape>().Any()
            || alternateContent.GetFirstChild<AlternateContentChoice>()?.Requires?.Value?.Contains("wps", StringComparison.Ordinal) == true;

        // Pictures (inline or floating) are kept; anything else DrawingML - a wps shape, a group, a chart, ... - is not.
        private static bool IsUnsupportedDrawing(Ooxml.Drawing drawing) =>
            drawing.Descendants<Wps.WordprocessingShape>().Any()
            || drawing.Descendants<A.GraphicData>().Any(t => t.Uri?.Value != PictureGraphicDataUri)
            || (drawing.GetFirstChild<Wp.Anchor>() is not null && !drawing.Descendants<A.GraphicData>().Any());
    }
}
