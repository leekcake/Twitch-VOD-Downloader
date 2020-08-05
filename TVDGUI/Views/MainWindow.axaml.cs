using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Utils;
using Avalonia.Markup.Xaml;
using HtmlAgilityPack;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TVDGUI.Models;
using Twitch_VOD_Downloader;

namespace TVDGUI.Views
{
    public class MainWindow : Window
    {
        private ChromeDriver crawler;

        private TextBox MaxDownloadTextBox;
        private TextBox MaxChunkTextBox;
        private TextBox PathTextBox;
        private TextBox QueryIdTextBox;

        private TextBlock fetchListCountTextBlock;
        private TextBlock StatusTextBlock;

        private Button fetchListReloadButton;
        private Button FindPathButton;
        private Button QueryIdButton;
        private Button QueryListButton;

        private Button DownloadButton;
        private ProgressBar AllDownloadProgressBar;
        private ProgressBar CurDownloadProgressBar;
        private TextBlock DownloadStatusTextBlock;

        private ItemsControl VODList;

        private List<string> fetchList = new List<string>();

        public ObservableCollection<VODData> VODDatas = new ObservableCollection<VODData>();
        private string ffmpegArg;

        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif

            var chromeOptions = new ChromeOptions();
            chromeOptions.AddArguments("--headless");
            crawler = new ChromeDriver(chromeOptions);

            MaxDownloadTextBox = this.FindControl<TextBox>("MaxDownloadTextBox");
            MaxChunkTextBox = this.FindControl<TextBox>("MaxChunkTextBox");
            PathTextBox = this.FindControl<TextBox>("PathTextBox");
            QueryIdTextBox = this.FindControl<TextBox>("QueryIdTextBox");

            FindPathButton = this.FindControl<Button>("FindPathButton");
            FindPathButton.Click += FindPathButton_Click;

            fetchListCountTextBlock = this.FindControl<TextBlock>("fetchListCountTextBlock");
            fetchListReloadButton = this.FindControl<Button>("fetchListReloadButton");
            ReadFetchList();
            fetchListReloadButton.Click += FetchListReloadButton_Click;

            QueryIdButton = this.FindControl<Button>("QueryIdButton");
            QueryIdButton.Click += QueryIdButton_Click;

            QueryListButton = this.FindControl<Button>("QueryListButton");
            QueryListButton.Click += QueryListButton_Click;

            StatusTextBlock = this.FindControl<TextBlock>("StatusTextBlock");

            VODList = this.FindControl<ItemsControl>("VODList");
            VODList.Items = VODDatas;

            DownloadButton = this.FindControl<Button>("DownloadButton");
            DownloadButton.Click += DownloadButton_Click;

            AllDownloadProgressBar = this.FindControl<ProgressBar>("AllDownloadProgressBar");
            CurDownloadProgressBar = this.FindControl<ProgressBar>("CurDownloadProgressBar");
            DownloadStatusTextBlock = this.FindControl<TextBlock>("DownloadStatusTextBlock");
            

            /*
            MaxDownloadTextBox.GetObservable(TextBox.TextProperty).Subscribe(text =>
            {
                
            });
            */

            ffmpegArg = "";
            if (File.Exists("encode.txt"))
            {
                ffmpegArg = File.ReadAllText("encode.txt").Trim();
            }
            else
            {
                ffmpegArg = "-c:v copy";
            }

            Closing += MainWindow_Closing;
        }

        private void SetInteractive(bool active)
        {
            DownloadButton.IsEnabled = active;
            VODList.IsEnabled = active;

            MaxDownloadTextBox.IsEnabled = active;
            MaxChunkTextBox.IsEnabled = active;
            PathTextBox.IsEnabled = active;
            QueryIdTextBox.IsEnabled = active;

            fetchListReloadButton.IsEnabled = active;
            FindPathButton.IsEnabled = active;
            QueryIdButton.IsEnabled = active;
            QueryListButton.IsEnabled = active;
        }

        private async void DownloadButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if( !Directory.Exists(PathTextBox.Text) )
            {
                return;
            }

