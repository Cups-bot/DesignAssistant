using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CupsForge
{
    /// <summary>
    /// Размер значка одним числом — и постоянная толщина штриха при любом размере.
    ///
    /// Значки нарисованы в поле 24×24 и показываются меньше. При Stretch="Uniform"
    /// WPF масштабирует вместе с фигурой и штрих: чтобы получить на экране
    /// полтора пикселя, при размере 12 нужно задать 3, а при размере 17 — 2.1.
    ///
    /// Раньше эти числа стояли в разметке руками, у каждого значка своё, подобранное
    /// на глаз. Толщина по набору гуляла от 1.37 до 1.7 пикселя, и поменять её
    /// разом было нельзя — пришлось бы править два десятка мест, пересчитывая
    /// каждое.
    ///
    /// Теперь размер задаётся один раз:
    ///
    ///     &lt;Path Style="{StaticResource Icon}" Data="{StaticResource I.Close}"
    ///           app:IconBox.Size="14"/&gt;
    ///
    /// а толщина берётся из токена <c>Size.Stroke</c> и пересчитывается сама.
    /// Хотите значки тоньше или жирнее — меняете ОДНО число в Tokens.xaml.
    /// </summary>
    public static class IconBox
    {
        /// <summary>Поле, в котором нарисованы значки. Совпадает с viewBox в SVG.</summary>
        private const double AuthoringBox = 24.0;

        /// <summary>Толщина по умолчанию, если токен почему-то не нашёлся.</summary>
        private const double FallbackStroke = 1.7;

        public static readonly DependencyProperty SizeProperty =
            DependencyProperty.RegisterAttached(
                "Size", typeof(double), typeof(IconBox),
                new PropertyMetadata(double.NaN, OnSizeChanged));

        public static void SetSize(DependencyObject element, double value) =>
            element.SetValue(SizeProperty, value);

        public static double GetSize(DependencyObject element) =>
            (double)element.GetValue(SizeProperty);

        private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Path path || e.NewValue is not double size || size <= 0)
                return;

            path.Width = size;
            path.Height = size;
            path.Stretch = Stretch.Uniform;

            // Толщина в координатах ЧЕРТЕЖА, а не экрана: Uniform поделит её
            // на тот же множитель, на который уменьшит фигуру, и на экране
            // останется ровно столько, сколько просили в токене.
            path.StrokeThickness = StrokeFor(path) * AuthoringBox / size;
        }

        private static double StrokeFor(FrameworkElement element)
        {
            // Токен ищется от самого элемента: так значок в панели настроек
            // и значок в главном окне возьмут одно и то же значение, даже если
            // словари когда-нибудь подключат по-разному.
            object? token = element.TryFindResource("Size.Stroke");
            return token is double value && value > 0 ? value : FallbackStroke;
        }
    }
}
