using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed class PingMeasurement {
        internal long? Milliseconds;
        internal string Status;
    }
    internal static class NetworkProbe {
        internal static bool ValidHost(string host){return !string.IsNullOrWhiteSpace(host)&&host.Length<=253&&Uri.CheckHostName(host)!=UriHostNameType.Unknown;}
        internal static async Task<IPAddress[]> Resolve(string host){
            if(!ValidHost(host))throw new ArgumentException("Введите IP-адрес или имя сервера без https://, порта и пути.");
            IPAddress literal;if(IPAddress.TryParse(host,out literal))return new[]{literal};
            var query=Dns.GetHostAddressesAsync(host);if(await Task.WhenAny(query,Task.Delay(5000))!=query){var observe=query.ContinueWith(t=>{var error=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);throw new TimeoutException("DNS не ответил за 5 секунд.");}
            var addresses=await query;if(addresses.Length==0)throw new InvalidOperationException("DNS не вернул адрес сервера.");return addresses;
        }
        internal static async Task<PingMeasurement> Send(IPAddress address){using(var ping=new Ping()){var reply=await ping.SendPingAsync(address,1200,new byte[32]);return new PingMeasurement{Milliseconds=reply.Status==IPStatus.Success?(long?)reply.RoundtripTime:null,Status=reply.Status==IPStatus.Success?"Ответ получен":reply.Status==IPStatus.TimedOut?"Нет ответа за 1,2 с":reply.Status.ToString()};}}
        internal static string Summary(IList<PingMeasurement> samples){
            if(samples.Count==0)return "Нет завершённых измерений.";
            var times=samples.Where(s=>s.Milliseconds.HasValue).Select(s=>s.Milliseconds.Value).ToArray();double loss=100.0*(samples.Count-times.Length)/samples.Count;
            if(times.Length==0)return "Ответов: 0 из "+samples.Count+". Отсутствие ICMP-ответов не доказывает отсутствие интернета: сервер или сеть могут блокировать ping.";
            double jitter=times.Zip(times.Skip(1),(a,b)=>(double)Math.Abs(a-b)).DefaultIfEmpty(0).Average();
            return "Ответов: "+times.Length+" из "+samples.Count+" · Без ответа: "+loss.ToString("N0")+" %\nЗадержка: минимум "+times.Min()+" мс · средняя "+times.Average().ToString("N1")+" мс · максимум "+times.Max()+" мс\nИзменчивость между полученными ответами: "+(times.Length>1?jitter.ToString("N1")+" мс":"недостаточно ответов")+".";
        }
    }
    internal sealed partial class MainWindow {
        private TextBox networkHost;
        private Button networkTestStart,networkTestStop;
        private TextBlock networkTestStatus,networkTestSummary,networkTestAdvice;
        private ItemsControl networkTestRows;
        private ProgressBar networkTestProgress;
        private bool probingNetwork,stopNetwork;
        private Func<string,Task<IPAddress[]>> resolveNetwork=NetworkProbe.Resolve;
        private Func<IPAddress,Task<PingMeasurement>> pingNetwork=NetworkProbe.Send;
        private void InitializeNetworkDiagnostics(){
            var panel=ToolPage("NetworkPage");panel.Children.Add(Paragraph("Проверка покажет, как быстро выбранный сервер отвечает и сколько запросов осталось без ответа. Отправляем 10 небольших ICMP-пакетов; настройки сети не меняются. Это не измерение скорости скачивания и не оценка игрового FPS."));
            panel.Children.Add(Paragraph("Адрес сервера или роутера — например, 1.1.1.1 или адрес игрового сервера. Внешний сервер увидит обычные сетевые запросы с вашего подключения."));
            var controls=new WrapPanel{Margin=new Thickness(0,0,0,12)};panel.Children.Add(controls);networkHost=new TextBox{Text="1.1.1.1",Width=260,Margin=new Thickness(0,0,10,8)};System.Windows.Automation.AutomationProperties.SetName(networkHost,"Адрес для проверки сети");controls.Children.Add(networkHost);networkTestStart=new Button{Content="Проверить соединение",Margin=new Thickness(0,0,10,8)};controls.Children.Add(networkTestStart);networkTestStop=new Button{Content="Остановить",IsEnabled=false,Margin=new Thickness(0,0,0,8)};controls.Children.Add(networkTestStop);
            networkTestProgress=new ProgressBar{Minimum=0,Maximum=10,Height=5,Margin=new Thickness(0,0,0,12)};networkTestProgress.SetResourceReference(Control.ForegroundProperty,"Accent");networkTestProgress.SetResourceReference(Control.BackgroundProperty,"Raised");panel.Children.Add(networkTestProgress);networkTestStatus=Paragraph("Проверка ещё не запускалась.");panel.Children.Add(networkTestStatus);
            var card=new Border{CornerRadius=new CornerRadius(12),Padding=new Thickness(18),Margin=new Thickness(0,0,0,14)};card.SetResourceReference(Border.BackgroundProperty,"Surface");networkTestSummary=Paragraph("Здесь появятся задержка, доля запросов без ответа и изменчивость задержки.");networkTestSummary.FontSize=18;networkTestSummary.Margin=new Thickness(0);card.Child=networkTestSummary;panel.Children.Add(card);
            networkTestAdvice=Paragraph("Сравнивайте замеры до и во время вашей обычной нагрузки. Если роутер отвечает стабильно, а внешний сервер — нет, проверьте другое направление: проблема может быть на маршруте или на сервере.");panel.Children.Add(networkTestAdvice);networkTestRows=new ItemsControl();panel.Children.Add(networkTestRows);networkTestStart.Click+=async(s,e)=>await ProbeNetwork();networkTestStop.Click+=(s,e)=>{stopNetwork=true;networkTestStop.IsEnabled=false;networkTestStatus.Text="Остановим после текущего запроса (DNS — до 5 с, ping — до 1,2 с).";};Window.Closed+=(s,e)=>stopNetwork=true;
        }
        private async Task ProbeNetwork(){
            if(probingNetwork)return;string host=networkHost.Text.Trim();if(!NetworkProbe.ValidHost(host)){networkTestStatus.Text="Введите IP-адрес или имя сервера без https://, порта и пути.";return;}
            probingNetwork=true;stopNetwork=false;networkHost.IsEnabled=networkTestStart.IsEnabled=false;networkTestStop.IsEnabled=true;networkTestProgress.Value=0;networkTestRows.ItemsSource=null;networkTestSummary.Text="Определяем адрес сервера…";networkTestStatus.Text="DNS: определяем адрес "+host+"…";var samples=new List<PingMeasurement>();var lines=new List<string>();
            try{var watch=Stopwatch.StartNew();var addresses=await resolveNetwork(host);watch.Stop();if(closed||stopNetwork)return;var address=addresses.FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetwork)??addresses.First();IPAddress literal;string dns=IPAddress.TryParse(host,out literal)?"Указан IP: DNS не требуется.":"Определение адреса: "+watch.ElapsedMilliseconds+" мс (включая локальный кэш).";
                for(int i=0;i<10&&!stopNetwork&&!closed;i++){networkTestStatus.Text=host+" → "+address+" · "+dns+" Запрос "+(i+1)+" из 10.";var sample=await pingNetwork(address);if(closed)return;samples.Add(sample);lines.Add((i+1)+". "+sample.Status+(sample.Milliseconds.HasValue?" · "+sample.Milliseconds.Value+" мс":""));networkTestRows.ItemsSource=lines.ToArray();networkTestProgress.Value=samples.Count;networkTestSummary.Text=NetworkProbe.Summary(samples);if(i<9&&!stopNetwork)await Task.Delay(200);}
                if(!closed)networkTestStatus.Text=(stopNetwork?"Проверка остановлена":"Проверка завершена")+" · "+host+" → "+address+". "+dns;
            }catch(Exception ex){if(!closed){networkTestStatus.Text="Проверка не завершена: "+ex.Message;networkTestSummary.Text=NetworkProbe.Summary(samples);}}
            finally{probingNetwork=false;if(!closed){networkHost.IsEnabled=networkTestStart.IsEnabled=true;networkTestStop.IsEnabled=false;if(stopNetwork){networkTestStatus.Text="Проверка остановлена. Завершённых измерений: "+samples.Count+".";networkTestSummary.Text=NetworkProbe.Summary(samples);}}}
        }
    }
}
