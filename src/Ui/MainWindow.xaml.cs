using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Backend.Discovery;

namespace Ui;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();

        DeviceManager dm = new DeviceManager();
        IReadOnlyList<DeviceDescriptor> devices = dm.ListDevices();

        Debug.WriteLine($"Devices found: {devices.Count}");
    }

    private void Button_Click(object sender, RoutedEventArgs e) {

    }
}