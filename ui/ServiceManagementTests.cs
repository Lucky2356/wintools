using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.CSharp;

namespace Wintools {
    internal static class ServiceManagementTests {
        private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        internal static void Run(){
            Assert(Program.Hosted,"Hosted runner required.");
            string name="WintoolsFixture"+Guid.NewGuid().ToString("N"),dependent=name+"Child";
            string directory=Path.Combine(Program.Data,"runtime","service-fixture"),exe=Path.Combine(directory,"fixture.exe");Directory.CreateDirectory(directory);
            using(var compiler=new CSharpCodeProvider()){var options=new CompilerParameters(new[]{"System.dll","System.ServiceProcess.dll"},exe){GenerateExecutable=true};var result=compiler.CompileAssemblyFromSource(options,"using System.ServiceProcess; public sealed class Fixture : ServiceBase { public Fixture(string name){ServiceName=name;CanStop=true;} public static void Main(string[] args){ServiceBase.Run(new Fixture(args[0]));} }");Assert(!result.Errors.HasErrors,"Service fixture compilation failed");}
            try{
                Create(name,exe,null);Create(dependent,exe,name);
                var before=ServiceActions.Inspect(name);Assert(before.Mode=="Manual"&&before.State=="Stopped","Fixture initial state incorrect");
                var delayed=Change(name,"delayed",null,0);Assert(ServiceActions.Inspect(name).Mode=="Auto"&&ServiceActions.Inspect(name).Delayed,"Delayed automatic start was not applied");
                Change(name,"restore",delayed,0);Assert(ServiceActions.Inspect(name).Mode=="Manual"&&ServiceActions.Read(delayed).Status=="REVERTED","Service mode restore failed");
                Change(dependent,"start",null,0);Assert(ServiceActions.Inspect(name).State=="Running","Dependency did not start");Change(name,"stop",null,4);Assert(ServiceActions.Inspect(name).State=="Running"&&ServiceActions.Inspect(dependent).State=="Running","Dependent services stopped implicitly");
                var stopped=Change(dependent,"stop",null,0);Assert(ServiceActions.Inspect(dependent).State=="Stopped","Service stop failed");Change(dependent,"restore",stopped,0);Assert(ServiceActions.Inspect(dependent).State=="Running","Service state restore failed");
                Change(dependent,"stop",null,0);Change(name,"stop",null,0);
                string otherSid="S-1-0-0";bool rejected=false;try{ServiceActions.Worker(new[]{"--service-worker","start",Convert.ToBase64String(Encoding.UTF8.GetBytes(name)),Guid.NewGuid().ToString("N"),otherSid,"-"});}catch(ArgumentException){rejected=true;}Assert(rejected,"Service worker accepted another user");Assert(ServiceActions.Inspect(name).State=="Stopped","Rejected service request mutated state");
            }finally{Delete(dependent);Delete(name);}
        }
        private static string Change(string name,string action,string restore,int expected){string id=Guid.NewGuid().ToString("N");int result=ServiceActions.Worker(new[]{"--service-worker",action,Convert.ToBase64String(Encoding.UTF8.GetBytes(name)),id,WindowsIdentity.GetCurrent().User.Value,restore??"-"});if(result!=expected)throw new Exception("Service action "+action+" returned "+result+": "+File.ReadAllText(Path.Combine(Program.Data,"runtime",id+".service.log")));return id;}
        private static void Create(string name,string exe,string dependency){using(var type=new ManagementClass("Win32_Service"))using(var input=type.GetMethodParameters("Create")){input["Name"]=name;input["DisplayName"]=name;input["PathName"]="\""+exe+"\" "+name;input["ServiceType"]=16;input["ErrorControl"]=1;input["StartMode"]="Manual";input["DesktopInteract"]=false;if(dependency!=null)input["ServiceDependencies"]=new[]{dependency};using(var result=type.InvokeMethod("Create",input,null))Assert(Convert.ToUInt32(result["ReturnValue"])==0,"Could not create test service");}}
        private static void Delete(string name){try{using(var service=new ManagementObject("Win32_Service.Name='"+name+"'")){service.InvokeMethod("StopService",null);service.InvokeMethod("Delete",null);}}catch(ManagementException){} }
    }
    internal sealed partial class MainWindow {
        private async Task ServiceManagementSmoke(){
            Assert(ServiceActions.ValidName("Example Service_123")&&!ServiceActions.ValidName("../service")&&!ServiceActions.ValidName("bad\nname"),"Service name validation failed");
            Assert(!ServiceActions.Manageable(new ServiceSnapshot{Kind="Kernel Driver",State="Stopped",Mode="Manual"}),"Driver management accepted");
            var real=await Task.Run(()=>ServiceActions.Inspect("EventLog"));Assert(real.Name=="EventLog"&&!string.IsNullOrEmpty(real.Mode),"Read-only service configuration failed");
            var inspect=serviceInspect;var latest=serviceLatest;var run=serviceRun;int calls=0;
            try{
                var fixture=new ServiceSnapshot{Name="WintoolsFixture",Label="Тестовая служба",Description="Проверка элементов управления без изменения Windows.",State="Running",Mode="Manual",Kind="Own Process",CanStop=true};serviceInspect=name=>fixture;serviceLatest=name=>null;serviceRun=(name,action,id)=>{calls++;return Task.FromResult(new EngineResult{Code=0,Output="Тест: действие службы не запускалось."});};services=new[]{new ServiceState{Name=fixture.Name,Label=fixture.Label,State=fixture.State,Mode=fixture.Mode}};FilterServices();serviceList.SelectedIndex=0;await ReadServiceSelection();ShowPage(5);Assert(serviceStop.IsEnabled&&serviceRestart.IsEnabled&&!serviceStart.IsEnabled,"Service controls do not reflect state");
                SetBusy(true);Assert(!serviceStop.IsEnabled&&!serviceModeApply.IsEnabled,"Service controls ignored operation lock");SetBusy(false);var pending=ChangeSelectedService("stop");Assert(confirmation!=null,"Service change was not confirmed");FinishConfirmation(false);await pending;Assert(calls==0,"Cancelled service action executed");
                Text("Status","Готово к работе");serviceStatus.Text="Тест интерфейса: одна служба, команды изменения не выполняются.";Window.Width=1280;Window.Height=800;Window.UpdateLayout();await Task.Delay(100);Capture("portable-ui-service-management.png");Window.Width=800;Window.Height=600;Window.UpdateLayout();await Task.Delay(100);Assert(serviceList.ActualHeight>80,"Service controls consumed the list viewport");Capture("portable-ui-service-management-compact.png");
                fixture.Mode="Disabled";RefreshServiceControls();Assert(!serviceRestart.IsEnabled,"Restart allowed a disabled service");
                fixture.Mode="Manual";string recordId=Guid.NewGuid().ToString("N"),historyDirectory=Path.Combine(Program.Data,"service-history"),historyPath=Path.Combine(historyDirectory,recordId+".json");Directory.CreateDirectory(historyDirectory);
                try{var record=new ServiceChange{Schema="wintools/service-change/1",Id=recordId,Name=fixture.Name,Action="manual",Before=fixture,After=fixture,TimeUtc=DateTime.UtcNow.ToString("o"),Status="OK"};File.WriteAllText(historyPath,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(record));ReadHistory();var row=Get<ListBox>("History").Items.Cast<HistoryRow>().Single(r=>r.Run==recordId);Assert(row.ServiceName==fixture.Name&&row.CanRevert,"Service change missing from shared history");var restore=RestoreServiceHistory(row);FinishConfirmation(true);await restore;Assert(calls==1,"History did not route service rollback to service worker");File.WriteAllText(historyPath,"{broken");ReadHistory();Assert(Get<ListBox>("History").Items.Count==0&&Get<TextBlock>("HistoryStatus").Text.Contains("недоступен"),"Corrupt service history escaped validation");}finally{File.Delete(historyPath);ReadHistory();}
            }finally{serviceInspect=inspect;serviceLatest=latest;serviceRun=run;}
            serviceList.SelectedItem=null;await RefreshServices();ShowPage(0);
        }
    }
}
