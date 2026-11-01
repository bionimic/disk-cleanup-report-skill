using System.Globalization;

namespace DiskCleanup.Services;

internal static class Format
{
    public static string Size(long bytes)
    {
        if (bytes < 0) bytes = 0;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var format = unit == 0 ? "0" : value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
