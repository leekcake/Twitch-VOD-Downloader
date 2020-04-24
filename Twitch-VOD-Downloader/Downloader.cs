using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Twitch_VOD_Downloader
{
    public class Downloader
    {
        public class Chunk
        {
            public int chunkNum;
            public byte[] data;
        }

        public readonly string header, path, ffmpegArg;

        public int maxDownload = 15;
        public int maxChunkInMemory = 30;
        public bool isCopy = false;

        public List<Chunk> chunks = new List<Chunk>();

        public Process ffmpeg;

        private bool downloaderEnd = false;
        private bool uploaderEnd = false;

        private string lastFFmpegResult = "";
        private int chunkCount = 0;
        private int downloadedChunk = 0;
        private int pushedChunk = 0;

        public string StatusText
        {
            get
            {
                return $"D {downloadedChunk}/{chunkCount}, P {pushedChunk}/{chunkCount} # {lastFFmpegResult}";
            }
        }

        public bool IsFinished
        {
            get
            {
                return downloaderEnd && uploaderEnd;
            }
        }

        public Downloader(string header, string path, string ffmpegArg)
        {
            this.header = header;
            this.path = path;
            this.ffmpegArg = ffmpegArg;
        }

        public void Start()
        {
            new Task(() =>
            {
                DownloadThreadCmd();
            }).Start();
            new Task(() =>
            {
                PushThreadCmd();
            }).Start();
        }

        public async Task DownloadThreadCmd()
        {
            var client = new WebClient();
            var playlistRaw = await client.DownloadStringTaskAsync($"https://vod-secure.twitch.tv/{header}/chunked/index-dvr.m3u8");
            var playlist = playlistRaw.Split("\n");
            var chunkInx = new List<int>();
            foreach (var data in playlist)
            {
                if (data.EndsWith(".ts"))
                {
                    chunkInx.Add(int.Parse(data.Replace(".ts", "")));
                }
            }
            chunkCount = chunkInx.Count;

            var tasks = new List<Task>();

            while (true)
            {
                tasks.RemoveAll(t => t.IsCompleted);
                if (tasks.Count >= maxDownload || chunks.Count >= maxChunkInMemory)
                {
                    Thread.Sleep(1000);
                    continue;
                }
                if (tasks.Count == 0 && chunkInx.Count == 0)
                {
                    break;
                }
                if(chunkInx.Count == 0)
                {
                    continue;
                }

                var chunk = chunkInx[0];
                chunkInx.RemoveAt(0);
                tasks.Add(DownloadChunk(chunk));
            }
            downloaderEnd = true;
        }

        public async Task DownloadChunk(int chunkNum)
        {
            var client = new WebClient();
            var data = await client.DownloadDataTaskAsync($"https://vod-secure.twitch.tv/{header}/chunked/{chunkNum}.ts");
            var chunk = new Chunk();
            chunk.chunkNum = chunkNum;
            chunk.data = data;
            lock (chunks)
            {
                chunks.Add(chunk);
                downloadedChunk += 1;
            }
        }

        public async Task PushThreadCmd()
        {
            ffmpeg = new Process();
            ffmpeg.StartInfo.CreateNoWindow = true;
            ffmpeg.StartInfo.UseShellExecute = false;
            ffmpeg.StartInfo.RedirectStandardInput = true;
            ffmpeg.StartInfo.RedirectStandardError = true;
            ffmpeg.StartInfo.RedirectStandardOutput = true;
            ffmpeg.OutputDataReceived += Ffmpeg_DataReceived;
            ffmpeg.ErrorDataReceived += Ffmpeg_DataReceived;
            ffmpeg.StartInfo.FileName = "ffmpeg";
            ffmpeg.StartInfo.Arguments = $"-y -analyzeduration {1024 * 1024 * 300} -probesize {1024 * 1024 * 300} -i pipe: {ffmpegArg} \"{path}\"";
            ffmpeg.Start();
            ffmpeg.BeginErrorReadLine();
            ffmpeg.BeginOutputReadLine();

            int currentChunk = 0;
            while (true)
            {
                Chunk data = null;
                lock (chunks)
                {
                    foreach (var chunk in chunks)
                    {
                        if (chunk.chunkNum == currentChunk)
                        {
                            data = chunk;
                            pushedChunk += 1;
                        }
                    }

                    if (data != null)
                    {
                        chunks.Remove(data);
                        currentChunk = data.chunkNum + 1;
                    }
                    //Downloader is finished and no chunk, finished?
                    if (chunks.Count == 0 && downloaderEnd)
                    {
                        break;
                    }
                }
                if (data != null)
                {
                    await ffmpeg.StandardInput.BaseStream.WriteAsync(data.data);
                }
                else
                {
                    await Task.Delay(10);
                }
            }
            await ffmpeg.StandardInput.BaseStream.FlushAsync();
            ffmpeg.StandardInput.Close();
            ffmpeg.WaitForExit();
            uploaderEnd = true;
        }

        private void Ffmpeg_DataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            Debug.WriteLine(e.Data);
            lastFFmpegResult = e.Data.Replace("\r", "").Replace("\n", "");
        }
    }
}
