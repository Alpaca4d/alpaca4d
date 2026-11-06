using System.Linq;

namespace Alpaca4d
{
    /// <summary>
    /// File paths on the way into a .tcl file.
    ///
    /// Tcl splits a command at whitespace and substitutes backslashes, dollars and brackets, so a
    /// full path - "C:\Users\Jane Doe\recorder.mpco" - reaches OpenSees as several broken words.
    /// Braces pass a word through untouched. A plain name such as "recorder.mpco" is written as it
    /// stands, so decks that worked before come out byte for byte the same.
    /// </summary>
    public static class TclPath
    {
        private const string Special = "\\\"{}[]$;";

        /// <summary>A path as one Tcl word: as it stands when Tcl would read it unchanged, in braces otherwise.</summary>
        public static string Write(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "{}";

            bool plain = path.All(c => !char.IsWhiteSpace(c) && Special.IndexOf(c) < 0);
            return plain ? path : "{" + path + "}";
        }
    }
}