            SetInteractive(false);
            await DownloadQueueAll(PathTextBox.Text);
        }

        public async Task DownloadQueueAll(string path)
        {
            var list = new List<VODData>(VODDatas.Count);
            foreach(var item in VODDatas)
            {
                if(item.DownloadIt)
                {
                    list.Add(item);
                }
            }

            var maxDownload = int.Parse(MaxDownloadTextBox.Text);
            var maxChunk = int.Parse(MaxChunkTextBox.Text);

            AllDownloadProgressBar.Maximum = list.Count;
            CurDownloadProgressBar.Maximum = 1;

            for(int i = 0; i < list.Count; i++)
            {
                var data = list[i];
                var status = new Action<string>((message) =>
                {
                    DownloadStatusTextBlock.Text = $"다운로드: {message} [{data.Summary}]";
                });
                status("시작됨");

                var root = Path.Combine(path, data.StreamerId);
                if(!Directory.Exists(root))
                {
                    Directory.CreateDirectory(root);
                }

                var downTo = Path.Combine(root, $"Fin_{data.StreamerId}_" + Regex.Replace(data.BroadcastDate, "[^0-9.]", "") + "-TVD.mp4");

                var downloader = new Downloader(data.VODHeader, downTo, ffmpegArg);
                downloader.maxDownload = maxDownload;
                downloader.maxChunkInMemory = maxChunk;
                downloader.Start();

                while (!downloader.IsFinished)
                {
                    status($"Delayed chunk count: {downloader.DownloadedChunk - downloader.PushedChunk}");
                    if (downloader.ChunkCount == 0)
                    {
                        CurDownloadProgressBar.Value = 0;
                    }
                    else
                    {
                        CurDownloadProgressBar.Value = downloader.PushedChunk / (double) downloader.ChunkCount;
                    }
                    AllDownloadProgressBar.Value = i + CurDownloadProgressBar.Value;
                    await Task.Delay(10);
                }

                status("완료, 다음 작업 확인중");
                AllDownloadProgressBar.Value = i + 1;
            }
        }

        private async void QueryListButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            QueryListButton.IsEnabled = false;
            foreach(var id in fetchList)
            {
                await ParseId(id);
            }
            StatusTextBlock.Text = "Crawl list complete";
            QueryListButton.IsEnabled = true;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            crawler.Close();
        }

        private async void QueryIdButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            QueryIdButton.IsEnabled = false;
            await ParseId(QueryIdTextBox.Text);
            QueryIdButton.IsEnabled = true;
        }

        private async Task ParseId(string id)
        {
            //https://www.twitch.tv/roong__/videos?filter=archives&sort=time            
            var url = $"https://www.twitch.tv/{id}/videos?filter=archives&sort=time";
            StatusTextBlock.Text = $"Crawling: {id}";
            await Task.Delay(100);
            crawler.Navigate().GoToUrl(url);
            await Task.Delay(2000);
            int lastTry = -1;
            while(true)
            {
                if(lastTry == crawler.FindElementsByClassName("preview-card-thumbnail__image").Count)
                {
                    break;
                }
                lastTry = crawler.FindElementsByClassName("preview-card-thumbnail__image").Count;
                ((IJavaScriptExecutor)crawler).ExecuteScript("window.scrollTo(0, document.body.scrollHeight)");
                await Task.Delay(1000);
            }
            foreach (var node in crawler.FindElementsByClassName("preview-card-thumbnail__image"))
            {
                var image = node.FindElement(By.ClassName("tw-image"));

                var title = image.GetAttribute("title");
                var src = image.GetAttribute("src");
                var alt = image.GetAttribute("alt");

                if(src.Contains("processing"))
                {
                    continue;
                }

                Debug.WriteLine($"{title} / {src}");

                var data = new VODData();
                data.BroadcastDate = title;
                data.StreamerId = id;
                data.BroadcastTitle = alt;
                data.ThumbnailURL = src;
                data.DownloadIt = true;

                VODDatas.Add(data);
            }
            StatusTextBlock.Text = $"Crawled: {id}";
        }

        private void FetchListReloadButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            ReadFetchList();
        }

        public void ReadFetchList()
        {
            fetchList.Clear();

            try
            {
                var reader = new StreamReader(new FileStream("fetchList.txt", FileMode.OpenOrCreate));

                string line = reader.ReadLine();
                while (line != null)
                {
                    var trim = line.Trim();
                    if (trim.Length == 0 || trim.StartsWith("#"))
                    {
                        line = reader.ReadLine();
                        continue;
                    }
                    if(trim.Contains("-"))
                    {
                        trim = trim.Split("-")[0].Trim();
                    }
                    fetchList.Add(trim);
                    line = reader.ReadLine();
                }

                reader.Close();
                fetchListCountTextBlock.Text = $"{fetchList.Count} streamers will queried. (Check fetchList.txt)";
            }
            catch
            {
                fetchListCountTextBlock.Text = $"error streamers will queried. (Check fetchList.txt)";
            }
        }

        private async void FindPathButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            var path = await dialog.ShowAsync(this);
            PathTextBox.Text = path;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
