using System.Windows;
using CupsCore;

namespace CupsForge
{
    public partial class App : Application
    {
        public App()
        {
            // До создания стартового окна: оно ссылается на токены через
            // StaticResource, и без них не соберётся.
            Theme.Apply(this);
        }
    }
}
