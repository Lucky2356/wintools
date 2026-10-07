using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task UpdateRecheckSmoke(){
            Assert(ValidBuild(CurrentWindowsBuild())&&!ValidBuild("26100")&&!ValidBuild("1.2.3")&&!ValidBuild(null),"Windows build format incorrect");
            var build=windowsBuild;var verify=updateVerification;var savedBuild=preferences.WindowsBuild;var savedVerify=preferences.VerifyAfterUpdates;var plan=preferences.Plan.ToList();int checks=0;string current="26100.1";
            string journal=Path.Combine(Program.Data,"state","applied.dat");if(File.Exists(journal))throw new IOException("Update recheck smoke requires isolated journal.");
            try{
                windowsBuild=()=>current;updateVerification=()=>{checks++;verificationDrift=new[]{"UI-FILEEXT","UNKNOWN-ID"};return Task.FromResult(0);};
                preferences.WindowsBuild=null;await CheckAfterWindowsUpdate();Assert(checks==0&&preferences.WindowsBuild=="26100.1"&&Preferences.Load().WindowsBuild=="26100.1","First start ran a check or lost the build");
                current="26100.2";await CheckAfterWindowsUpdate();Assert(checks==0&&preferences.WindowsBuild=="26100.2","Check ran without applied settings");
                File.WriteAllText(journal,"");current="26100.3";
                SetBusy(true);await CheckAfterWindowsUpdate();SetBusy(false);Assert(checks==0&&preferences.WindowsBuild=="26100.2","Busy check skipped but remembered the new build");
                await CheckAfterWindowsUpdate();Assert(checks==1&&preferences.WindowsBuild=="26100.3"&&page==7&&Get<TextBlock>("Status").Text.Contains("Изменились настройки: 2"),"Drift after Windows update not reported");
                preferences.Plan.Clear();AddDriftToPlan();Assert(preferences.Plan.SequenceEqual(new[]{"UI-FILEEXT"})&&page==4,"Drifted settings not added to plan");
                preferences.VerifyAfterUpdates=false;current="26100.4";await CheckAfterWindowsUpdate();Assert(checks==1&&preferences.WindowsBuild=="26100.4","Disabled update check still ran");
                Assert(Get<CheckBox>("VerifyAfterUpdates")!=null,"Update check option missing from settings");
            }finally{if(File.Exists(journal))File.Delete(journal);windowsBuild=build;updateVerification=verify;verificationDrift=new string[0];preferences.WindowsBuild=savedBuild;preferences.VerifyAfterUpdates=savedVerify;preferences.Plan=plan;SavePreferences();RefreshPlan();Text("Status","Готово к работе");}
            ShowPage(0);
        }
    }
}
