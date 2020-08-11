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
        public readonly string[] proxy;
        private bool[] proxyAlive;
        private int[] proxyRecheck;

        public int maxDownload = 15;
        public int maxChunkInMemory = 30;
        public bool isCopy = false;

        public List<Chunk> chunks = new List<Chunk>();

        public Process ffmpeg;

        private bool downloaderEnd = false;
        private bool uploaderEnd = false;

        private string lastFFmpegResult = "";
        private int currentProxyInx = 0;
        private async Task<WebClient> NewClient()
        {
            var client = new WebClient();

            if (proxy != null)
            {
                int inx;
                while (true)
                {
                    inx = currentProxyInx++;
                    if (currentProxyInx >= proxy.Length)
                    {
                        currentProxyInx = 0;
                    }
                    var to = proxy[inx];
                    if (to != "direct")
                    {
                        WebProxy wp = new WebProxy(to);
                        client.Proxy = wp;
                    } else
                    {
                        client.Proxy = null;
                    }

                    if (proxyRecheck[inx] <= 0)
                    {
                        try
                        {
                            //Fast service provider for alive check.
                            await client.DownloadStringTaskAsync("http://1.1.1.1");
                            proxyAlive[inx] = true;
                        }
                        catch
                        {
                            proxyAlive[inx] = false;
                        }
                        proxyRecheck[inx] = 100;
                    }
                    else
                    {
                        proxyRecheck[inx] -= 1;
                    }

                    if(proxyAlive[inx])
                    {
                        break;
                    }
                }
            }

            client.Headers.Add("Referer", "https://www.twitch.tv/");
            client.Headers.Add("Origin", "https://www.twitch.tv/");
            client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:81.0) Gecko/20100101 Firefox/81.0");
            return client;
        }

        public int ChunkCount { get; private set; } = 0;

        public int DownloadedChunk { get; private set; } = 0;

        public int PushedChunk { get; private set; } = 0;

        public string StatusText
        {
            get
            {
                return $"D {DownloadedChunk}/{ChunkCount}, P {PushedChunk}/{ChunkCount} # {lastFFmpegResult}";
            }
        }

        public bool IsFinished
        {
            get
            {
                return downloaderEnd && uploaderEnd;
            }
        }

        public Downloader(string header, string path, string ffmpegArg, string[] proxy = null)
        {
            this.header = header;
            this.path = path;
            this.ffmpegArg = ffmpegArg;
            this.proxy = proxy;
            this.proxyAlive = new bool[proxy.Length];
            this.proxyRecheck = new int[proxy.Length];
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
            var client = await NewClient();
            string playlistRaw;
            try
            {
                playlistRaw = await client.DownloadStringTaskAsync($"https://vod-secure.twitch.tv/{header}/chunked/index-dvr.m3u8");
            }
            catch (Exception ex)
            {
                chunks.Clear();
                downloaderEnd = true;
                return;
            }
            client.Dispose();
            var playlist = playlistRaw.Split("\n");
            var chunkInx = new List<int>();
            foreach (var data in playlist)
            {
                if (data.EndsWith(".ts"))
                {
                    chunkInx.Add(int.Parse(data.Replace(".ts", "")));
                }
            }
            ChunkCount = chunkInx.Count;

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
                if (chunkInx.Count == 0)
                {
                    continue;
                }

                var chunk = chunkInx[0];
                chunkInx.RemoveAt(0);
                tasks.Add(DownloadChunk(chunk));
            }
            downloaderEnd = true;
        }

        public async Task DownloadChunk(int chunkNum, int retryCount = 0)
        {
            byte[] data;
            var client = await NewClient();
            try
            {
                try
                {
                    data = await client.DownloadDataTaskAsync($"https://vod-secure.twitch.tv/{header}/chunked/{chunkNum}.ts");
                }
                catch
                {
                    data = await client.DownloadDataTaskAsync($"https://vod-secure.twitch.tv/{header}/chunked/{chunkNum}-muted.ts");
                }
            }
            catch (WebException ex)
            {
                Debug.WriteLine(ex.Message);
                Debug.WriteLine(ex.StackTrace);
                await Task.Delay(1000 * retryCount);
                if(retryCount > 5)
                {
                    retryCount = 5;
                }
                _ = Task.Run(async () =>
                {
                    await DownloadChunk(chunkNum, retryCount++);
                });
                client.Dispose();
                return;
            }
            client.Dispose();
            var chunk = new Chunk();
            chunk.chunkNum = chunkNum;
            chunk.data = data;
            lock (chunks)
            {
                chunks.Add(chunk);
                DownloadedChunk += 1;
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
                            PushedChunk += 1;
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
                    await ffmpeg.StandardInput.BaseStream.FlushAsync();
                }
                else
                {
                    await Task.Delay(1);
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
