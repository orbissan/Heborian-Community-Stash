namespace Heborian
{
    // Single version source: metadata, banner, client plugin, MSBuild and ZIP packaging.
    public static class BuildInfo
    {
        public const string Version = "1.10.1";
        public static string BannerTitle(string title) => title.TrimEnd() + " " + Version;
    }
}
