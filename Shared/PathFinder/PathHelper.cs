using System;
using System.IO;

namespace LoadGenTool.Shared.PathFinder
{
    public static class PathHelper
    {
        public static string FindRootFolder()
        {
            string targetFolder = "LoadGenTool";
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);

            while (current != null && !current.Name.Equals(targetFolder, StringComparison.OrdinalIgnoreCase))
            {
                current = current.Parent;
            }

            if (current == null || !current.Name.Equals(targetFolder, StringComparison.OrdinalIgnoreCase))
            {
                throw new DirectoryNotFoundException(
                    $"Could not locate the '{targetFolder}' folder in the directory hierarchy starting from '{AppContext.BaseDirectory}'.");
            }

            return current.FullName;
        }
    }
}