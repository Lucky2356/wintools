using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private void InitializeProblems()
        {
            Click("ReportProblem", ShowProblemHelp);
        }

        // Plain next steps for a failed operation, and a ready diagnostics package for a bug report.
        private void ShowProblemHelp()
        {
            var body = new StackPanel();
            var message = SheetText(Get<TextBlock>("Status").Text);
            message.FontWeight = System.Windows.FontWeights.SemiBold;
            body.Children.Add(message);
            body.Children.Add(SheetPoint("", Lang.T("Посмотрите подробности"), Lang.T("Кнопка «Подробности» внизу окна показывает вывод операции: в последних строках обычно названа причина.")));
            body.Children.Add(SheetPoint("", Lang.T("Повторите позже"), Lang.T("Часть ошибок временная: занят файл, нет сети или Windows ещё устанавливает обновления. Повторите после перезапуска Wintools или компьютера.")));
            body.Children.Add(SheetPoint("", Lang.T("Проверьте историю"), Lang.T("Если операция прервалась, в «Истории и откате» видно, что успело измениться, и это можно вернуть.")));
            body.Children.Add(SheetPoint("", Lang.T("Сообщите о проблеме"), Lang.T("Пакет диагностики содержит версии, ошибки и журналы Wintools; имя пользователя, компьютера и путь профиля заменяются. Приложите его к сообщению на GitHub.")));
            var report = SheetButton(Lang.T("Сообщить на GitHub ↗"), false, () => OpenUrl("https://github.com/Lucky2356/wintools/issues/new/choose"));
            report.SetResourceReference(System.Windows.FrameworkElement.StyleProperty, "Link");
            ShowSheet(null, Lang.T("Что делать, если операция не удалась"), body, CloseSheet, new[] { report }, new[]
            {
                SheetButton(Lang.T("Закрыть"), false, CloseSheet),
                SheetButton(Lang.T("Собрать пакет диагностики"), true, async () =>
                {
                    CloseSheet();
                    await CreateDiagnosticBundle();
                })
            });
        }
    }
}
