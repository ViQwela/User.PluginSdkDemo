using System.Windows;
using System.Windows.Controls;

namespace YourNamespace
{
    public partial class PitWindowSettingsControl : UserControl
    {
        private PitWindowPlugin _plugin;

        // Пустой конструктор — нужен дизайнеру Visual Studio
        public PitWindowSettingsControl()
        {
            InitializeComponent();

            // Дизайнер Visual Studio может создавать контрол без плагина.
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                this.DataContext = new PitWindowSettings(); // фиктивные данные для предпросмотра
            }
        }

        // Рабочий конструктор — SimHub будет вызывать его из плагина
        public PitWindowSettingsControl(PitWindowPlugin plugin) : this()
        {
            _plugin = plugin;
            if (_plugin != null && _plugin.Settings != null)
            {
                this.DataContext = _plugin.Settings; // биндинги из XAML начинают работать
            }
        }

        private void OnSaveNowClick(object sender, RoutedEventArgs e)
        {
            _plugin?.SaveSettingsNow();
        }

        private void OnResetDefaultsClick(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Сбросить настройки на значения по умолчанию?", "PitWindow",
                                      MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                var def = new PitWindowSettings();
                if (_plugin != null)
                {
                    _plugin.Settings = def;          // заменяем объект настроек
                    this.DataContext = def;          // обновляем биндинги
                    _plugin.SaveSettingsNow();       // сохраняем файл конфигурации
                }
                else
                {
                    this.DataContext = def;          // для дизайнера
                }
            }
        }
    }
}