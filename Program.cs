using System.Windows;

namespace UselessChecker
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new App();
            app.Run();
        }
    }
}