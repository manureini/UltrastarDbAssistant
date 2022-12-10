using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using VideoLibrary;

namespace UltrastarDbAssistant
{
    internal class Program
    {
        private const string TMP_DIR = "tmp";
        private const string FINISH_DIR = "done";
        private const string FFMPEG_EXE = "ffmpeg.exe";
        private const string FFMPEG_DOWNLOAD_URL = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

        private const string YOUTUBE_VIDEO_URL_PREFIX = "https://www.youtube.com/watch?v=";

        static async Task Main(string[] args)
        {
            if (Directory.Exists(TMP_DIR))
                Directory.Delete(TMP_DIR, true);

            Directory.CreateDirectory(TMP_DIR);

            if (!File.Exists(FFMPEG_EXE))
            {
                await DownloadFfmpegAsync();
            }

            int songId = int.Parse(args[0]);

            await DownloadSong(songId);

            Directory.Delete(TMP_DIR, true);

            Console.WriteLine("Finished processing");
        }

        private static async Task DownloadSong(int pSongId)
        {
            var (cover, youtube, songInfoTitle) = await GetSongInfo(pSongId);

            Console.WriteLine();
            Console.WriteLine(songInfoTitle);
            Console.WriteLine();

            var videoTask = DownloadVideoAsync(youtube);
            var audioTask = DownloadAudioAsync(youtube);

            var (content, _) = await GetSong(pSongId);

            string artist = content.Substring(content.IndexOf("#ARTIST") + 8, content.IndexOf("#", content.IndexOf("#ARTIST") + 1) - (content.IndexOf("#ARTIST") + 8)).Trim();
            string title = content.Substring(content.IndexOf("#TITLE") + 7, content.IndexOf("#", content.IndexOf("#TITLE") + 1) - (content.IndexOf("#TITLE") + 7)).Trim();

            var artistTitle = artist + " - " + title;

            var songDir = Path.Combine(TMP_DIR, artistTitle);

            Directory.CreateDirectory(songDir);

            var contentFileName = removeIllegalChars(artistTitle + ".txt");
            var coverFileName = artistTitle + Path.GetExtension(cover);

            content = content.Replace("#COVER: ", "#COVER:" + coverFileName);

            HttpClient httpClient = new HttpClient();
            var coverData = await httpClient.GetByteArrayAsync(cover);

            File.WriteAllBytes(Path.Combine(songDir, coverFileName), coverData);

            var (videoTmpFilename, videoExtension) = videoTask.Result;

            var videoFileName = artistTitle + videoExtension;

            File.Move(videoTmpFilename, Path.Combine(songDir, videoFileName));

            content = content.Replace("#VIDEO: ", "#VIDEO:" + videoFileName);

            var audioTmpFileName = audioTask.Result;

            var mp3FileName = artistTitle + ".mp3";

            File.Move(audioTmpFileName, Path.Combine(songDir, mp3FileName));

            content = content.Replace("#MP3: ", "#MP3:" + mp3FileName);

            File.WriteAllText(Path.Combine(songDir, contentFileName), content);

            if (!Directory.Exists(FINISH_DIR))
                Directory.CreateDirectory(FINISH_DIR);

            var finishDefaultDir = songDir.Replace(TMP_DIR, FINISH_DIR);
            var finishDir = finishDefaultDir;

            int dirCount = 1;

            while (Directory.Exists(finishDir))
            {
                finishDir = finishDefaultDir + "_" + dirCount;
                dirCount++;
            }

            Directory.Move(songDir, finishDir);
        }

        private static async Task<(string content, string filename)> GetSong(int pSongId)
        {
            Console.WriteLine("Start song content download...");

            var baseUri = new Uri("https://usdb.eu/");

            var cookieContainer = new CookieContainer();
            using var handler = new HttpClientHandler() { CookieContainer = cookieContainer };
            using var httpClient = new HttpClient(handler) { BaseAddress = baseUri };

            var response = await httpClient.PostAsync("https://usdb.eu/download", null);

            await Task.Delay(TimeSpan.FromSeconds(22));

            var sessionCookie = cookieContainer.GetCookies(baseUri).First(c => c.Name == "PHPSESSID");

            httpClient.DefaultRequestHeaders.Add("cookie", $"PHPSESSID={sessionCookie.Value}");

            var resp = await httpClient.GetAsync("https://usdb.eu/download/" + pSongId);

            var content = await resp.Content.ReadAsStringAsync();

            var contentDisposition = resp.Content.Headers.First(h => h.Key == "Content-Disposition").Value.First();

            var filename = contentDisposition.Replace("attachment; filename=", string.Empty).Trim();

            Console.WriteLine("Finished song content download");

            return (content, filename);
        }

