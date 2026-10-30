#if DEBUG
using System.Collections.Generic;
using System.Text;

namespace GwentAccess.Dev
{
    /// <summary>
    /// Ring buffer of everything the mod has spoken, so a dev driver that can't hear NVDA can read it back.
    /// Lines carry a stable increasing index; poll with a cursor to get only new ones. Ported from RTAccess.
    /// </summary>
    internal sealed class SpeechTap
    {
        private const int Capacity = 500;
        private readonly object _lock = new object();
        private readonly List<string> _lines = new List<string>();
        private long _base; // index of _lines[0]

        public void Add(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_lock)
            {
                _lines.Add(text);
                if (_lines.Count > Capacity) { _lines.RemoveAt(0); _base++; }
            }
        }

        /// <summary>"index: text" for every line at or after since; next is the cursor for the following call.</summary>
        public string Render(long since, out long next)
        {
            lock (_lock)
            {
                long end = _base + _lines.Count;
                if (since < _base) since = _base;
                var sb = new StringBuilder();
                for (long i = since; i < end; i++) sb.Append(i).Append(": ").Append(_lines[(int)(i - _base)]).Append('\n');
                next = end;
                return sb.ToString();
            }
        }
    }
}
#endif
