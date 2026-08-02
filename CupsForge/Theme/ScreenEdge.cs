using System;
using System.Runtime.InteropServices;
using System.Windows;
using CupsCore;
using System.Windows.Interop;

namespace CupsForge
{
    /// <summary>
    /// Рабочая область экрана, на котором сейчас окно.
    ///
    /// SystemParameters.WorkArea отдаёт ГЛАВНЫЙ монитор и только его. У дизайнера
    /// почти наверняка два экрана, и свёрнутый к краю язычок уезжал бы на чужой —
    /// а это ровно тот сорт «у меня работает», из-за которого потом не понимают,
    /// куда делась программа. Спрашиваем Windows, на каком мониторе окно.
    ///
    /// Координаты возвращаются в единицах WPF (с поправкой на масштаб), потому что
    /// Left/Top у окна именно в них: на экране со 150% разница в полтора раза.
    /// </summary>
    public static class ScreenEdge
    {
        private const int MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rectangle
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rectangle Monitor;
            public Rectangle Work;
            public int Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        /// <summary>
        /// Рабочая область (без панели задач) экрана, на котором окно.
        /// Если спросить не удалось — главный экран: программа должна работать
        /// и на системе, где что-то пошло не так с многомониторным API.
        /// </summary>
        public static Rect WorkAreaFor(Window window)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                    return SystemParameters.WorkArea;

                IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                if (monitor == IntPtr.Zero)
                    return SystemParameters.WorkArea;

                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref info))
                    return SystemParameters.WorkArea;

                var source = PresentationSource.FromVisual(window);
                var transform = source?.CompositionTarget?.TransformFromDevice;

                var topLeft = new Point(info.Work.Left, info.Work.Top);
                var bottomRight = new Point(info.Work.Right, info.Work.Bottom);

                if (transform.HasValue)
                {
                    topLeft = transform.Value.Transform(topLeft);
                    bottomRight = transform.Value.Transform(bottomRight);
                }

                return new Rect(topLeft, bottomRight);
            }
            catch
            {
                return SystemParameters.WorkArea;
            }
        }
    }
}
