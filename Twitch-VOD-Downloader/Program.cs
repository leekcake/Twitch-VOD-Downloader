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
            Console.WriteLine("Enter max download count, no input will default to 15");
            var maxDownloadLine = Console.ReadLine();
            int maxDownload = 15;
            if(maxDownloadLine != "")
            {
                maxDownload = int.Parse(maxDownloadLine);
            }

            Console.WriteLine("Enter max amount of chunks in memory, no input will default to 15");
            var maxChunkInMemoryLine = Console.ReadLine();
            int maxChunkInMemory = 30;
            if (maxChunkInMemoryLine != "")
            {
                maxChunkInMemory = int.Parse(maxChunkInMemoryLine);
            }

            Console.WriteLine("Enter the thumbnail link of the VOD you want to download");
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
                Console.WriteLine("Enter the directory to save the VOD to");
                Console.WriteLine("You can save a path into 'path.txt' to skip this");
                path = Console.ReadLine();
            }

            var ffmpegArg = "";
            if (File.Exists("encode.txt"))
            {
                ffmpegArg = File.ReadAllText("encode.txt").Trim();
            }
            else
            {
                Console.WriteLine("No encoding options set, using direct stream copy.");
                Console.WriteLine("You can set custom ffmpeg encoding options in 'encode.txt'");
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
