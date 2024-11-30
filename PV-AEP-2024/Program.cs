using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace PV_AEP_2024
{
    internal class Program
    {
        private static readonly List<string> countyCodes =
        [
            "ab", "ar", "ag", "bc", "bh", "bn", "br", "bt", "bv", "bz",
            "cs", "cl", "cj", "ct", "cv", "db", "dj", "gl", "gr", "gj",
            "hr", "hd", "il", "is", "if", "mm", "mh", "ms", "nt", "ot",
            "ph", "sj", "sm", "sb", "sv", "tr", "tm", "tl", "vs", "vl",
            "vn", "s1", "s2", "s3", "s4", "s5", "s6", "sr"
        ];
        private static readonly string baseURL = "https://prezenta.roaep.ro/prezidentiale24112024/";
        private static readonly string rootFolder = @"C:\USR\AEP";
        private static readonly string destinationFolder = @"C:\USR\AEP-clean";

        static async Task Main(string[] args)
        {
            //await GetPDFsFromAEP();

            CopyAndRenameFiles();
        }

        private static async Task GetPDFsFromAEP()
        {
            var skip = 0;
            var countiesToProcess = countyCodes.Skip(skip);

            foreach (var countyCode in countiesToProcess)
            {
                var saveDirectory = $@"{rootFolder}\{countyCode}";
                Directory.CreateDirectory(saveDirectory);

                var jsonUrl = $"https://prezenta.roaep.ro/prezidentiale24112024/data/json/sicpv/pv/pv_{countyCode}.json";
                using var client = new HttpClient();
                var response = await client.GetAsync(jsonUrl);

                var json = await response.Content.ReadAsStringAsync();
                var urls = GetUrlsForCounty(json);

                var scannedUrls = urls.Where(x => x.Contains("scnnd")).ToList();

                await DownloadFilesAsync(scannedUrls, saveDirectory, TimeSpan.FromMilliseconds(150), maxRetries: 3);
            }
        }

        private static List<string> GetUrlsForCounty(string json)
        {
            JObject parsedJson = JObject.Parse(json);

            // Extract URLs
            var urls = parsedJson["scopes"]!
                .SelectTokens("..files..url")
                .Select(url => url.ToString())
                .ToList();

            return urls;
        }

        private static async Task DownloadFilesAsync(List<string> urls, string saveDirectory, TimeSpan throttleDelay, int maxRetries)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36");

            foreach (var url in urls)
            {
                var fullUrl = $"{baseURL}{url}";
                var fileName = Path.Combine(saveDirectory, Path.GetFileName(new Uri(fullUrl).LocalPath));

                bool success = false;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        Console.WriteLine($"Downloading {fullUrl} (Attempt {attempt}/{maxRetries})...");
                        var response = await httpClient.GetAsync(fullUrl);
                        response.EnsureSuccessStatusCode();

                        await using var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None);
                        await response.Content.CopyToAsync(fileStream);

                        Console.WriteLine($"Downloaded: {fileName}");
                        success = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error downloading {fullUrl}: {ex.Message}");
                        if (attempt == maxRetries)
                        {
                            Console.WriteLine($"Failed to download {fullUrl} after {maxRetries} attempts.");
                        }
                    }
                }

                if (!success) continue;

                // Throttle to avoid overwhelming the server
                Console.WriteLine("Throttling...");
                await Task.Delay(throttleDelay);
            }
        }

        public static void CopyAndRenameFiles()
        {
            // Ensure the destination folder exists
            Directory.CreateDirectory(destinationFolder);

            // Regex to extract the number before "scnnd"
            Regex regex = new Regex(@"_([a-z0-9]{2})_\d+_(\d+)_scnnd", RegexOptions.IgnoreCase);

            // Process all files in subdirectories
            foreach (string filePath in Directory.GetFiles(rootFolder, "*.pdf", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(filePath);
                Match match = regex.Match(fileName);


                if (match.Success)
                {
                    string code = match.Groups[1].Value; // Extract the two-letter code
                    string number = match.Groups[2].Value.PadLeft(4, '0'); // Extract and pad the number

                    // Get relative path of the current file's directory
                    string relativePath = Path.GetRelativePath(rootFolder, Path.GetDirectoryName(filePath)!);
                    string destinationSubfolder = Path.Combine(destinationFolder, relativePath);

                    // Ensure destination subfolder exists
                    Directory.CreateDirectory(destinationSubfolder);

                    // Build the destination file path
                    string newFileName = $"{code}_{number}.pdf";
                    string destinationFilePath = Path.Combine(destinationSubfolder, newFileName);

                    // Copy the file to the destination with the new name
                    File.Copy(filePath, destinationFilePath, true);

                    Console.WriteLine(newFileName);
                }
            }
        }
    }
}
