using System.Windows;
using System.Windows.Media.Animation;
using CupsCore;

namespace CupsForge
{
    /// <summary>
    /// Сворачивание программы к правому краю экрана.
    ///
    /// Окно уезжает за край, прячется целиком, и вместо него у края остаётся
    /// узкий язычок поверх всех окон. Щелчок по язычку возвращает окно на место.
    ///
    /// Зачем это вместо обычного сворачивания в панель задач: дизайнер работает
    /// в Illustrator весь день, а CupsForge нужен на минуту в начале каждого
    /// заказа. В панели задач его ещё надо найти среди двадцати значков; у края
    /// экрана он всегда на одном месте и в одном щелчке, при этом не занимает
    /// ни пикселя рабочего пространства.
    ///
    /// Выключается в настройках: кому мешает полоска поверх всех окон, тот
    /// пользуется обычным сворачиванием.
    /// </summary>
    public partial class AutoWindow
    {
        private DockTab? _dock;

        /// <summary>
        /// Где окно стояло до сворачивания — туда и вернём.
        ///
        /// Запоминаются обе координаты. Раньше возвращалась только левая, и
        /// достаточно было свернуть, подвинуть что-то на экране и развернуть,
        /// чтобы окно оказалось не там, где его оставили.
        /// </summary>
        private double _expandedLeft;
        private double _expandedTop;

        /// <summary>Программа сейчас свёрнута в язычок.</summary>
        internal bool IsCollapsed => _dock is { IsVisible: true };

        private void Collapse_Click(object sender, RoutedEventArgs e) => CollapseToEdge();

        /// <summary>
        /// Убирает окно за правый край экрана и показывает язычок.
        /// </summary>
        internal void CollapseToEdge()
        {
            if (IsCollapsed)
                return;

            Rect work = ScreenEdge.WorkAreaFor(this);
            _expandedLeft = Left;
            _expandedTop = Top;

            var slide = new DoubleAnimation(Left, work.Right, (Duration)FindResource("M.Base"))
            {
                EasingFunction = (IEasingFunction)FindResource("Ease")
            };

            slide.Completed += (_, _) =>
            {
                // Анимация «держит» значение и не даёт задать Left вручную —
                // снимаем её перед тем, как расставлять окно обратно.
                BeginAnimation(LeftProperty, null);
                Left = _expandedLeft;
                Hide();
                ShowDock(work);
            };

            BeginAnimation(LeftProperty, slide);
        }

        private void ShowDock(Rect work)
        {
            if (_dock == null)
            {
                _dock = new DockTab();
                _dock.Expand += (_, _) => ExpandFromEdge();

                // Перетащили язычок — запоминаем в профиле: человек двигает его
                // один раз и ожидает найти там же завтра.
                _dock.Moved += (_, top) =>
                {
                    MachineProfile.Current.SideDrawerTop = top;
                    try { MachineProfile.Current.Save(); }
                    catch (Exception ex) { Log("Не удалось запомнить место язычка: " + ex.Message,
                                               NoticeKind.Warning); }
                };

                // Из свёрнутого вида иначе не выйти: главное окно спрятано,
                // и закрыть программу было бы нечем.
                _dock.Exit += (_, _) =>
                {
                    _dock?.Close();
                    _dock = null;
                    Application.Current.Shutdown();
                };
            }

            // По высоте язычок встаёт вровень с окном — там, где человек только
            // что на него смотрел. Если язычок когда-то перетаскивали, побеждает
            // выбранное положение: его выбирали осознанно, подальше от того,
            // что мешает на экране.
            double top = MachineProfile.Current.SideDrawerTop
                         ?? _expandedTop + (ActualHeight - _dock.Height) / 2;

            _dock.ShowAt(work, top);
        }

        /// <summary>Возвращает окно из-за края и убирает язычок.</summary>
        internal void ExpandFromEdge()
        {
            if (_dock == null)
                return;

            Rect work = ScreenEdge.WorkAreaFor(_dock);

            _dock.Hide();

            // Показываем уже за краем, чтобы окно выехало, а не возникло.
            // Вертикаль ставим сразу: анимируется только въезд сбоку.
            Left = work.Right;
            Top = _expandedTop;
            Show();
            Activate();

            BeginAnimation(LeftProperty,
                new DoubleAnimation(work.Right, _expandedLeft, (Duration)FindResource("M.Base"))
                {
                    EasingFunction = (IEasingFunction)FindResource("Ease")
                });
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
