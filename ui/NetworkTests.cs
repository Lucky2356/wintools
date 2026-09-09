using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task NetworkSmoke(){
            Assert(NetworkProbe.ValidHost("127.0.0.1")&&NetworkProbe.ValidHost("example.com")&&!NetworkProbe.ValidHost("https://example.com/test")&&!NetworkProbe.ValidHost("example.com & whoami"),"Network host validation failed");
            var samples=new List<PingMeasurement>{new PingMeasurement{Milliseconds=10},new PingMeasurement{Milliseconds=null},new PingMeasurement{Milliseconds=30}};
            Assert(NetworkProbe.Summary(samples).Contains("33")&&NetworkProbe.Summary(samples).Contains("20"),"Network summary arithmetic failed");
            Assert(NetworkProbe.Summary(new[]{new PingMeasurement()}).Contains("не доказывает"),"Blocked ICMP reported as broken internet");
            var localAddresses=await NetworkProbe.Resolve("127.0.0.1");Assert(localAddresses.Length==1&&localAddresses[0].Equals(IPAddress.Loopback),"Literal network address incorrectly resolved");var localReply=await NetworkProbe.Send(IPAddress.Loopback);Assert(localReply.Milliseconds.HasValue,"Local ICMP transport failed");
            var originalResolve=resolveNetwork;var originalPing=pingNetwork;int calls=0;
            try{
                resolveNetwork=host=>Task.FromResult(new[]{IPAddress.Loopback});pingNetwork=address=>Task.FromResult(new PingMeasurement{Milliseconds=++calls==3?(long?)null:10+calls,Status=calls==3?"Нет ответа за 1,2 с":"Ответ получен"});networkHost.Text="127.0.0.1";ShowPage(10);await ProbeNetwork();Assert(calls==10&&networkTestProgress.Value==10&&networkTestSummary.Text.Contains("9 из 10")&&!probingNetwork,"Network sequence failed");
                Window.Width=1280;Window.Height=800;Window.UpdateLayout();await Task.Delay(100);Capture("portable-ui-network.png");
                calls=0;pingNetwork=address=>{calls++;stopNetwork=true;return Task.FromResult(new PingMeasurement{Milliseconds=5,Status="Ответ получен"});};await ProbeNetwork();Assert(calls==1&&!networkTestStop.IsEnabled&&networkTestStart.IsEnabled,"Network stop ignored");
                resolveNetwork=host=>{throw new InvalidOperationException("fixture DNS failure");};await ProbeNetwork();Assert(networkTestStatus.Text.Contains("fixture DNS failure")&&networkTestSummary.Text.Contains("Нет завершённых измерений"),"DNS failure misreported as packet loss");
            }finally{resolveNetwork=originalResolve;pingNetwork=originalPing;networkHost.Text="1.1.1.1";}
            ShowPage(0);
        }
    }
}
