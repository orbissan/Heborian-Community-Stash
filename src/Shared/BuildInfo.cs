namespace Heborian
{
    // Single version source: metadata, banner, client plugin, MSBuild and ZIP packaging.
    public static class BuildInfo
    {
        public const string Version = "1.12.7";
        public static string BannerTitle(string title) => title.TrimEnd() + " " + Version;
    }
}
