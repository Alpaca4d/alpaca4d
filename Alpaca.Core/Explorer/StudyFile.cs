using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Alpaca4d.Explorer
{
    /// <summary>
    /// The data.csv of a design study: one row per design, the columns the explorer reads.
    ///
    /// The columns are the ones Design Explorer and Colibri settled on, so a study made here opens
    /// in either and one made with Colibri opens in the Alpaca4d Explorer:
    ///   in:Name   an input the design was made from
    ///   out:Name  a result it was measured by
    ///   img       the image of the design, or img:View when there is one per view
    ///   threeD    its 3D model, a .glb next to data.csv
    ///
    /// A study only ever grows. A design whose inputs are already in the file is not written twice,
    /// so a run that stopped half way picks up where it left off, and running the same study again
    /// costs nothing. Starting over is <see cref="Clear"/>, which removes data.csv and the files it
    /// names, and nothing else: the folder may well be one the user keeps other things in.
    /// </summary>
    public sealed class StudyFile
    {
        public const string DataFileName = "data.csv";
        public const string InputPrefix = "in:";
        public const string OutputPrefix = "out:";
        public const string ImageColumn = "img";
        public const string ModelColumn = "threeD";

        // No byte-order mark: the explorer reads the first column's name, and a BOM in front of it
        // would turn "in:Span" into something that is not an input.
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public string Folder { get; }
        public string DataPath => Path.Combine(Folder, DataFileName);

        /// <summary>The columns of the file on disk, or null when there is no file yet.</summary>
        public IReadOnlyList<string> Columns => _columns;

        /// <summary>How many designs the file holds.</summary>
        public int Count => _keys.Count;

        private List<string> _columns;
        private int[] _inputColumns = new int[0];
        private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);

        // The picture and model files the rows name. Case is ignored, as Windows and macOS ignore it.
        private readonly HashSet<string> _files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // How long data.csv was when it was last read or written here; anything else means another
        // writer - a second Explorer Record, another machine on a shared folder - has been at it.
        private long _knownLength = -1;

        private StudyFile(string folder)
        {
            Folder = folder;
        }

        /// <summary>The study in <paramref name="folder"/>, read from its data.csv if it has one.</summary>
        public static StudyFile Open(string folder)
        {
            var study = new StudyFile(Path.GetFullPath(folder));
            study.Reload();
            return study;
        }

        /// <summary>Reads the columns and the designs already recorded back from disk.</summary>
        public void Reload()
        {
            _columns = null;
            _inputColumns = new int[0];
            _keys.Clear();
            _files.Clear();
            _knownLength = -1;
            if (!File.Exists(DataPath)) return;

            using (var reader = new StreamReader(DataPath, Utf8, true))
            {
                string first = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(first)) return;
                SetColumns(ParseLine(first));

                var fileColumns = FileColumns(_columns);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var cells = ParseLine(line);
                    if (cells.Count < _columns.Count) continue;
                    _keys.Add(Key(_inputColumns.Select(i => cells[i])));
                    foreach (int i in fileColumns) NoteFile(cells[i]);
                }
            }
            _knownLength = new FileInfo(DataPath).Length;
        }

        /// <summary>Reads the file again if someone else has written to it, or removed it, since.</summary>
        public void Refresh()
        {
            bool exists = File.Exists(DataPath);
            if (!exists && _columns == null) return;
            if (!exists || new FileInfo(DataPath).Length != _knownLength) Reload();
        }

        private static List<int> FileColumns(IList<string> header)
        {
            return Enumerable.Range(0, header.Count)
                .Where(i => header[i].StartsWith(ImageColumn, StringComparison.OrdinalIgnoreCase)
                         || header[i].Equals(ModelColumn, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private void NoteFile(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) _files.Add(name.Trim());
        }

        /// <summary>
        /// The name to save a design's files under: <see cref="FileStem"/>, unless one of the files
        /// it would give is already another design's - two designs whose values differ only by case,
        /// or by characters a file name cannot hold - and then the stem with a hash of the design.
        /// </summary>
        public string StemFor(IList<string> inputValues, Func<string, IEnumerable<string>> filesOf)
        {
            string stem = FileStem(inputValues);
            return filesOf(stem).Any(_files.Contains) ? HashedStem(inputValues) : stem;
        }
        private void SetColumns(IEnumerable<string> columns)
        {
            _columns = columns.ToList();
            _inputColumns = Enumerable.Range(0, _columns.Count)
                .Where(i => _columns[i].StartsWith(InputPrefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        /// <summary>
        /// The columns of a study with these inputs and outputs. <paramref name="imageNames"/> holds
        /// one name per image taken of each design: a single image is the column "img", several are
        /// "img:Name" each, so the explorer can say which view it is showing.
        /// </summary>
        public static List<string> ColumnsFor(IEnumerable<string> inputs, IEnumerable<string> outputs, IList<string> imageNames, bool model)
        {
            var columns = new List<string>();
            columns.AddRange(inputs.Select(name => InputPrefix + name));
            columns.AddRange(outputs.Select(name => OutputPrefix + name));
            if (imageNames != null && imageNames.Count == 1)
                columns.Add(ImageColumn);
            else if (imageNames != null)
                columns.AddRange(imageNames.Select(name => ImageColumn + ":" + name));
            if (model)
                columns.Add(ModelColumn);
            return columns;
        }

        /// <summary>Whether a design with these columns can be added: the file is new, or has the same ones.</summary>
        public bool Accepts(IList<string> columns)
        {
            return _columns == null || _columns.SequenceEqual(columns, StringComparer.Ordinal);
        }

        /// <summary>Whether a design with these input values is in the file already.</summary>
        public bool Contains(IEnumerable<string> inputValues)
        {
            return _keys.Contains(Key(inputValues));
        }

        /// <summary>
        /// Adds a design. Returns false, and writes nothing, when one with the same inputs is there.
        /// Throws when the file has other columns: appending would leave rows that do not line up
        /// with the header, and the explorer would read every value under the wrong name.
        /// </summary>
        public bool Append(IList<string> columns, IList<string> inputValues, IList<string> cells)
        {
            if (cells.Count != columns.Count)
                throw new ArgumentException($"A design has {cells.Count} values for {columns.Count} columns.");
            if (!Accepts(columns))
                throw new InvalidOperationException(
                    $"{DataPath} holds a study with other columns. Record into another folder, or start a new study.");
            Refresh();
            if (!Accepts(columns))
                throw new InvalidOperationException(
                    $"{DataPath} holds a study with other columns. Record into another folder, or start a new study.");
            if (Contains(inputValues)) return false;

            Directory.CreateDirectory(Folder);
            var text = new StringBuilder();
            if (_columns == null || !File.Exists(DataPath))
            {
                text.Append(Line(columns));
                SetColumns(columns);
            }
            text.Append(Line(cells));
            File.AppendAllText(DataPath, text.ToString(), Utf8);
            _knownLength = new FileInfo(DataPath).Length;

            _keys.Add(Key(inputValues));
            foreach (int i in FileColumns(_columns)) NoteFile(cells[i]);
            return true;
        }

        /// <summary>
        /// Starts the study over: deletes data.csv and the image and model files it names, and
        /// returns how many files went. Only files inside the folder are touched, whatever a row says.
        /// </summary>
        public int Clear()
        {
            int deleted = 0;
            if (File.Exists(DataPath))
            {
                var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var lines = File.ReadAllLines(DataPath, Utf8);
                if (lines.Length > 0)
                {
                    var fileColumns = FileColumns(ParseLine(lines[0]));
                    foreach (var line in lines.Skip(1))
                    {
                        var cells = ParseLine(line);
                        foreach (int i in fileColumns.Where(i => i < cells.Count))
                        {
                            string path = InsideFolder(cells[i]);
                            if (path != null) files.Add(path);
                        }
                    }
                }

                foreach (var path in files.Where(File.Exists))
                {
                    File.Delete(path);
                    deleted++;
                }
                File.Delete(DataPath);
                deleted++;
            }

            Reload();
            return deleted;
        }

        /// <summary>The full path of a file a row names, or null when it is a URL or outside the folder.</summary>
        private string InsideFolder(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains("://")) return null;
            try
            {
                string root = Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string path = Path.GetFullPath(Path.Combine(Folder, name));
                return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>What identifies a design: its input values, in column order.</summary>
        public static string Key(IEnumerable<string> inputValues)
        {
            return string.Join("\u001f", inputValues);
        }

        /// <summary>
        /// The name a design's files are saved under, made of its input values: 12_2.5_HEA200.png.
        /// Characters a file name cannot hold become '-'. A name that would be very long is cut and
        /// ends in a hash of the full key instead, so two designs that share a long prefix still get
        /// files of their own.
        /// </summary>
        public static string FileStem(IList<string> inputValues)
        {
            const int MaxLength = 80;
            var parts = inputValues.Select(Sanitise).Where(part => part.Length > 0).ToList();
            string stem = parts.Count > 0 ? string.Join("_", parts) : "design";
            return stem.Length <= MaxLength ? stem : HashedStem(inputValues);
        }

        /// <summary>The file stem cut short if need be and ended with a hash of the design's key, which no other design shares.</summary>
        public static string HashedStem(IList<string> inputValues)
        {
            const int MaxLength = 80;
            var parts = inputValues.Select(Sanitise).Where(part => part.Length > 0).ToList();
            string stem = parts.Count > 0 ? string.Join("_", parts) : "design";
            if (stem.Length > MaxLength - 9) stem = stem.Substring(0, MaxLength - 9).TrimEnd('_', '-');

            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Utf8.GetBytes(Key(inputValues)));
                return stem + "_" + string.Concat(hash.Take(4).Select(b => b.ToString("x2")));
            }
        }

        private static string Sanitise(string value)
        {
            var text = new StringBuilder();
            foreach (char c in (value ?? "").Trim())
                text.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '-');
            return text.ToString().Trim('.');
        }

        /// <summary>
        /// One CSV line, RFC 4180: a cell holding a comma or a quote is quoted, and line breaks
        /// inside a cell become spaces so that one design is always one line.
        /// </summary>
        public static string Line(IEnumerable<string> cells)
        {
            return string.Join(",", cells.Select(Quote)) + "\n";
        }

        private static string Quote(string cell)
        {
            string text = (cell ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            if (text.IndexOfAny(new[] { ',', '"' }) < 0 && text.Trim() == text) return text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>The cells of one CSV line, quotes undone.</summary>
        public static List<string> ParseLine(string line)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
                else cell.Append(c);
            }
            cells.Add(cell.ToString());
            return cells;
        }
    }
}
