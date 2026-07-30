using System.Windows;
using System.Windows.Input;

namespace UselessChecker
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Загружаем начальную страницу (Главная)
            ContentFrame.Navigate(new Pages.HomePage());
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Двойной клик - развернуть/восстановить
                if (WindowState == WindowState.Maximized)
                    WindowState = WindowState.Normal;
                else
                    WindowState = WindowState.Maximized;
            }
            else
            {
                // Одинарный клик - перетаскивание
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Home_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new Pages.HomePage());
        }

        private void Check_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new Pages.CheckPage());
        }

        private void Stats_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new Pages.StatsPage());
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new Pages.SettingsPage());
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new Pages.HelpPage());
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
