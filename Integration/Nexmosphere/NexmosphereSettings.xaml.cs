using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace XiboClient.Integration.Nexmosphere
{
    /// <summary>
    /// Interaction logic for NexmosphereSettings.xaml
    /// </summary>
    public partial class NexmosphereSettings : Window
    {
        private Nexmosphere _nexmosphere;

        public NexmosphereSettings()
        {
            InitializeComponent();

            Loaded += InfoScreen_Loaded;
            Unloaded += InfoScreen_Unloaded;
        }

        private void InfoScreen_Loaded(object sender, RoutedEventArgs e)
        {
            _nexmosphere = Nexmosphere.Instance;
            _nexmosphere.OnMessageRecieved += _nexmosphere_OnMessageRecieved;

            // Assert our config
            textBoxPortName.Text = _nexmosphere.Settings.PortName;
            checkBoxIsEnabled.IsChecked = _nexmosphere.Settings.IsEnabled;
        }

        private void _nexmosphere_OnMessageRecieved(Message message)
        {
            if (!CheckAccess())
            {
                Dispatcher.Invoke(() => dataGrid.Items.Add(message));
                return;
            };
            dataGrid.Items.Add(message);
        }

        private void InfoScreen_Unloaded(object sender, RoutedEventArgs e)
        {
            // Save
            _nexmosphere.Stop();
            _nexmosphere.SaveConfiguration();

            // Unbind events
            Loaded -= InfoScreen_Loaded;
            Unloaded -= InfoScreen_Unloaded;
        }

        private void checkBoxIsEnabled_Checked(object sender, RoutedEventArgs e)
        {
            if (_nexmosphere != null)
            {
                _nexmosphere.Settings.IsEnabled = true;
                _nexmosphere.Settings.ModifiedDt = DateTime.Now;
            }
        }

        private void checkBoxIsEnabled_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_nexmosphere != null)
            {
                _nexmosphere.Settings.IsEnabled = false;
                _nexmosphere.Settings.ModifiedDt = DateTime.Now;
            }
        }

        private void textBoxPortName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_nexmosphere != null)
            {
                _nexmosphere.Settings.PortName = textBoxPortName.Text;
                _nexmosphere.Settings.ModifiedDt = DateTime.Now;
            }
        }

        private void buttonStart_Click(object sender, RoutedEventArgs e)
        {
            _nexmosphere.Start();
        }

        private void buttonStop_Click(object sender, RoutedEventArgs e)
        {
            _nexmosphere.Stop();
        }

        private void checkboxTriggerWebHook_Checked(object sender, RoutedEventArgs e)
        {
            if (_nexmosphere != null)
            {
                _nexmosphere.Settings.IsTriggerWebhook = true;
                _nexmosphere.Settings.ModifiedDt = DateTime.Now;
            }
        }

        private void checkboxTriggerWebHook_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_nexmosphere != null)
            {
                _nexmosphere.Settings.IsTriggerWebhook = false;
                _nexmosphere.Settings.ModifiedDt = DateTime.Now;
            }
        }
    }
}
