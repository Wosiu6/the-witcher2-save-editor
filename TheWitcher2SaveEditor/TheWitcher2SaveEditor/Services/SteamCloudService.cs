using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TheWitcher2SaveEditor.Services;

public static class SteamCloudService
{
    public static void UpdateRemoteCache(string savedFilePath)
    {
        var directory = Path.GetDirectoryName(savedFilePath);
        if (directory == null) return;

        var parentDir = Path.GetDirectoryName(directory);
        if (parentDir == null) return;

        var cacheFile = Path.Combine(parentDir, "remotecache.vdf");
        if (!File.Exists(cacheFile)) return;

        var fileName = Path.GetFileName(savedFilePath);
        var fileBytes = File.ReadAllBytes(savedFilePath);
        var fileSize = fileBytes.Length;
        var sha1 = ComputeSha1(fileBytes);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var content = File.ReadAllText(cacheFile, Encoding.UTF8);
        var updatedContent = UpdateEntryInVdf(content, fileName, fileSize, sha1, timestamp);

        if (updatedContent != null)
        {
            File.WriteAllText(cacheFile, updatedContent, Encoding.UTF8);
        }
        else
        {
            var newEntry = CreateVdfEntry(fileName, fileSize, sha1, timestamp);
            content = InsertEntryInVdf(content, newEntry);
            File.WriteAllText(cacheFile, content, Encoding.UTF8);
        }
    }

    public static bool IsInSteamRemoteFolder(string filePath)
    {
        var normalized = filePath.Replace('/', '\\');
        return normalized.Contains(@"\Steam\userdata\", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains(@"\remote\", StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha1(byte[] data)
    {
        var hash = SHA1.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    private static string? UpdateEntryInVdf(string content, string fileName, long size, string sha1, long timestamp)
    {
        // Match the entry block for this filename
        // Format: "\tfilename"\n\t{\n\t\t...\n\t}
        var escapedName = Regex.Escape($"\"{fileName}\"");
        var pattern = $@"(\t{escapedName}\s*\r?\n\t\{{\s*\r?\n)([\s\S]*?)(\r?\n\t\}})";
        var match = Regex.Match(content, pattern);

        if (!match.Success) return null;

        var innerContent = match.Groups[2].Value;

        // Update size
        innerContent = Regex.Replace(innerContent,
            @"(""size""\s+)""[^""]*""",
            $@"$1""{size}""");

        // Update localtime
        innerContent = Regex.Replace(innerContent,
            @"(""localtime""\s+)""[^""]*""",
            $@"$1""{timestamp}""");

        // Update time
        innerContent = Regex.Replace(innerContent,
            @"(""time""\s+)""[^""]*""",
            $@"$1""{timestamp}""");

        // Update sha
        innerContent = Regex.Replace(innerContent,
            @"(""sha""\s+)""[^""]*""",
            $@"$1""{sha1}""");

        // Set syncstate to 2 (modified locally, needs upload)
        innerContent = Regex.Replace(innerContent,
            @"(""syncstate""\s+)""[^""]*""",
            @"$1""2""");

        // Set persiststate to 0
        innerContent = Regex.Replace(innerContent,
            @"(""persiststate""\s+)""[^""]*""",
            @"$1""0""");

        return content.Substring(0, match.Groups[2].Index)
            + innerContent
            + content.Substring(match.Groups[2].Index + match.Groups[2].Length);
    }

    private static string CreateVdfEntry(string fileName, long size, string sha1, long timestamp)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\t\"{fileName}\"");
        sb.AppendLine("\t{");
        sb.AppendLine($"\t\t\"root\"\t\t\"0\"");
        sb.AppendLine($"\t\t\"size\"\t\t\"{size}\"");
        sb.AppendLine($"\t\t\"localtime\"\t\t\"{timestamp}\"");
        sb.AppendLine($"\t\t\"time\"\t\t\"{timestamp}\"");
        sb.AppendLine($"\t\t\"remotetime\"\t\t\"0\"");
        sb.AppendLine($"\t\t\"sha\"\t\t\"{sha1}\"");
        sb.AppendLine($"\t\t\"syncstate\"\t\t\"2\"");
        sb.AppendLine($"\t\t\"persiststate\"\t\t\"0\"");
        sb.AppendLine($"\t\t\"platformstosync2\"\t\t\"-1\"");
        sb.AppendLine("\t}");
        return sb.ToString();
    }

    private static string InsertEntryInVdf(string content, string newEntry)
    {
        // Insert before the closing brace of the root object
        var lastBrace = content.LastIndexOf('}');
        if (lastBrace < 0) return content;
        return content.Substring(0, lastBrace) + newEntry + "\n" + content.Substring(lastBrace);
    }
}
