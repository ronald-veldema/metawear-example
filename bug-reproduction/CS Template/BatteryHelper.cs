using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.Threading;
using Windows.UI.Core;
using static MbientLab.MetaWear.Functions;


/** update the battery info every X secs.
 */
namespace MbientLab.MetaWear.Template
{
    internal class BatteryHelper
    {
        private readonly int BATTERY_UPDATE_SECONDS = 5 * 60;

        public async static Task<bool> append_file(string msg)
        {
            String filename = "battery_info.txt";
            
            StorageFile file = await Windows.Storage.ApplicationData.Current.LocalFolder.CreateFileAsync(filename, CreationCollisionOption.OpenIfExists);

            long time = Stopwatch.GetTimestamp();
            await FileIO.AppendTextAsync(file, time + ",\t" + msg + "\r\n");
            
            return true;
        }

        public async Task<bool> update_battery_info(DeviceSetup deviceSetup)
        {
            IntPtr batterySignal = mbl_mw_settings_get_battery_state_data_signal(deviceSetup.getBoard());
            if (batterySignal.ToInt64() != 0)
            {
                mbl_mw_datasignal_subscribe(batterySignal, deviceSetup.batteryDataHandler);
                DeviceSetup.gotSignal = false;
                mbl_mw_datasignal_read(batterySignal);

                while (!DeviceSetup.gotSignal)
                {
                    await Task.Delay(10);
                }

                string msg = "" + DeviceSetup.batteryValue.charge + " %, " + DeviceSetup.batteryValue.voltage + " V";
                await append_file(msg);


                deviceSetup.setBatteryText(msg + "(press for update)");

                mbl_mw_datasignal_unsubscribe(batterySignal);
            }
            else
            {
                deviceSetup.info("failed to initialize battery signal");
            }
            return true;
        }

        internal void start_battery_logging(DeviceSetup deviceSetup)
        {
            TimeSpan period = TimeSpan.FromSeconds(BATTERY_UPDATE_SECONDS);

            ThreadPoolTimer PeriodicTimer = ThreadPoolTimer.CreatePeriodicTimer((source) =>
            {
                    //
                    // Update the UI thread by using the UI core dispatcher.
                    //
                    deviceSetup.Dispatcher.RunAsync(CoreDispatcherPriority.High,
                   () =>
                    {
                        Debug.WriteLine("auto-update of battery status!");
                        update_battery_info(deviceSetup);
                    });

            }, period);
        }        
    }
}