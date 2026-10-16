using System.Text.RegularExpressions;

namespace Heborian
{
    public static class HistoryPresentation
    {
        // ItemInfo can decorate locale names. Keep the audit archive unchanged,
        // but never expose its rich-text syntax or stat suffixes as a display name.
        public static string ItemName(string value)
        {
            string name=Regex.Replace(value ?? "", "<[^>]*>", "");
            name=Regex.Replace(name, @"\s*\[\d+\s*/\s*\d+\]", "");
            name=name.Replace("☆", "").Replace("★", "");
            return Regex.Replace(name, @"\s+", " ").Trim();
        }
    }
}
