using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private Expander hardwareExpander;
        private Grid hardwareCards;
        private int hardwareColumns = 1;
        private TextBlock hardwareStatus;
        private Button hardwareRefresh, hardwareExport;
        private HardwareSection[] hardwareSnapshot;
        private DateTime hardwareCaptured;
        private bool hardwareReading;
        private Func<Task<HardwareSection[]>> hardwareRead = HardwareReader.Read;
        private void InitializeHardware(StackPanel parent)
        {
            var panel = new StackPanel();
            hardwareStatus = Paragraph(Lang.T("Модели устройств и версии драйверов из сведений Windows. ГиБ = 1024³ байт; ёмкость диска может отличаться от числа на упаковке. Температуры и объём видеопамяти здесь не измеряются."));
            panel.Children.Add(hardwareStatus);
            var controls = new WrapPanel();
            panel.Children.Add(controls);
            hardwareRefresh = new Button
            {
                Content = Lang.T("Обновить характеристики"),
                Margin = new Thickness(0, 0, 10, 12)
            };
            controls.Children.Add(hardwareRefresh);
            hardwareRefresh.Click += async (s, e) => await ReadHardware();
            hardwareExport = new Button
            {
                Content = Lang.T("Сохранить в файл…"),
                IsEnabled = false,
                Margin = new Thickness(0, 0, 0, 12)
            };
            controls.Children.Add(hardwareExport);
            hardwareExport.Click += (s, e) => ExportHardware();
            hardwareCards = new Grid();
            panel.Children.Add(hardwareCards);
            hardwareExpander = new Expander
            {
                Header = Lang.T("Характеристики ПК · процессор, память, видеокарты и диски"),
                Content = panel,
                Margin = new Thickness(0, 0, 0, 18)
            };
            hardwareExpander.SetResourceReference(Control.ForegroundProperty, "Text");
            parent.Children.Add(hardwareExpander);
            hardwareExpander.Expanded += async (s, e) =>
            {
                if (hardwareSnapshot == null)
                    await ReadHardware();
            };
            panel.SizeChanged += (s, e) =>
            {
                int columns = panel.ActualWidth >= 1800 ? 4 : panel.ActualWidth >= 1300 ? 3 : panel.ActualWidth >= 760 ? 2 : 1;
                if (columns != hardwareColumns)
                {
                    hardwareColumns = columns;
                    if (hardwareSnapshot != null)
                        LayoutHardware();
                }
            };
        }

        private async Task ReadHardware()
        {
            if (hardwareReading)
                return;
            hardwareReading = true;
            hardwareRefresh.IsEnabled = false;
            hardwareExport.IsEnabled = false;
            hardwareStatus.Text = Lang.T("Читаем сведения об оборудовании… Настройки не меняются.");
            try
            {
                var snapshot = await hardwareRead();
                if (closed)
                    return;
                hardwareSnapshot = snapshot;
                hardwareCaptured = DateTime.Now;
                RenderHardware();
            }
            catch (Exception ex)
            {
                hardwareStatus.Text = Lang.T("Не удалось обновить характеристики: ") + ex.Message + (hardwareSnapshot == null ? "" : Lang.T(". Ниже остался предыдущий снимок от ") + hardwareCaptured.ToString("HH:mm:ss") + ".");
            }
            finally
            {
                hardwareReading = false;
                hardwareRefresh.IsEnabled = true;
                hardwareExport.IsEnabled = hardwareSnapshot != null;
            }
        }

        private void RenderHardware()
        {
            LayoutHardware();
            hardwareStatus.Text = Lang.T("Снимок от ") + hardwareCaptured.ToString("HH:mm:ss") + Lang.T(" · Разделов: ") + hardwareSnapshot.Length + Lang.T(" · С неполными данными: ") + hardwareSnapshot.Count(s => s.Unavailable) + Lang.T(". ГиБ = 1024³ байт. Температуры и объём видеопамяти не измеряются.");
        }

        private void LayoutHardware()
        {
            hardwareCards.Children.Clear();
            hardwareCards.ColumnDefinitions.Clear();
            var columns = new StackPanel[hardwareColumns];
            for (int i = 0; i < columns.Length; i++)
            {
                hardwareCards.ColumnDefinitions.Add(new ColumnDefinition());
                columns[i] = new StackPanel();
                Grid.SetColumn(columns[i], i);
                hardwareCards.Children.Add(columns[i]);
            }

            int index = 0;
            foreach (var section in hardwareSnapshot)
            {
                var content = new StackPanel();
                var title = Paragraph(section.Title);
                title.SetResourceReference(FrameworkElement.StyleProperty, "CardTitle");
                content.Children.Add(title);
                var detail = Paragraph(section.Text);
                detail.Margin = new Thickness(0);
                content.Children.Add(detail);
                var card = new Border
                {
                    Child = content,
                    Padding = new Thickness(16),
                    Margin = new Thickness(0, 0, 10, 10),
                    CornerRadius = new CornerRadius(8)
                };
                Card(card);
                // Inset tiles inside the expander card.
                card.SetResourceReference(Border.BackgroundProperty, "Layer");
                card.SetResourceReference(Border.BorderBrushProperty, "Divider");
                columns[index++ % columns.Length].Children.Add(card);
            }
        }

        private void ExportHardware()
        {
            if (hardwareReading || hardwareSnapshot == null)
                return;
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Lang.T("Сохранить характеристики ПК"),
                Filter = Lang.T("Текстовый отчёт (*.txt)|*.txt"),
                FileName = "Wintools-PC-" + hardwareCaptured.ToString("yyyyMMdd-HHmmss") + ".txt",
                DefaultExt = ".txt"
            };
            if (dialog.ShowDialog(Window) != true)
                return;
            try
            {
                File.WriteAllText(dialog.FileName, HardwareReader.Report(hardwareSnapshot, hardwareCaptured), new UTF8Encoding(true));
                hardwareStatus.Text = Lang.T("Отчёт сохранён: ") + dialog.FileName;
            }
            catch (Exception ex)
            {
                hardwareStatus.Text = Lang.T("Не удалось сохранить отчёт: ") + ex.Message;
            }
        }
    }
}
