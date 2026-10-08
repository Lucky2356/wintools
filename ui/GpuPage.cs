using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private readonly GpuReader gpuReader = new GpuReader();
        private TextBlock gpuValue, gpuMemory, gpuStatus;
        private ResourceGraph gpuGraph;
        private ComboBox gpuAdapter;
        private bool readingGpu, readingGpuAdapters;
        private Func<GpuAdapter[]> gpuInventory = GpuInventory.Read;
        private Func<GpuSample> gpuRead;
        private void InitializeGpu(StackPanel panel)
        {
            gpuRead = gpuReader.Read;
            var card = ResourceCard(Lang.T("Видеокарта"), out gpuValue, out gpuGraph);
            gpuGraph.Percent = true;
            gpuMemory = Caption(Lang.T("Выберите GPU ниже"));
            card.Children.Add(gpuMemory);
            gpuAdapter = new ComboBox
            {
                DisplayMemberPath = "Name"
            };
            System.Windows.Automation.AutomationProperties.SetName(gpuAdapter, Lang.T("Видеокарта для мониторинга"));
            card.Children.Add(DevicePicker(gpuAdapter, Lang.T("Обновить видеокарты"), async () => await ReadGpuAdapters()));
            gpuStatus = Caption(Lang.T("Нагрузка самого занятого блока выбранного GPU. Объём видеопамяти сообщает DXGI; общая память — доступный предел ОЗУ, а не дополнительно установленная видеопамять."));
            gpuStatus.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(gpuStatus);
            gpuAdapter.SelectionChanged += (s, e) =>
            {
                gpuGraph.Clear();
                gpuValue.Text = Lang.T("Первый замер…");
                var row = gpuAdapter.SelectedItem as GpuAdapter;
                gpuMemory.Text = row == null ? Lang.T("Видеоадаптер не выбран") : Lang.T("Выделенная видеопамять: ") + (row.Dedicated / 1073741824.0).ToString("N1", Lang.Culture) + Lang.T(" ГиБ\nОбщая ОЗУ — предел: ") + (row.SharedLimit / 1073741824.0).ToString("N1", Lang.Culture) + Lang.T(" ГиБ");
            };
            resourceTimer.Tick += async (s, e) => await SampleGpu();
            panel.IsVisibleChanged += async (s, e) =>
            {
                if (panel.IsVisible && gpuAdapter.Items.Count == 0)
                    await ReadGpuAdapters();
            };
        }

        private async Task ReadGpuAdapters()
        {
            if (readingGpuAdapters || closed)
                return;
            readingGpuAdapters = true;
            var prior = gpuAdapter.SelectedItem as GpuAdapter;
            try
            {
                var rows = await Task.Run(() => gpuInventory());
                if (closed)
                    return;
                gpuAdapter.ItemsSource = rows;
                gpuAdapter.SelectedItem = prior == null ? null : rows.FirstOrDefault(r => r.Id == prior.Id);
                if (gpuAdapter.SelectedIndex < 0 && rows.Length > 0)
                    gpuAdapter.SelectedIndex = 0;
                gpuStatus.Text = rows.Length == 0 ? Lang.T("Аппаратные видеоадаптеры DXGI не найдены. В удалённой сессии или виртуальной машине данные могут быть недоступны.") : Lang.T("Видеокарт: ") + rows.Length + Lang.T(". Показывается нагрузка самого занятого блока GPU; нагрузки разных блоков не складываются.");
            }
            catch (Exception ex)
            {
                gpuAdapter.ItemsSource = new GpuAdapter[0];
                gpuValue.Text = Lang.T("Недоступно");
                gpuStatus.Text = Lang.T("Не удалось прочитать видеокарты: ") + ex.Message;
            }
            finally
            {
                readingGpuAdapters = false;
            }
        }

        private async Task SampleGpu()
        {
            if (readingGpu || closed || resourcesPaused || page != 6 || Window.WindowState == WindowState.Minimized)
                return;
            var selected = gpuAdapter.SelectedItem as GpuAdapter;
            if (selected == null)
                return;
            readingGpu = true;
            try
            {
                var sample = await Task.Run(() => gpuRead());
                if (closed || page != 6 || resourcesPaused || Window.WindowState == WindowState.Minimized || gpuAdapter.SelectedItem != selected)
                    return;
                double value;
                bool known = sample.Usage.TryGetValue(selected.Id, out value);
                gpuValue.Text = known ? value.ToString("N0", Lang.Culture) + " %" : Lang.T("Нет замера");
                gpuGraph.Push(known ? (double? )value : null);
                gpuStatus.Text = "GPU · " + DateTime.Now.ToString("HH:mm:ss") + Lang.T(" · Самый занятый блок. ") + (known ? sample.Error : string.IsNullOrEmpty(sample.Error) ? Lang.T("Счётчики выбранной видеокарты недоступны.") : sample.Error);
            }
            catch (Exception ex)
            {
                if (!closed && page == 6 && !resourcesPaused && gpuAdapter.SelectedItem == selected)
                {
                    gpuValue.Text = Lang.T("Недоступно");
                    gpuGraph.Push(null);
                    gpuStatus.Text = ex.Message;
                }
            }
            finally
            {
                readingGpu = false;
            }
        }
    }
}
