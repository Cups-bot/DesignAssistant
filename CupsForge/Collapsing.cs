using System.Windows;
using System.Windows.Media.Animation;
using CupsCore;

namespace CupsForge
{
    /// <summary>
    /// Сворачивание программы к правому краю экрана.
    ///
    /// Окно уезжает за край, прячется целиком, и вместо него у края остаётся
    /// узкий язычок поверх всех окон. Щелчок по язычку возвращает окно.
    ///
    /// Зачем это вместо сворачивания в панель задач: дизайнер работает
    /// в Illustrator весь день, а CupsForge нужен на минуту в начале каждого
    /// заказа. В панели задач его надо искать среди двадцати значков; у края
    /// экрана он всегда на одном месте и в одном щелчке, при этом не занимает
    /// ни пикселя рабочего пространства.
    ///
    /// ГЛАВНОЕ ПРАВИЛО: язычок у края экрана — это ПРОДОЛЖЕНИЕ стрелки внутри
    /// окна. Они всегда на одной высоте, и переход между ними выглядит так,
    /// будто стрелка осталась на месте, а окно от неё уехало. Поэтому связь
    /// двусторонняя: подняли окно и свернули — язычок оказался выше; опустили
    /// язычок и развернули — окно открылось ниже.
    ///
    /// Выключается в настройках: кому мешает полоска поверх всех окон, тот
    /// пользуется обычным сворачиванием.
    /// </summary>
    public partial class AutoWindow
    {
        private DockTab? _dock;

        /// <summary>
        /// Горизонтальное положение окна. Вертикальное НЕ запоминается: его
        /// задаёт язычок, который человек мог подвинуть, пока окно было свёрнуто.
        /// </summary>
        private double _expandedLeft;

        /// <summary>Программа сейчас свёрнута в язычок.</summary>
        internal bool IsCollapsed => _dock is { IsVisible: true };

        private void Collapse_Click(object sender, RoutedEventArgs e) => CollapseToEdge();

        /// <summary>
        /// Середина стрелки внутри окна — по вертикали, в координатах экрана.
        /// От неё считается высота язычка и обратно.
        /// </summary>
        private double ArrowCenterOnScreen => Top + ArrowOffsetInWindow;

        /// <summary>Насколько середина стрелки отстоит от верха окна.</summary>
        private double ArrowOffsetInWindow
        {
            get
            {
                try
                {
                    Point inWindow = DrawerTabButton
                        .TransformToAncestor(this)
                        .Transform(new Point(0, 0));
                    return inWindow.Y + DrawerTabButton.ActualHeight / 2;
                }
                catch
                {
                    // Окно ещё не разложено — середина по высоте не хуже прочего.
                    return ActualHeight / 2;
                }
            }
        }

        /// <summary>Убирает окно за правый край экрана и показывает язычок.</summary>
        internal void CollapseToEdge()
        {
            if (IsCollapsed)
                return;

            Rect work = ScreenEdge.WorkAreaFor(this);
            _expandedLeft = Left;

            // Высоту язычка считаем ДО отъезда: после Hide() спрашивать нечего.
            double arrowCenter = ArrowCenterOnScreen;

            AnimateLeft(Left, work.Right, () =>
            {
                Left = _expandedLeft;
                Hide();
                ShowDock(work, arrowCenter);
            });
        }

        private void ShowDock(Rect work, double arrowCenter)
        {
            if (_dock == null)
            {
                _dock = new DockTab();
                _dock.Expand += (_, _) => ExpandFromEdge();

                // Из свёрнутого вида иначе не выйти: главное окно спрятано,
                // и закрыть программу было бы нечем.
                _dock.Exit += (_, _) =>
                {
                    _dock?.Close();
                    _dock = null;
                    Application.Current.Shutdown();
                };
            }

            // Язычок встаёт серединой туда, где была середина стрелки в окне.
            _dock.ShowAt(work, arrowCenter - _dock.Height / 2);
        }

        /// <summary>Возвращает окно из-за края и убирает язычок.</summary>
        internal void ExpandFromEdge()
        {
            if (_dock == null)
                return;

            Rect work = ScreenEdge.WorkAreaFor(_dock);
            double tabCenter = _dock.Top + _dock.Height / 2;

            _dock.Hide();

            // Окно встаёт так, чтобы его стрелка оказалась там, где стоял
            // язычок: если язычок опускали, окно открывается ниже.
            // По горизонтали — там же, где было.
            Left = work.Right;
            Show();

            double top = tabCenter - ArrowOffsetInWindow;
            Top = Math.Max(work.Top, Math.Min(top, work.Bottom - ActualHeight));

            Activate();
            AnimateLeft(work.Right, _expandedLeft, null);
        }

        /// <summary>
        /// Двигает окно по горизонтали и ОБЯЗАТЕЛЬНО снимает анимацию в конце.
        ///
        /// Незанятая мелочь, стоившая бага: доигравшая анимация продолжает
        /// удерживать Left, и присвоения ей не перебить. Окно, передвинутое
        /// мышью за титульную строку, уезжало по-настоящему, но свойство
        /// оставалось прежним — при следующем сворачивании запоминалось старое
        /// место, и окно всплывало там, где стояло при запуске (по центру
        /// экрана), а не там, где его оставили.
        /// </summary>
        private void AnimateLeft(double from, double to, Action? done)
        {
            var slide = new DoubleAnimation(from, to, (Duration)FindResource("M.Base"))
            {
                EasingFunction = (IEasingFunction)FindResource("Ease")
            };

            slide.Completed += (_, _) =>
            {
                BeginAnimation(LeftProperty, null);
                Left = to;
                done?.Invoke();
            };

            BeginAnimation(LeftProperty, slide);
        }

        /// <summary>
        /// Закрытие программы уносит язычок с собой. Он — отдельное окно, и без
        /// этого остался бы висеть поверх всех, пережив программу.
        /// </summary>
        private void CloseDock()
        {
            _dock?.Close();
            _dock = null;
        }
    }
}
