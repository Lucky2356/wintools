using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private Border attentionBanner;
        private TextBlock attentionText;
        private HistoryRow[] attentionRows = new HistoryRow[0];
        private Button rollbackButton;

        // After a crash, a closed laptop lid or a failed step the start page says so once, with the way to history.
        private Border AttentionBanner()
        {
            attentionBanner = new Border { BorderThickness = new Thickness(3, 1, 1, 1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 16), Visibility = Visibility.Collapsed };
            attentionBanner.SetResourceReference(Border.BackgroundProperty, "Surface");
            attentionBanner.SetResourceReference(Border.BorderBrushProperty, "Warning");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new TextBlock { Text = "", FontSize = 18, Margin = new Thickness(0, 2, 12, 0) };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
            grid.Children.Add(icon);
            var words = new StackPanel();
            words.Children.Add(new TextBlock { Text = Lang.T("Прошлая операция не завершилась"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            attentionText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            attentionText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            words.Children.Add(attentionText);
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);
            var buttons = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var open = new Button { Content = Lang.T("Открыть историю"), Margin = new Thickness(0, 0, 8, 0) };
            open.Click += (s, e) =>
            {
                AcknowledgeAttention();
                ShowPage(1);
            };
            buttons.Children.Add(open);
            var hide = new Button { Content = Lang.T("Скрыть") };
            hide.SetResourceReference(FrameworkElement.StyleProperty, "Ghost");
            hide.Click += (s, e) => AcknowledgeAttention();
            buttons.Children.Add(hide);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);
            attentionBanner.Child = grid;
            return attentionBanner;
        }

        private void InitializeRecovery()
        {
            // The updater keeps the replaced program next to it; going back uses the same verified replacement.
            var row = (WrapPanel)Get<Button>("CheckUpdates").Parent;
            rollbackButton = new Button { Margin = new Thickness(0, 0, 8, 8), Visibility = Visibility.Collapsed };
            rollbackButton.Click += async (s, e) => await RollBack();
            row.Children.Add(rollbackButton);
            ShowRollback();
        }

        // History entries that stopped half way or failed and that the person has not seen on the start page yet.
        internal static HistoryRow[] Unfinished(IEnumerable<HistoryRow> rows, ICollection<string> acknowledged)
        {
            return rows.Where(r => r.Status == Lang.T("Требует внимания") && !acknowledged.Contains(AttentionKey(r))).OrderByDescending(r => r.TimeUtc).ToArray();
        }

        private static string AttentionKey(HistoryRow row)
        {
            return row.Run + "|" + row.Title;
        }

        private void ShowAttention(IEnumerable<HistoryRow> rows)
        {
            if (attentionBanner == null)
                return;
            attentionRows = Unfinished(rows, preferences.Acknowledged);
            attentionBanner.Visibility = attentionRows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (attentionRows.Length == 0)
                return;
            var newest = attentionRows[0];
            attentionText.Text = (attentionRows.Length == 1 ? "«" + newest.Title + "» · " + newest.Detail + ". " : Lang.T("Незавершённых операций: ") + attentionRows.Length + Lang.T(", последняя — «") + newest.Title + "». ") + Lang.T("Возможно, Wintools или компьютер закрылись во время работы. Проверьте запись в истории: там можно вернуть прежнее состояние.");
        }

        private void AcknowledgeAttention()
        {
            foreach (var row in attentionRows)
                preferences.Acknowledged.Add(AttentionKey(row));
            // Only the latest entries matter; the list must not grow with every failure for years.
            if (preferences.Acknowledged.Count > 100)
                preferences.Acknowledged.RemoveRange(0, preferences.Acknowledged.Count - 100);
            SavePreferences();
            attentionRows = new HistoryRow[0];
            attentionBanner.Visibility = Visibility.Collapsed;
        }

        // The version saved by the last update, or null when there is none to return to.
        internal static string PreviousVersion(string exe)
        {
            var previous = exe + ".previous";
            if (!File.Exists(previous))
                return null;
            try
            {
                var version = FileVersionInfo.GetVersionInfo(previous).ProductVersion;
                return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
            }
            catch (IOException)
            {
                return null;
            }
        }

        private void ShowRollback()
        {
            if (rollbackButton == null)
                return;
            var version = PreviousVersion(Program.Exe);
            bool available = version != null && version != Program.Version;
            rollbackButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            if (available)
            {
                rollbackButton.Content = Lang.T("Вернуть версию ") + version;
                rollbackButton.ToolTip = Lang.T("Если после обновления что-то перестало работать: Wintools закроется и откроется предыдущая версия.");
            }
        }

        private async Task RollBack()
        {
            var version = PreviousVersion(Program.Exe);
            if (busy || checking || downloading || version == null)
                return;
            if (!await Confirm(Lang.T("Вернуть Wintools ") + version + Lang.T("? Приложение закроется и откроется предыдущая версия. История и настройки сохранятся. Автоматическая установка обновлений будет выключена, чтобы новая версия не вернулась сама.")))
                return;
            try
            {
                var directory = Path.Combine(Program.Data, "updates", Guid.NewGuid().ToString("N"));
                Program.SafeDirectory(directory);
                Directory.CreateDirectory(directory);
                var next = Path.Combine(directory, "next.exe");
                File.Copy(Program.Exe + ".previous", next, false);
                string digest;
                using (var hash = SHA256.Create())
                using (var stream = File.OpenRead(next))
                    digest = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                preferences.AutoInstall = false;
                SavePreferences();
                Updates.LaunchReplacement(directory, digest);
                replacing = true;
                Window.Close();
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception))
                    throw;
                Text("UpdateStatus", Lang.T("Не удалось вернуть предыдущую версию: ") + ex.Message);
                Text("Status", Lang.T("Версия не изменена."));
            }
        }
    }
}
