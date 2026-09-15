using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task CleanupSmoke(){
            string directory=Path.Combine(Program.Data,"cleanup-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var cutoff=DateTime.Now.AddDays(-3);string old=Path.Combine(directory,"old.tmp"),recent=Path.Combine(directory,"recent.tmp");File.WriteAllBytes(old,new byte[128]);File.WriteAllBytes(recent,new byte[256]);File.SetLastWriteTime(old,cutoff.AddDays(-1));File.SetLastWriteTime(recent,cutoff);
            var read=cleanupRead;var run=cleanupRun;int calls=0;
            try{
                var scanned=CleanupPreview.Scan(directory,cutoff,CancellationToken.None,100);Assert(scanned.Errors==0&&scanned.Files==1&&scanned.Bytes==128&&File.Exists(old)&&File.Exists(recent),"Cleanup preview age/size or read-only boundary failed");Assert(CleanupPreview.Scan(directory,cutoff,CancellationToken.None,1).Errors>0,"Limited scan reported complete result");Assert(CleanupPreview.Scan(Path.GetPathRoot(directory),cutoff,CancellationToken.None,100).Errors>0,"Drive-root cleanup accepted");Assert(CleanupPreview.Scan(Path.Combine(directory,"absent"),cutoff,CancellationToken.None,100).Files==0,"Absent cleanup directory invented files");
                var savedTemp=Environment.GetEnvironmentVariable("TEMP");var savedTmp=Environment.GetEnvironmentVariable("TMP");try{Environment.SetEnvironmentVariable("TEMP",directory);Environment.SetEnvironmentVariable("TMP",Path.Combine(directory,"different"));var actual=CleanupPreview.Read("CLN-USERTEMP",CancellationToken.None);Assert(actual.Source==directory&&actual.Files>=1&&actual.Errors==0,"Preview did not use cleanup engine TEMP source");}finally{Environment.SetEnvironmentVariable("TEMP",savedTemp);Environment.SetEnvironmentVariable("TMP",savedTmp);}
                ShowPage(11);integrityChoice.SelectedIndex=integrityActions.Length;Assert(cleanupPanel.Visibility==Visibility.Visible&&integrityReport.Visibility==Visibility.Collapsed,"Cleanup view did not replace integrity content");
                cleanupRead=(id,token)=>new CleanupEstimate{Source=directory,Files=2,Bytes=1048576,Captured=DateTime.Now};cleanupRun=(id,progress)=>{calls++;return Task.FromResult(new EngineResult{Code=0,Output="Тест: файлы не удалялись."});};await ScanCleanup();Assert(!cleanupApply.IsEnabled&&cleanupChecks[0].IsEnabled,"Scan selected files without user choice");cleanupChecks[0].IsChecked=true;RefreshCleanupEnabled();Assert(cleanupApply.IsEnabled,"Completed cleanup category cannot be selected");
                foreach(var size in new[]{new Size(1280,800),new Size(800,600)}){Window.Width=size.Width;Window.Height=size.Height;Window.UpdateLayout();cleanupPanel.ScrollToTop();Assert(cleanupPanel.ActualHeight>200&&cleanupPanel.ActualWidth>400,"Cleanup has no usable viewport");Capture(size.Width==800?"portable-ui-cleanup-compact.png":"portable-ui-cleanup.png");}
                var pending=ApplyCleanup();Assert(confirmation!=null,"Cleanup omitted irreversible-action confirmation");FinishConfirmation(false);await pending;Assert(calls==0&&cleanupEstimates[0]!=null,"Cancelled cleanup ran or invalidated preview");pending=ApplyCleanup();FinishConfirmation(true);await pending;Assert(calls==1&&cleanupEstimates[0]==null&&!cleanupApply.IsEnabled,"Cleanup did not invalidate consumed estimate");
                await ScanCleanup();cleanupChecks[0].IsChecked=cleanupChecks[1].IsChecked=true;cleanupRun=(id,progress)=>{calls++;return Task.FromResult(new EngineResult{Code=4,Output="fixture incomplete"});};pending=ApplyCleanup();FinishConfirmation(true);await pending;Assert(calls==2&&cleanupStatus.Text.Contains("не полностью"),"Failed cleanup continued with next category");ExpandOutput(false);
                cleanupRead=(id,token)=>new CleanupEstimate{Source=directory,Files=10,Bytes=1000,Errors=1,Error="fixture access denied"};await ScanCleanup();Assert(!cleanupChecks[0].IsEnabled&&!cleanupApply.IsEnabled&&cleanupValues[0].Text.Contains("неполный"),"Incomplete scan enabled deletion");
                cleanupRead=(id,token)=>{token.WaitHandle.WaitOne();token.ThrowIfCancellationRequested();return new CleanupEstimate();};var scanning=ScanCleanup();Assert(busy&&cleanupStop.IsEnabled,"Scan cannot be cancelled");cleanupStop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await scanning;Assert(!busy&&cleanupStatus.Text.Contains("остановлен")&&!cleanupApply.IsEnabled,"Cancelled scan retained unsafe selection");
                integrityChoice.SelectedIndex=0;Assert(cleanupPanel.Visibility==Visibility.Collapsed&&integrityReport.Visibility==Visibility.Visible,"Cannot return to integrity checks");
            }finally{cleanupRead=read;cleanupRun=run;integrityChoice.SelectedIndex=0;File.Delete(old);File.Delete(recent);Directory.Delete(directory);SetBusy(false);ShowPage(0);}
        }
    }
}
