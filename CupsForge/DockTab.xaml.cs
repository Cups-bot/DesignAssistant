using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CupsCore;

namespace CupsForge
{
    /// <summary>
    /// Язычок свёрнутой программы у правого края экрана, поверх всех окон.
    ///
    /// Свёрнутая в него программа не занимает места на экране и не мешает
    /// работать в Illustrator, но остаётся на расстоянии одного щелчка —
    /// в отличие от сворачивания в панель задач, где её ещё надо найти
    /// среди двадцати других значков.
    /// </summary>
    public partial class DockTab : Window
    {
        /// <summary>Нажали на язычок — развернуть программу.</summary>
        public event EventHandler? Expand;

        /// <summary>Правая кнопка — выйти. Иначе из свёрнутого вида не выйти вовсе.</summary>
        public event EventHandler? Exit;

        public DockTab()
        {
            InitializeComponent();

            // Подсветка при наведении: язычок узкий, и без отклика непонятно,
            // живой он или просто нарисован.
            Tab.MouseEnter += (_, _) =>
            {
                Tab.Background = (Brush)FindResource("Input");
                Tab.BorderBrush = (Brush)FindResource("AccentDeep");
                Arrow.Stroke = (Brush)FindResource("Text");
            };
            Tab.MouseLeave += (_, _) =>
            {
                Tab.Background = (Brush)FindResource("Panel");
                Tab.BorderBrush = (Brush)FindResource("Line");
                Arrow.Stroke = (Brush)FindResource("Muted");
            };
        }

        /// <summary>Ставит язычок вплотную к правому краю указанной рабочей области.</summary>
        public void ShowAt(Rect workArea)
        {
            Left = workArea.Right - Width;
            Top = workArea.Top + (workArea.Height - Height) / 2;
            Show();

            // Topmost сбрасывается, если поверх всплыло другое «поверх всех».
            // Возвращаем его при каждом показе: язычок, ушедший под чужое окно,
            // равнозначен пропавшей программе.
            Topmost = false;
            Topmost = true;
        }

        private void Tab_Click(object sender, MouseButtonEventArgs e) =>
            Expand?.Invoke(this, EventArgs.Empty);

        private void Tab_Exit(object sender, MouseButtonEventArgs e) =>
            Exit?.Invoke(this, EventArgs.Empty);
    }
}
