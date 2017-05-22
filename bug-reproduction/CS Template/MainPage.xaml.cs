using MbientLab.MetaWear.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Usb;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System.Threading;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace MbientLab.MetaWear.Template {
    public sealed class MacAddressHexString : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, string language) {
            string hexString = ((ulong)value).ToString("X");
            return hexString.Insert(2, ":").Insert(5, ":").Insert(8, ":").Insert(11, ":").Insert(14, ":");
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) {
            throw new NotImplementedException();
        }
    }

    public sealed class ConnectionStateColor : IValueConverter {
        public SolidColorBrush ConnectedColor { get; set; }
        public SolidColorBrush DisconnectedColor { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language) {
            switch ((BluetoothConnectionStatus)value) {
                case BluetoothConnectionStatus.Connected:
                    return ConnectedColor;
                case BluetoothConnectionStatus.Disconnected:
                    return DisconnectedColor;
                default:
                    throw new MissingMemberException("Unrecognized connection status: " + value.ToString());
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) {
            throw new NotImplementedException();
        }
    }

    public sealed partial class MainPage : Page
    {
        private static MainPage _mainPage;

        public MainPage()
        {
            this.InitializeComponent();
            _mainPage = this;

            Package cp = Package.Current;
            string description = cp.Description;
            PackageId id = cp.Id;
            PackageVersion v = id.Version;

            Version.Text = "Version " + v.Major.ToString() + "." + v.Minor.ToString();            
        }


        public async Task<bool> info(string str)
        {
            MessageDialog showDialog = new MessageDialog(str);
            showDialog.Commands.Add(new UICommand("OK")
            {
                Id = 1
            });
            showDialog.DefaultCommandIndex = 0;
            showDialog.CancelCommandIndex = 1;
            var result = await showDialog.ShowAsync();
            return true;
        }
        
        internal static MainPage getMainPage()
        {
            return _mainPage;
        }

        internal void disposeResources()
        {
            Debug.WriteLine("closing/displosing resources!\n");
        }
        
        
        protected override async void OnNavigatedTo(NavigationEventArgs e) {
            base.OnNavigatedTo(e);
            refreshDevices_Click(null, null);
            
            if (!Functions.CheckMetawearDLLIsAvailable())
            {
                await info("DLL '" + Functions.METAWEAR_DLL + "' is not available.");
            }
        }

        

        /// <summary>
        /// Callback for the refresh button which populates the devices list
        /// </summary>
        private async void refreshDevices_Click(object sender, RoutedEventArgs e) {
            pairedDevices.Items.Clear();            

            DeviceInformationCollection set = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelector());
            Debug.WriteLine("BLE devices =====> " + set.Count);
            messages.Text = "#BLE devices = " + set.Count;
            foreach (var devInfo in set) {
                try
                {
                    Debug.WriteLine("device = " + devInfo.Name + ", " + devInfo.Pairing);
                    BluetoothLEDevice ble = await BluetoothLEDevice.FromIdAsync(devInfo.Id);
                    if (ble != null)
                    {
                        pairedDevices.Items.Add(ble);
                    } else
                    {
                        Debug.WriteLine("windows is confused; found a BLE device-info ("+devInfo.Name+") but could not find it");
                    }
                } catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }
        }
        /// <summary>
        /// Callback for the devices list which navigates to the <see cref="DeviceSetup"/> page with the selected device
        /// </summary>
        private async void pairedDevices_SelectionChanged(object sender, SelectionChangedEventArgs e) {

            var selectedDevice = ((ListView)sender).SelectedItem as BluetoothLEDevice;

            if (selectedDevice != null)
            {
                initFlyout.ShowAt(pairedDevices);
                var board = MetaWearBoard.getMetaWearBoardInstance(selectedDevice);
                var initResult = await board.Initialize();

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal, async () => {
                    initFlyout.Hide();

                    if (initResult == Status.ERROR_TIMEOUT)
                    {
                        await new ContentDialog()
                        {
                            Title = "Error",
                            Content = "API initialization timed out.  Try re-pairing the MetaWear or moving it closer to the host device",
                            PrimaryButtonText = "OK"
                        }.ShowAsync();
                    }
                    else
                    {
                        this.Frame.Navigate(typeof(DeviceSetup), selectedDevice);
                    }
                });
            }
        }
    
    

        private void textBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // intentionally left empty...
        }        
    }
}
