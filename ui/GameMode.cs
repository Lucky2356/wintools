using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    // What the game mode changed, kept on disk so a crash or a power cut cannot leave the PC in it unnoticed.
    internal sealed class GameSession
    {
        public string Schema = "wintools/game-mode/1";
        public string Name, ProcessRecord, PowerRecord, PowerBefore, PowerTarget;
        public uint PriorityBefore;
        public int Pid;
        public long Started;
    }

    internal static class GameModeState
    {
        // Ultimate and High performance, in order of preference; laptops with Modern Standby often have neither.
        internal static readonly string[] FastPlans = { "e9a42b02-d5df-448d-aa00-03f14749eb61", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" };

        internal static string Path
        {
            get
            {
                return System.IO.Path.Combine(Program.Data, "state", "game-mode.json");
            }
        }

        internal static string FastPlan(PowerSnapshot snapshot)
        {
            return snapshot == null ? null : FastPlans.FirstOrDefault(id => snapshot.Plans.Any(p => p.Id == id));
        }

        internal static GameSession Load()
        {
            try
            {
                if (!File.Exists(Path) || new FileInfo(Path).Length > 8192)
                    return null;
                var session = new JavaScriptSerializer().Deserialize<GameSession>(File.ReadAllText(Path));
                return session != null && session.Schema == "wintools/game-mode/1" && session.Pid > 4 && !string.IsNullOrEmpty(session.Name) ? session : null;
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)
                    return null;
                throw;
            }
        }

        internal static void Save(GameSession session)
        {
            Program.SafeDirectory(System.IO.Path.GetDirectoryName(Path));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllText(Path, new JavaScriptSerializer().Serialize(session));
        }

        internal static void Clear()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
    }

    internal sealed partial class MainWindow
    {
        private GameSession gameMode;
        private Button gameModeButton;
        private TextBlock gameModeStatus;
        private bool closingAfterGame;

        private void InitializeGameMode(Panel parent)
        {
            var title = Paragraph(Lang.T("Игровой режим"));
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(0, 6, 0, 4);
            parent.Children.Add(title);
            gameModeStatus = Paragraph(Lang.T("Для выбранной игры: высокий приоритет, схема питания «Высокая производительность» и пауза фоновых проверок Wintools. Всё возвращается кнопкой или при закрытии Wintools."));
            gameModeStatus.FontSize = 12;
            parent.Children.Add(gameModeStatus);
            gameModeButton = new Button { Content = Lang.T("Включить игровой режим"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 14) };
            gameModeButton.Click += async (s, e) =>
            {
                if (gameMode == null)
                    await StartGameMode();
                else
                    await StopGameMode();
            };
            parent.Children.Add(gameModeButton);
            // Closing Wintools ends the game mode first; the window closes once everything is back.
            Window.Closing += async (s, e) =>
            {
                if (gameMode == null || closingAfterGame || e.Cancel || busy || downloading)
                    return;
                e.Cancel = true;
                closingAfterGame = true;
                await StopGameMode();
                if (gameMode == null)
                    Window.Close();
                else
                    closingAfterGame = false;
            };
        }

        private void RefreshGameMode()
        {
            if (gameModeButton == null)
                return;
            var row = processList.SelectedItem as ProcessRow;
            gameModeButton.Content = gameMode == null ? Lang.T("Включить игровой режим") : Lang.T("Выключить игровой режим");
            gameModeButton.IsEnabled = !busy && (gameMode != null || row != null && processSettings != null && !readingProcessSettings);
            if (gameMode != null)
                gameModeStatus.Text = Lang.T("Включён для «") + gameMode.Name + "» · PID " + gameMode.Pid + Lang.T(". Фоновые проверки Wintools приостановлены. Выключите режим после игры — или просто закройте Wintools.");
        }

        private async Task StartGameMode()
        {
            var row = processList.SelectedItem as ProcessRow;
            var before = processSettings;
            if (busy || gameMode != null || row == null || before == null)
                return;
            var readPower = powerRead;
            PowerSnapshot snapshot;
            try
            {
                snapshot = await Task.Run(() => readPower());
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception))
                    throw;
                snapshot = null;
            }

            var fast = GameModeState.FastPlan(snapshot);
            if (!await Confirm(Lang.T("Включить игровой режим для «") + row.Name + "» · PID " + row.Id + "?\n\n" + Lang.T("• приоритет процесса «Высокий» — остальные программы могут работать медленнее;\n") + (fast == null ? Lang.T("• схемы «Высокая производительность» на этом ПК нет — питание не меняется;\n") : fast == snapshot.Active ? Lang.T("• схема «Высокая производительность» уже используется;\n") : Lang.T("• схема питания «Высокая производительность» — Windows попросит подтверждение; ноутбук будет греться сильнее;\n")) + Lang.T("• фоновые проверки Wintools на паузе.\n\nВсё вернётся кнопкой «Выключить», при закрытии Wintools, а после сбоя Wintools предложит вернуть настройки при запуске.")))
                return;
            var session = new GameSession { Name = row.Name, Pid = row.Id, Started = row.Started };
            gameMode = session;
            GameModeState.Save(session);
            if (before.Priority != 128)
            {
                await RunProcessChange(row, before, "priority", 128, null);
                if (processSettings != null && processSettings.Id == row.Id && processSettings.Priority == 128)
                {
                    session.PriorityBefore = before.Priority;
                    var record = Latest(ProcessActions.History, r => r.Pid == row.Id && r.Started == row.Started && r.Action == "priority" && !r.Restore && r.Status == "OK" && r.After == "128");
                    session.ProcessRecord = record == null ? null : record.Id;
                    GameModeState.Save(session);
                }
            }

            if (fast != null && fast != snapshot.Active)
            {
                await RunPowerChange(fast, snapshot.Active, null);
                if (powerSnapshot != null && powerSnapshot.Active == fast)
                {
                    session.PowerBefore = snapshot.Active;
                    session.PowerTarget = fast;
                    var record = Latest(PowerActions.History, r => r.Action == "select" && r.Target == fast && r.Before == snapshot.Active && r.Status == "OK");
                    session.PowerRecord = record == null ? null : record.Id;
                }
            }

            GameModeState.Save(session);
            ServiceVisibility();
            Text("Status", Lang.T("Игровой режим включён для «") + row.Name + "».");
            RefreshGameMode();
        }

        // Returns only what the game mode itself changed and is still in place; a later change by the person stays.
        private async Task StopGameMode()
        {
            var session = gameMode ?? GameModeState.Load();
            if (busy || session == null)
                return;
            var notes = new System.Collections.Generic.List<string>();
            if (session.PriorityBefore != 0)
            {
                try
                {
                    var inspect = processInspect;
                    var now = await Task.Run(() => inspect(session.Pid, session.Started));
                    if (now.Priority == 128)
                        await RunProcessChange(new ProcessRow { Id = session.Pid, Started = session.Started, Name = session.Name }, now, "priority", session.PriorityBefore, Reverting(session.ProcessRecord, id => ProcessActions.Read(id).Status == "OK"));
                }
                catch (Exception ex)
                {
                    // The game has usually exited by now, and its priority went with it.
                    if (!(ex is IOException || ex is InvalidOperationException || ex is ArgumentException || ex is System.ComponentModel.Win32Exception))
                        throw;
                    notes.Add(Lang.T("игра уже закрыта, приоритет возвращать не нужно"));
                }
            }

            bool powerBack = true;
            if (session.PowerTarget != null)
            {
                try
                {
                    var read = powerRead;
                    var snapshot = await Task.Run(() => read());
                    if (snapshot.Active == session.PowerTarget && snapshot.Plans.Any(p => p.Id == session.PowerBefore))
                    {
                        await RunPowerChange(session.PowerBefore, snapshot.Active, Reverting(session.PowerRecord, id => PowerActions.Read(id).Status == "OK"));
                        powerBack = powerSnapshot != null && powerSnapshot.Active == session.PowerBefore;
                    }
                    else
                        notes.Add(Lang.T("схему питания уже сменили, оставляем текущую"));
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception))
                        throw;
                    Text("Status", Lang.T("Не удалось проверить схему питания: ") + ex.Message);
                    return;
                }
            }

            if (!powerBack)
            {
                Text("Status", Lang.T("Схема питания не возвращена. Повторите выключение игрового режима или верните её в «Истории»."));
                return;
            }

            gameMode = null;
            GameModeState.Clear();
            ServiceVisibility();
            Text("Status", Lang.T("Игровой режим выключен") + (notes.Count == 0 ? "." : ": " + string.Join("; ", notes) + "."));
            RefreshGameMode();
            ShowGameLeftover();
        }

        // History lists the newest record first.
        private static T Latest<T>(Func<T[]> history, Func<T, bool> match) where T : class
        {
            try
            {
                return history().Where(match).FirstOrDefault();
            }
            catch (IOException)
            {
                return null;
            }
        }

        // The history record is marked as returned when it is still the one in effect; otherwise the return is a new entry.
        private static string Reverting(string id, Func<string, bool> active)
        {
            try
            {
                return id != null && active(id) ? id : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
