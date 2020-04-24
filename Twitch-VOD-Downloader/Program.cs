using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Twitch_VOD_Downloader
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Give max download count, no input to default value(15)");
            var maxDownloadLine = Console.ReadLine();
            int maxDownload = 15;
            if(maxDownloadLine != "")
            {
                maxDownload = int.Parse(maxDownloadLine);
            }

            Console.WriteLine("Give max count of chunk in memory, no input to default value(30)");
            var maxChunkInMemoryLine = Console.ReadLine();
            int maxChunkInMemory = 30;
            if (maxChunkInMemoryLine != "")
            {
                maxChunkInMemory = int.Parse(maxChunkInMemoryLine);
            }

            Console.WriteLine("Give thumbnail link of want to download!");
            var link = Console.ReadLine();

            //https://d2nvs31859zcd8.cloudfront.net/28dd4643c2928e7ed5cc_lilac_unicorn__37692897216_1424638597/storyboards/600030923-strip-0.jpg
            //https://static-cdn.jtvnw.net/cf_vods/d2nvs31859zcd8/2688ba222d79d5181615_lilac_unicorn__37625411648_1420413817/thumb/thumb0-320x180.jpg
            var split = link.Split('/');
            var header = split[split.Length - 3];

            Console.WriteLine("Detected Header: " + header);

            var path = "";
            if (File.Exists("path.txt"))
            {
                path = File.ReadAllText("path.txt");
            }
            else
            {
                Console.WriteLine("Give directory that saving VOD");
                Console.WriteLine("You can save path into 'path.txt' for skip this");
                path = Console.ReadLine();
            }

            var ffmpegArg = "";
            if (File.Exists("encode.txt"))
            {
                ffmpegArg = File.ReadAllText("encode.txt").Trim();
            }
            else
            {
                Console.WriteLine("No encoding option, use direct stream copy.");
                Console.WriteLine("You can encoding option into 'encode.txt' for custom ffmpeg encoding option");
                ffmpegArg = "-c:v copy";
            }

            var downloader = new Downloader(header, Path.Combine(path, header + ".mp4"), ffmpegArg);
            downloader.maxDownload = maxDownload;
            downloader.maxChunkInMemory = maxChunkInMemory;
            downloader.Start();

            Stopwatch watch = new Stopwatch();
            watch.Start();
            while (!downloader.IsFinished)
            {
                Console.WriteLine(downloader.StatusText.Replace("\r", "").Replace("\n", ""));
                Thread.Sleep(1000);
            }
            watch.Stop();
            Console.WriteLine("Downloaded, elapsed time: " + watch.Elapsed);
        }
    }
}
