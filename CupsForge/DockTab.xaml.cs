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

        /// <summary>Язычок перетащили. Передаёт новое положение по вертикали.</summary>
        public event EventHandler<double>? Moved;

        /// <summary>
        /// Насколько нужно сдвинуть язычок, чтобы это считалось перетаскиванием,
        /// а не щелчком. Без порога любое дрожание руки при нажатии превращало бы
        /// щелчок в микро-перетаскивание, и программа не разворачивалась бы.
        /// </summary>
        private const double DragThreshold = 4;

        private Point _grab;
        private bool _dragging;
        private double _travelled;
        private Rect _bounds;

        public DockTab()
        {
            InitializeComponent();

            // Подсветка при наведении: язычок узкий, и без отклика непонятно,
            // живой он или просто нарисован.
            Tab.MouseEnter += (_, _) => Paint("Input", "AccentDeep", "Text");
            Tab.MouseLeave += (_, _) => { if (!_dragging) Paint("Panel", "Line", "Muted"); };
        }

        private void Paint(string background, string border, string arrow)
        {
            Tab.Background = (Brush)FindResource(background);
            Tab.BorderBrush = (Brush)FindResource(border);
            Arrow.Stroke = (Brush)FindResource(arrow);
        }

        /// <summary>
        /// Ставит язычок к правому краю рабочей области на заданную высоту.
        /// </summary>
        /// <param name="workArea">Рабочая область экрана, к которому прижимаемся.</param>
        /// <param name="top">Желаемое положение по вертикали.</param>
        public void ShowAt(Rect workArea, double top)
        {
            _bounds = workArea;

            Left = workArea.Right - Width;
            Top = Clamp(top);
            Show();

            // Topmost сбрасывается, если поверх всплыло другое «поверх всех».
            // Возвращаем его при каждом показе: язычок, ушедший под чужое окно,
            // равнозначен пропавшей программе.
            Topmost = false;
            Topmost = true;
        }

        /// <summary>Не даём утащить язычок за пределы экрана — оттуда его не вернуть.</summary>
        private double Clamp(double top) =>
            _bounds.Height <= 0
                ? top
                : Math.Max(_bounds.Top, Math.Min(top, _bounds.Bottom - Height));

        // ---------- перетаскивание ----------

        private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _grab = e.GetPosition(this);
            _travelled = 0;
            _dragging = true;
            Tab.CaptureMouse();
        }

        private void Tab_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging)
                return;

            // Считаем смещение относительно точки захвата ВНУТРИ окна: окно
            // едет вместе с курсором, поэтому точка захвата остаётся на месте,
            // а разница и есть пройденный путь. Экранные координаты пришлось бы
            // переводить из пикселей в единицы WPF, и на экране со 150% масштаба
            // язычок убегал бы от курсора в полтора раза быстрее.
            double shift = e.GetPosition(this).Y - _grab.Y;
            if (shift == 0)
                return;

            _travelled += Math.Abs(shift);
            Top = Clamp(Top + shift);
        }

        private void Tab_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging)
                return;

            _dragging = false;
            Tab.ReleaseMouseCapture();

            if (!Tab.IsMouseOver)
                Paint("Panel", "Line", "Muted");

            // Не сдвинули — значит это был щелчок.
            if (_travelled < DragThreshold)
                Expand?.Invoke(this, EventArgs.Empty);
            else
                Moved?.Invoke(this, Top);
        }

        /// <summary>
        /// Сдвинуть язычок с тем же ограничением, что и мышью. Нужно самопроверке:
        /// настоящее перетаскивание она воспроизвести не может, а ограничение
        /// краями экрана проверить обязана — за них язычок уходит безвозвратно.
        /// </summary>
        internal void MoveTo(double top) => Top = Clamp(top);

        /// <summary>Сообщить о перемещении. Только для самопроверки.</summary>
        internal void RaiseMoved(double top) => Moved?.Invoke(this, top);

        private void Tab_Exit(object sender, MouseButtonEventArgs e) =>
            Exit?.Invoke(this, EventArgs.Empty);
    }
}