        private static async Task<(string cover, string youtube, string title)> GetSongInfo(int pSongId)
        {
            using var httpClient = new HttpClient();

            var response = await httpClient.GetAsync("https://player.usdb.eu/2.0.0/json.php?songFile=" + pSongId);

            var jsoncontent = await response.Content.ReadAsStringAsync();

            jsoncontent = jsoncontent[1..^1];

            var songInfo = JsonSerializer.Deserialize<SongInfoJson>(jsoncontent)!;

            return (songInfo.title.link, songInfo.youtube, songInfo.artist.label + " - " + songInfo.title.label);
        }

        private static async Task<(string tmpFilename, string extension)> DownloadVideoAsync(string pYoutubeVideoId)
        {
            Console.WriteLine("Start video download...");

            var videos = await YouTube.Default.GetAllVideosAsync(YOUTUBE_VIDEO_URL_PREFIX + pYoutubeVideoId);
            var video = videos.OrderByDescending(v => v.Resolution).First();

            var filename = Path.Combine(TMP_DIR, pYoutubeVideoId + ".video.tmp");

            var data = await video.GetBytesAsync();

            await File.WriteAllBytesAsync(filename, data);

            Console.WriteLine("Finished video download");

            return (filename, video.FileExtension);
        }

        private static async Task<string> DownloadAudioAsync(string pYoutubeVideoId)
        {
            Console.WriteLine("Start audio download...");

            var videos = await YouTube.Default.GetAllVideosAsync(YOUTUBE_VIDEO_URL_PREFIX + pYoutubeVideoId);
            var video = videos.OrderByDescending(v => v.AudioBitrate).First();

            var filename = Path.Combine(TMP_DIR, pYoutubeVideoId + ".audiovideo.tmp");

            var data = await video.GetBytesAsync();

            await File.WriteAllBytesAsync(filename, data);

            Console.WriteLine("Finished audio download");

            Console.WriteLine("Start audio convert...");

            var soundFileName = Path.Combine(TMP_DIR, pYoutubeVideoId + ".mp3");

            var ffmpegArgs = $"-i \"{filename}\" \"{soundFileName}\"";

            var startInfo = new ProcessStartInfo();
            startInfo.FileName = FFMPEG_EXE;
            startInfo.Arguments = ffmpegArgs;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            var process = new Process();
            process.StartInfo = startInfo;
            process.Start();

            await process.WaitForExitAsync();

            File.Delete(filename);

            Console.WriteLine("Finished Audio convert");

            return soundFileName;
        }

        private static async Task DownloadFfmpegAsync()
        {
            Console.WriteLine("Download ffmpeg...");

            var httpClient = new HttpClient();
            var data = await httpClient.GetByteArrayAsync(FFMPEG_DOWNLOAD_URL);

            var ffmpegTmpFile = Path.Combine(TMP_DIR, "ffmpeg.zip");
            File.WriteAllBytes(ffmpegTmpFile, data);

            ZipFile.ExtractToDirectory(ffmpegTmpFile, TMP_DIR, true);

            var ffmpegPath = Directory.GetFiles(TMP_DIR, FFMPEG_EXE, SearchOption.AllDirectories).First();

            File.Move(ffmpegPath, FFMPEG_EXE);

            Console.WriteLine("Finished downloading ffmpeg...");
        }

        public static string removeIllegalChars(string stringWithIllegalChars)
        {
            stringWithIllegalChars = stringWithIllegalChars.Replace("\\", " ");
            stringWithIllegalChars = stringWithIllegalChars.Replace("/", " ");
            stringWithIllegalChars = stringWithIllegalChars.Replace("\"", " ");
            stringWithIllegalChars = stringWithIllegalChars.Replace("*", " ");
            stringWithIllegalChars = stringWithIllegalChars.Replace(":", " ");
            stringWithIllegalChars = stringWithIllegalChars.Replace("?", " ");
            return stringWithIllegalChars;
        }

        public class SongInfoJson
        {
            public string copyright { get; set; }
            public int version { get; set; }
            public string link { get; set; }
            public Title title { get; set; }
            public Artist artist { get; set; }
            public string youtube { get; set; }
            public int gap { get; set; }

            public class Title
            {
                public string label { get; set; }
                public string link { get; set; }
            }

            public class Artist
            {
                public string label { get; set; }
                public string link { get; set; }
            }
        }
    }
}