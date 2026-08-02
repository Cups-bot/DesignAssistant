using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CupsCore
{
    /// <summary>
    /// Скругление углов окна средствами Windows.
    ///
    /// Почему не проще:
    ///
    /// 1. CornerRadius у WindowChrome скругляет только НАРИСОВАННУЮ рамку внутри
    ///    окна. Само окно остаётся прямоугольным, и в углах остаётся фон рамки —
    ///    вместо скругления получается тёмный уголок.
    ///
    /// 2. AllowsTransparency="True" даёт настоящие скруглённые углы, но переводит
    ///    окно в слоистый режим: субпиксельное сглаживание (ClearType) в нём
    ///    отключается, весь текст становится заметно мягче. В программе, которую
    ///    целыми днями читает дизайнер, это плохой размен.
    ///
    /// 3. Windows 11 скругляет окна сама, но не все: у окна без стандартной рамки
    ///    (WindowChrome, ResizeMode=NoResize) менеджер окон считает форму заданной
    ///    приложением и не вмешивается. Отсюда «скругление не работает».
    ///
    /// Остаётся попросить прямо: DwmSetWindowAttribute со значением
    /// DWMWA_WINDOW_CORNER_PREFERENCE. Скругляет композитор, поэтому окно
    /// остаётся непрозрачным и ClearType продолжает работать.
    ///
    /// На Windows 10 атрибута нет — вызов вернёт ошибку, углы останутся прямыми.
    /// Это не повод падать: программа работает одинаково и так, и так.
    /// </summary>
    public static class WindowRounding
    {
        private const int DwmwaWindowCornerPreference = 33;

        private enum CornerPreference
        {
            Default = 0,
            DoNotRound = 1,
            Round = 2,
            RoundSmall = 3
        }

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
                                                        ref int value, int size);

        /// <summary>
        /// Скруглить углы окна. Зовётся из SourceInitialized и позже: до создания
        /// дескриптора окна просить нечего.
        /// </summary>
        /// <returns>true — Windows приняла запрос.</returns>
        public static bool Apply(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            return Apply(handle);
        }

        public static bool Apply(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return false;

            try
            {
                int preference = (int)CornerPreference.Round;
                return DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference,
                                             ref preference, sizeof(int)) == 0;
            }
            catch (DllNotFoundException)
            {
                return false; // dwmapi нет — очень старая система
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        /// <summary>
        /// Подписывает окно на скругление, когда бы оно ни создалось.
        /// Отдельный метод, чтобы каждому окну не повторять условие с дескриптором.
        /// </summary>
        public static void Attach(Window window)
        {
            if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            {
                Apply(window);
                return;
            }

            window.SourceInitialized += (_, _) => Apply(window);
        }
    }
}
