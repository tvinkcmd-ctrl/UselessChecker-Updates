using System.Windows;
using System.Windows.Controls;

namespace UselessChecker.Pages
{
    public partial class HomePage : Page
    {
        public HomePage()
        {
            InitializeComponent();
        }

        private void StartCheck_Click(object sender, RoutedEventArgs e)
        {
            // Переход на страницу проверки
            if (NavigationService != null)
                NavigationService.Navigate(new CheckPage());
        }

        private void LearnMore_Click(object sender, RoutedEventArgs e)
        {
            // Переход на страницу помощи
            if (NavigationService != null)
                NavigationService.Navigate(new HelpPage());
        }
    }
}
