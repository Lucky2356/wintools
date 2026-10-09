using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed class LoadLatencyResult
    {
        internal long? IdleMs, LoadedMs;
        internal int LoadedLost, LoadedSent;
        internal double Mbps;
    }

    // Latency under load ("bufferbloat"): the delay to a server while the line is busy downloading.
    // A router with a large buffer keeps it low when idle and lets it jump during downloads, which breaks calls and games.
    internal static class LoadLatency
    {
        internal const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=200000000";

        internal static async Task<LoadLatencyResult> Measure(IPAddress target, Func<IPAddress, Task<PingMeasurement>> ping, Func<CancellationToken, Action<long>, Task> download, TimeSpan duration, Action<string> progress)
        {
            var idle = new List<long>();
            for (int i = 0; i < 5; i++)
            {
                var sample = await ping(target);
                if (sample.Milliseconds.HasValue)
                    idle.Add(sample.Milliseconds.Value);
                await Task.Delay(200);
            }

            progress(Lang.T("Скачиваем тестовый файл и измеряем задержку…"));
            long bytes = 0;
            var loaded = new List<long>();
            int sent = 0;
            var watch = Stopwatch.StartNew();
            using (var stop = new CancellationTokenSource(duration))
            {
                var transfer = download(stop.Token, count => Interlocked.Add(ref bytes, count));
                // The first second is the download speeding up; the delay is measured once the line is full.
                await Task.WhenAny(transfer, Task.Delay(1000));
                while (!transfer.IsCompleted && !stop.IsCancellationRequested)
                {
                    var sample = await ping(target);
                    sent++;
                    if (sample.Milliseconds.HasValue)
                        loaded.Add(sample.Milliseconds.Value);
                    await Task.WhenAny(transfer, Task.Delay(250));
                }

                stop.Cancel();
                try
                {
                    await transfer;
                }
                catch (OperationCanceledException)
                {
                }
            }

            var seconds = Math.Max(0.5, watch.Elapsed.TotalSeconds);
            return new LoadLatencyResult
            {
                IdleMs = DnsBenchmark.Median(idle),
                LoadedMs = DnsBenchmark.Median(loaded),
                LoadedSent = sent,
                LoadedLost = sent - loaded.Count,
                Mbps = Interlocked.Read(ref bytes) * 8 / seconds / 1000000
            };
        }

        internal static async Task Download(CancellationToken token, Action<long> received)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            using (var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var buffer = new byte[65536];
                    int count;
                    while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        received(count);
                }
            }
        }

        internal static string Verdict(LoadLatencyResult result, out string tone)
        {
            tone = "Muted";
            if (!result.IdleMs.HasValue)
                return Lang.T("Сервер не ответил даже без нагрузки. Выберите другой адрес выше.");
            if (!result.LoadedMs.HasValue)
            {
                tone = "Danger";
                return Lang.T("Под нагрузкой сервер перестал отвечать: соединение перегружается.");
            }

            long growth = Math.Max(0, result.LoadedMs.Value - result.IdleMs.Value);
            string numbers = Lang.T("Без нагрузки ") + result.IdleMs + Lang.T(" мс, во время скачивания ") + result.LoadedMs + Lang.T(" мс (+") + growth + Lang.T(" мс). Скорость скачивания около ") + result.Mbps.ToString("N0", Lang.Culture) + Lang.T(" Мбит/с.");
            if (growth < 30 && result.LoadedLost == 0)
            {
                tone = "Success";
                return Lang.T("Отлично: задержка почти не растёт. ") + numbers;
            }

            if (growth < 100)
            {
                tone = "Warning";
                return Lang.T("Задержка заметно растёт, когда кто-то скачивает. Звонки и игры могут подтормаживать. ") + numbers;
            }

            tone = "Danger";
            return Lang.T("Задержка сильно растёт под нагрузкой (bufferbloat). Помогает включить в роутере SQM, Smart Queue или QoS. ") + numbers;
        }
    }

    internal sealed partial class MainWindow
    {
        private Button loadLatencyStart;
        private TextBlock loadLatencyResult;
        private bool measuringLoad;
        private Func<CancellationToken, Action<long>, Task> loadDownload = LoadLatency.Download;
        private TimeSpan loadDuration = TimeSpan.FromSeconds(10);

        private void InitializeLoadLatency(Panel parent)
        {
            var title = Paragraph(Lang.T("Задержка под нагрузкой"));
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(0, 12, 0, 4);
            parent.Children.Add(title);
            var about = Paragraph(Lang.T("Если во время скачивания или видеозвонка у других всё тормозит, проверьте, как растёт задержка под нагрузкой. Wintools 10 секунд скачивает тестовый файл с speed.cloudflare.com (до 200 МБ трафика) и в это время проверяет адрес выше."));
            about.FontSize = 12;
            parent.Children.Add(about);
            loadLatencyStart = new Button { Content = Lang.T("Проверить под нагрузкой"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 8) };
            loadLatencyStart.Click += async (s, e) => await MeasureLoadLatency();
            parent.Children.Add(loadLatencyStart);
            loadLatencyResult = Paragraph("");
            loadLatencyResult.Visibility = Visibility.Collapsed;
            parent.Children.Add(loadLatencyResult);
        }

        private async Task MeasureLoadLatency()
        {
            if (measuringLoad || probingNetwork)
                return;
            measuringLoad = true;
            loadLatencyStart.IsEnabled = networkTestStart.IsEnabled = networkHost.IsEnabled = false;
            loadLatencyResult.Visibility = Visibility.Visible;
            loadLatencyResult.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            loadLatencyResult.Text = Lang.T("Измеряем задержку без нагрузки…");
            try
            {
                var address = (await resolveNetwork(networkHost.Text.Trim())).First();
                var result = await LoadLatency.Measure(address, pingNetwork, loadDownload, loadDuration, text => loadLatencyResult.Text = text);
                if (closed)
                    return;
                string tone;
                loadLatencyResult.Text = LoadLatency.Verdict(result, out tone);
                loadLatencyResult.SetResourceReference(TextBlock.ForegroundProperty, tone == "Muted" ? "Text" : tone);
            }
            catch (Exception ex)
            {
                loadLatencyResult.Text = Lang.T("Проверка не завершена: ") + (ex is HttpRequestException ? Lang.T("тестовый файл не скачивается. ") : "") + ex.GetBaseException().Message;
            }
            finally
            {
                measuringLoad = false;
                if (!closed)
                    loadLatencyStart.IsEnabled = networkTestStart.IsEnabled = networkHost.IsEnabled = true;
            }
        }
    }
}
