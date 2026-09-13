namespace JLS.Services
{
    public static class PathHelper
    {
        public static string GetSafePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            if (path.Length >= 260 && !path.StartsWith(@"\\?\"))
            {
                if (path.StartsWith(@"\\")) return @"\\?\UNC\" + path.Substring(2);
                return @"\\?\" + path;
            }
            return path;
        }

        public static string GetExplorerSafePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            if (path.StartsWith(@"\\?\UNC\")) return @"\\" + path.Substring(8);
            if (path.StartsWith(@"\\?\")) return path.Substring(4);

            return path;
        }
    }
}