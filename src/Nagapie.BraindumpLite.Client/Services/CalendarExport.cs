using System.Text;
namespace Nagapie.BraindumpLite.Client.Services;
public static class CalendarExport
{
    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n").Replace(";", "\\;").Replace(",", "\\,");
    public static string Create(string title, string category, DateTime start, DateTime end, bool allDay)
    {
        if (end <= start) throw new ArgumentException("End must follow start.");
        var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Nagapie//Braindump Lite//EN", "CALSCALE:GREGORIAN", "BEGIN:VEVENT", $"UID:{Guid.NewGuid()}@nagapie", $"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}" };
        if (allDay) { lines.Add($"DTSTART;VALUE=DATE:{start:yyyyMMdd}"); lines.Add($"DTEND;VALUE=DATE:{end:yyyyMMdd}"); }
        else { lines.Add($"DTSTART:{start.ToUniversalTime():yyyyMMddTHHmmssZ}"); lines.Add($"DTEND:{end.ToUniversalTime():yyyyMMddTHHmmssZ}"); }
        lines.Add("SUMMARY:" + Escape(title)); lines.Add("DESCRIPTION:" + Escape(category)); lines.Add("END:VEVENT"); lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines.Select(Fold)) + "\r\n";
    }
    private static string Fold(string line)
    {
        var result = new StringBuilder(); int bytes = 0;
        foreach (var rune in line.EnumerateRunes()) {
            if (bytes + rune.Utf8SequenceLength > 75) { result.Append("\r\n "); bytes = 1; }
            result.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }
}
