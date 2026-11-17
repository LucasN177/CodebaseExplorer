using System.Xml;
using System.Xml.Linq;

namespace CodebaseExplorer.Analysis.Parsing;

/// <summary>
/// Hardened XML loading for untrusted repository files (SECURITY.md §3): DTD processing prohibited,
/// no external resolver, character limit, line information for evidence locations.
/// </summary>
internal static class SafeXml
{
    /// <summary>Loads <paramref name="content"/>; returns the error message instead of throwing on malformed or hostile input.</summary>
    public static bool TryLoad(string content, long maxCharacters, out XDocument? document, out string? error)
    {
        ArgumentNullException.ThrowIfNull(content);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maxCharacters,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CloseInput = true,
        };

        try
        {
            using var stringReader = new StringReader(content);
            using var xmlReader = XmlReader.Create(stringReader, settings);
            document = XDocument.Load(xmlReader, LoadOptions.SetLineInfo);
            error = null;
            return true;
        }
        catch (XmlException exception)
        {
            document = null;
            error = exception.Message;
            return false;
        }
    }

    /// <summary>1-based line and column of the element's opening <c>&lt;</c>; (0, 0) if unknown.</summary>
    public static (int Line, int Column) PositionOf(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        IXmlLineInfo info = element;
        // XmlReader reports the position of the element name; the '<' is one column earlier.
        return info.HasLineInfo() ? (info.LineNumber, Math.Max(1, info.LinePosition - 1)) : (0, 0);
    }
}
