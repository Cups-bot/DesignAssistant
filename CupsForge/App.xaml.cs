using System.Windows;
using CupsCore;
using Velopack;

namespace CupsForge
{
    public partial class App : Application
    {
        public App()
        {
            // САМОЕ ПЕРВОЕ действие программы, раньше любого окна.
            //
            // Установщик и обновлятор запускают этот же exe со служебными
            // аргументами (--veloapp-install и подобными) и ждут, что он быстро
            // отработает и выйдет. Если до этого успеет подняться интерфейс,
            // установка подвиснет на невидимом окне.
            VelopackApp.Build().Run();

            // До создания стартового окна: оно ссылается на токены через
            // StaticResource, и без них не соберётся.
            Theme.Apply(this);
        }
    }
}
