using System;
using System.Collections.Generic;
using System.Linq;

namespace Alpaca4d.Interop
{
    public enum ReportLevel
    {
        /// <summary>Something with a structural effect was not converted: the model is not the source's.</summary>
        Error,

        /// <summary>Converted, but only approximately.</summary>
        Warning,

        /// <summary>Left out or changed without a structural effect, or worth knowing when comparing results.</summary>
        Remark,
    }

    public class ReportEntry
    {
        public ReportLevel Level { get; }
        public string Text { get; }
        public IReadOnlyList<string> Subjects { get; }

        public ReportEntry(ReportLevel level, string text, IReadOnlyList<string> subjects)
        {
            Level = level;
            Text = text;
            Subjects = subjects;
        }

        /// <summary>How many subjects a message names before it says "and n more".</summary>
        public const int ListedSubjects = 5;

        /// <summary>The text, followed by the elements, sections or loads it concerns.</summary>
        public string Message
        {
            get
            {
                if (Subjects.Count == 0)
                    return Text;

                var listed = string.Join(", ", Subjects.Take(ListedSubjects));
                int more = Subjects.Count - ListedSubjects;
                return more > 0
                    ? $"{Text} ({Subjects.Count}: {listed} and {more} more)"
                    : $"{Text} ({listed})";
            }
        }

        public override string ToString() => $"{Level}: {Message}";
    }

    /// <summary>
    /// What a conversion could not do, collected so that a hundred beams with the same problem make
    /// one message naming a few of them rather than a hundred balloons on the component.
    /// </summary>
    public class ConversionReport
    {
        private readonly List<ReportEntry> _entries = new List<ReportEntry>();
        private readonly Dictionary<(ReportLevel, string), List<string>> _subjects = new Dictionary<(ReportLevel, string), List<string>>();

        /// <summary>
        /// Records <paramref name="text"/> once, however often it is added; each call's
        /// <paramref name="subject"/> - an element id, a section name - is added to the list it names.
        /// </summary>
        public void Add(ReportLevel level, string text, string subject = null)
        {
            var key = (level, text);
            if (!_subjects.TryGetValue(key, out var subjects))
            {
                subjects = new List<string>();
                _subjects[key] = subjects;
                _entries.Add(new ReportEntry(level, text, subjects));
            }

            if (!string.IsNullOrWhiteSpace(subject) && !subjects.Contains(subject))
                subjects.Add(subject);
        }

        public void Error(string text, string subject = null) => Add(ReportLevel.Error, text, subject);
        public void Warning(string text, string subject = null) => Add(ReportLevel.Warning, text, subject);
        public void Remark(string text, string subject = null) => Add(ReportLevel.Remark, text, subject);

        public IReadOnlyList<ReportEntry> Entries => _entries;

        public bool HasErrors => _entries.Any(entry => entry.Level == ReportLevel.Error);

        /// <summary>Every entry as one line, errors first.</summary>
        public List<string> Lines()
        {
            return _entries
                .OrderBy(entry => entry.Level)
                .Select(entry => entry.ToString())
                .ToList();
        }
    }
}
