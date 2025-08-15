using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using YourNamespace;

namespace User.PluginSdkDemo
{
    public partial class PitWindowSettingsControl : UserControl
    {
        private readonly PitWindowPlugin _plugin;

        // Пустой конструктор — необходим дизайнеру
        public PitWindowSettingsControl()
        {
            InitializeComponent();

            if (DesignerProperties.GetIsInDesignMode(this))
            {
                this.DataContext = new PitWindowSettings();
            }
        }

        // Рабочий конструктор — будет вызван SimHub с экземпляром плагина
        public PitWindowSettingsControl(PitWindowPlugin plugin) : this()
        {
            _plugin = plugin;
            if (_plugin != null && _plugin.Settings != null)
            {
                this.DataContext = _plugin.Settings;
            }
        }

        private void OnSaveNowClick(object sender, RoutedEventArgs e)
        {
            _plugin?.SaveSettingsNow();
        }

        private void OnResetDefaultsClick(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Сбросить настройки на значения по умолчанию?",
                                      "PitWindow", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                var def = new PitWindowSettings();
                if (_plugin != null)
                {
                    _plugin.Settings = def;    // требуется публичный setter
                    this.DataContext = def;
                    _plugin.SaveSettingsNow();
                }
                else
                {
                    this.DataContext = def;
                }
            }
        }
    }
}
