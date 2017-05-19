using MbientLab.MetaWear.Core;
using MbientLab.MetaWear.Sensor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage;
using Windows.System.Threading;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using static MbientLab.MetaWear.Functions;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace MbientLab.MetaWear.Template
{
    /// <summary>
    /// Blank page where users add their MetaWear commands
    /// </summary>
    public sealed partial class DeviceSetup : Page
    {
        public static Logger log = new Logger();

        /// <summary>
        /// Pointer representing the MblMwMetaWearBoard struct created by the C++ API
        /// </summary>
        public IntPtr board;
        BatteryHelper batteryHelper = new BatteryHelper();

        public IntPtr getBoard()
        {
            return board;
        }

        public DeviceSetup()
        {
            this.InitializeComponent();
            batteryHelper.start_battery_logging(this);
            sampleRate.Text = ((int)samplingFrequency).ToString();
        }

        public enum SamplingFrequency
        {
            ODR_25HZ = 25,
            ODR_50HZ = 50,
            ODR_100HZ = 100,
            ODR_200HZ = 200,
            ODR_400HZ = 400,
            ODR_800HZ = 800,
            ODR_1600HZ = 1600,
            ODR_3200HZ = 3200
        };

        private static SamplingFrequency samplingFrequency = SamplingFrequency.ODR_100HZ;

        private static long gyro_timestamp;
        private static long acc_timestamp;
        public static CartesianFloat accStructure;
        public static CartesianFloat rotStructure;

        private const byte GPIO_PIN = 0;
        private static bool last_retrieved_gpio_value;
        public static long registered_acc_log_entries;
        public static long registered_gyro_log_entries;

        private bool is_actively_logging;
        private volatile bool clicked_logging_stop;
        private IntPtr gyroLogger;
        private IntPtr accLogger;
        private int setup_device_counters;
        private volatile bool failed_to_create_acc_logger;
        private volatile bool failed_to_create_gyro_logger;
        public volatile static bool gotSignal;
        public static BatteryState batteryValue;
        private int numberOfGPIOTriggersSoFar;


        private static bool started_logging;
        private readonly int DEFAULT_LOG_TIMESTEP = 2000;
        private bool useGyro = true;
        private bool useAccel = true;
        private bool useSpill = true;
        private static bool triggered_gpio_already = false;
        private static bool USE_DISCONNECT_TO_ALLOW_RESET_OF_DEVICE_LOG = false;
        
        private static DeviceSetup deviceSetupPage;

        internal void setBatteryText(string msg)
        {
            batteryInfo.Content = msg;
        }

        private Fn_IntPtr gyroDataHandler = new Fn_IntPtr(pointer =>
        {
            registered_gyro_log_entries++;

            Data data = Marshal.PtrToStructure<Data>(pointer);
            gyro_timestamp = data.epoch;
            rotStructure = Marshal.PtrToStructure<CartesianFloat>(data.value);
        });

        /** this is a callback that is called for accel data.
         */
        private Fn_IntPtr accDataHandler = new Fn_IntPtr(pointer =>
        {
            registered_acc_log_entries++;

            Data data = Marshal.PtrToStructure<Data>(pointer);
            acc_timestamp = data.epoch;
            accStructure = Marshal.PtrToStructure<CartesianFloat>(data.value);
            Point pos = new Point();
            log.addLogEntry(new Logger.LogEntry(acc_timestamp, accStructure, rotStructure, pos, triggered_gpio_already));
        });

        /** this is a callback that is called for accel data.
         */

        private Fn_IntPtr gpio0DataHandler = new Fn_IntPtr(pointer =>
        {
            Data data = Marshal.PtrToStructure<Data>(pointer);
            UInt32 value = Marshal.PtrToStructure<UInt32>(data.value);

            Debug.WriteLine("gpio triggered");

            // we call this method async. Therefore we DO NOT use await here..
            CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, handleSplashEvent);
        });

        private static void handleSplashEvent()
        {
            deviceSetupPage.performTriggerActions();
        }

        /** this gets called once the user presses the button/GPIO_PIN
         */
        void performTriggerActions()
        {
            statusText.Text = "GPIO triggered";

            if (useSpill)
            {
                numberOfGPIOTriggersSoFar++;
                if (started_logging)
                {
                    last_retrieved_gpio_value = true;
                }
                else
                {
                    triggered_gpio_already = true;
                    string n = string.Format("on-trigger-detected-{0:yyyy-MM-dd_hh-mm-ss-tt}.csv",
                        DateTime.Now);

                    string filename = "log-" + n + ".csv";

                    //System.Diagnostics.Debug.WriteLine("write to file: " + filename);

                    // NOTE: we call this method async. We therefore do NOT use await here.
                    int freq = (int)samplingFrequency;
                    log.dumpLogToFile(filename, false, false, freq, "hello");

                    stop_metawear();

                    string dir = ApplicationData.Current.LocalFolder.Path;
                    textBox.Text = "GPIO TRIGGER: " + numberOfGPIOTriggersSoFar + ", stored in: " + dir;
                }
            }
        }

        public Fn_IntPtr batteryDataHandler = new Fn_IntPtr(pointer =>
        {
            Data data = Marshal.PtrToStructure<Data>(pointer);
            batteryValue = Marshal.PtrToStructure<BatteryState>(data.value);
            gotSignal = true;
        });

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var mwBoard = MetaWearBoard.getMetaWearBoardInstance(e.Parameter as BluetoothLEDevice);
            board = mwBoard.cppBoard;

            deviceSetupPage = this;

            string path = ApplicationData.Current.LocalFolder.Path;
            this.pathText.Text = path;
            triggered_gpio_already = false;

            this.gyroCheckbox.IsChecked = useGyro;
            this.accelCheckbox.IsChecked = useAccel;
            this.spillCheckbox.IsChecked = useSpill;
        }

        /// <summary>
        /// Callback for the back button which tears down the board and navigates back to the <see cref="MainPage"/> page
        /// </summary>
        private void back_Click(object sender, RoutedEventArgs e)
        {
            mbl_mw_metawearboard_tear_down(getBoard());
            this.Frame.Navigate(typeof(MainPage));
        }
       

        private bool setup_GPIO()
        {
            deviceSetupPage = this;
            triggered_gpio_already = false;

            if (! useSpill)
            {
                return true;
            }

            IntPtr gpio0Signal = mbl_mw_gpio_get_pin_monitor_data_signal(getBoard(), GPIO_PIN);
            if (gpio0Signal.ToInt64() != 0)
            {
                //IntPtr gpio0Signal = mbl_mw_gpio_get_digital_input_data_signal(getBoard(), PIN);
                mbl_mw_datasignal_subscribe(gpio0Signal, gpio0DataHandler);
                mbl_mw_gpio_set_pull_mode(getBoard(), GPIO_PIN, Gpio.PullMode.UP);
                mbl_mw_gpio_set_pin_change_type(getBoard(), GPIO_PIN, Gpio.PinChangeType.FALLING);
                mbl_mw_gpio_start_pin_monitoring(getBoard(), GPIO_PIN);

                Debug.WriteLine("gpio-initialized");
                return true;
            }
            else
            {
                info("failed to setup the GPIO signal");
                return false;
            }
        }


        private void shutdown_GPIO()
        {
            if (useSpill)
            {
                IntPtr gpio0Signal = mbl_mw_gpio_get_pin_monitor_data_signal(getBoard(), GPIO_PIN);
                //IntPtr gpio0Signal = mbl_mw_gpio_get_digital_input_data_signal(getBoard(), PIN);
                mbl_mw_gpio_stop_pin_monitoring(getBoard(), GPIO_PIN);
                mbl_mw_datasignal_unsubscribe(gpio0Signal);
            }
        }

        private IntPtr getAccSignal(bool highFreq)
        {
            IntPtr accSignal;
            if (highFreq)
            { 
                accSignal = mbl_mw_acc_get_high_freq_acceleration_data_signal(getBoard());
            }
            else
            {
                accSignal = mbl_mw_acc_get_acceleration_data_signal(getBoard());
            }

            if (accSignal.ToInt64() == 0)
            {
                accSignal = mbl_mw_acc_get_acceleration_data_signal(getBoard());
                if (accSignal.ToInt64() == 0)
                {
                    info("failed to locate ANY acceleration signal");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("high-frequency enabled!");
            }
            return accSignal;
        }

        private int getDataRateAccel()
        {
            return (int)samplingFrequency;
        }

        private void config_acc(IntPtr accSignal)
        {
            if (useAccel)
            {
                mbl_mw_acc_set_odr(getBoard(), getDataRateAccel());
                mbl_mw_acc_write_acceleration_config(getBoard());
            }
        }

        private bool setup_accelerometer(bool highFreq)
        {
            if (! useAccel)
            {
                return true;
            }
            IntPtr accSignal = getAccSignal(highFreq);
            if (accSignal.ToInt64() != 0)
            {
                mbl_mw_datasignal_subscribe(accSignal, accDataHandler);
                config_acc(accSignal);
                mbl_mw_acc_enable_acceleration_sampling(getBoard());
                mbl_mw_acc_start(getBoard());
                return true;
            }
            else
            {
                info("failed to initialize accel");
                return false;
            }
        }

        /** perform reverse actions of setup-accelerometer
         */
        private void shutdown_gyro()
        {
            if (useGyro)
            {
                IntPtr gyroSignal = mbl_mw_gyro_bmi160_get_high_freq_rotation_data_signal(getBoard());
                if (gyroSignal.ToInt64() != 0)
                {
                    mbl_mw_gyro_bmi160_stop(getBoard());
                    mbl_mw_gyro_bmi160_disable_rotation_sampling(getBoard());
                    mbl_mw_datasignal_unsubscribe(gyroSignal);
                }
            }
        }


        private void shutdown_accel()
        {
            if (useAccel)
            {
                IntPtr accSignal = mbl_mw_acc_get_acceleration_data_signal(getBoard());
                if (accSignal.ToInt64() != 0)
                {
                    mbl_mw_acc_stop(getBoard());
                    mbl_mw_acc_disable_acceleration_sampling(getBoard());
                    mbl_mw_datasignal_unsubscribe(accSignal);
                }
            }
        }


        private Sensor.GyroBmi160.OutputDataRate getDataRateGyro()
        {
            switch (samplingFrequency)
            {
                case SamplingFrequency.ODR_25HZ: return GyroBmi160.OutputDataRate.ODR_25HZ;
                case SamplingFrequency.ODR_50HZ: return GyroBmi160.OutputDataRate.ODR_50HZ;
                case SamplingFrequency.ODR_100HZ: return GyroBmi160.OutputDataRate.ODR_100HZ;
                case SamplingFrequency.ODR_200HZ: return GyroBmi160.OutputDataRate.ODR_200HZ;
                case SamplingFrequency.ODR_400HZ: return GyroBmi160.OutputDataRate.ODR_400HZ;
                case SamplingFrequency.ODR_800HZ: return GyroBmi160.OutputDataRate.ODR_800HZ;
                case SamplingFrequency.ODR_1600HZ: return GyroBmi160.OutputDataRate.ODR_1600HZ;
                case SamplingFrequency.ODR_3200HZ: return GyroBmi160.OutputDataRate.ODR_3200HZ;
            }
            return Sensor.GyroBmi160.OutputDataRate.ODR_200HZ;
        }

        private void config_gyro(IntPtr gyroSignal)
        {
            if (useGyro)
            {
                // Set data range to +/250 degrees per second
                mbl_mw_gyro_bmi160_set_range(getBoard(), Sensor.GyroBmi160.FullScaleRange.FSR_250DPS);
                mbl_mw_gyro_bmi160_set_odr(getBoard(), getDataRateGyro());
                // Write the changes to the sensor
                mbl_mw_gyro_bmi160_write_config(getBoard());
            }
        }


        private IntPtr getGyroSignal()
        {
            IntPtr gyroSignal;
            if (false)
            {
                gyroSignal = mbl_mw_gyro_bmi160_get_high_freq_rotation_data_signal(getBoard());
            }
            else
            {
                gyroSignal = mbl_mw_gyro_bmi160_get_rotation_data_signal(getBoard());
            }
            return gyroSignal;
        }

        private bool setup_gyroscope()
        {
            if (! useGyro)
            {
                return true;
            }
            IntPtr gyroSignal = getGyroSignal();
            if (gyroSignal.ToInt64() != 0)
            {
                mbl_mw_datasignal_subscribe(gyroSignal, gyroDataHandler);

                config_gyro(gyroSignal);

                mbl_mw_gyro_bmi160_enable_rotation_sampling(getBoard());
                mbl_mw_gyro_bmi160_start(getBoard());
                return true;
            }
            else
            {
                info("failed to initialize gyro");
                return false;
            }
        }
        
        
        private bool retrieveSamplingRate()
        {
            string text = sampleRate.Text;
            try
            {
                int value = int.Parse(text);
                foreach (SamplingFrequency hz in Enum.GetValues(typeof(SamplingFrequency)))
                {
                    if ((int)hz == value)
                    {
                        samplingFrequency = hz;
                        return true;
                    }
                }
            } catch (Exception)
            {
            }
            info(text + " is not a valid sampling frequency. Valid values are: 25, 50, 100, 200, 400, 800");
            return false;
        }

        private Point text2Point(string text)
        {
            int commaPos = text.IndexOf(',');
            if (commaPos <= 0)
            {
                info("invalid point ("+text+"), using 0, 0");
                return new Point(0,0);
            }
            string beforeComma = text.Substring(0, commaPos);
            string afterComma = text.Substring(commaPos + 1);

            int x = 0;
            try
            {
                x = int.Parse(beforeComma);
            }
            catch (Exception)
            {
                info("invalid value: " + beforeComma);
            }
            int y = 0;
            try
            {
                y = int.Parse(afterComma);
            }
            catch (Exception)
            {
                info("invalid value: " + afterComma);
            }
            return new Point(x, y);
        }

        private async Task<bool> haveControlFile()
        {
            string configFileName = this.driveControlScript.Text;
            string path = ApplicationData.Current.LocalFolder.Path;
            String fullPath = path + "\\" + configFileName;

            var item = await ApplicationData.Current.LocalFolder.TryGetItemAsync(configFileName);
            if(item == null)
            {
                info("file '" + configFileName + "' not found. Please copy to " + path);
            }
            return item != null;
        }

        /** event handler for when clicking on the start-sampling button.
         */
        private async void accStart_Click(object sender, RoutedEventArgs e)
        {
            if (! await haveControlFile())
            {
                return;
            }

            if (retrieveSamplingRate())
            {
                string dir = ApplicationData.Current.LocalFolder.Path;
                textBox.Text = "Storing in: " + dir;
                bool highFreq = true;

                if (setup_accelerometer(highFreq))
                {
                    if (setup_gyroscope())
                    {
                        if (setup_GPIO())
                        {
                            disableOptions();
                            accStart.Background = new SolidColorBrush(Colors.Red);
                        }
                    }
                }
            }
        }


        private bool has_drop_of_liquid_on_sensor()
        {
            return last_retrieved_gpio_value;
        }


        void stop_metawear()
        {
            shutdown_GPIO();
            shutdown_accel();
            shutdown_gyro();

            this.accStart.Background = new SolidColorBrush(Colors.LightGray);
            enableOptions();
        }

        private async void accStop_Click(object sender, RoutedEventArgs e)
        {
            stop_metawear();
            string n = string.Format("on-click-streaming-stop-{0:yyyy-MM-dd_hh-mm-ss-tt}.csv",
                DateTime.Now);

            string filename = "log-" + n + ".csv";
            //System.Diagnostics.Debug.WriteLine("write to file: " + filename);
            bool streaming = true;
            int freq = (int)samplingFrequency;
            String jsonFileName = "hello";
            await log.dumpLogToFile(filename, true, streaming, freq, jsonFileName);
        }


        private async void batteryInfo_Click(object sender, RoutedEventArgs e)
        {
            await batteryHelper.update_battery_info(this);
        }

        public async void info(string str)
        {
            MessageDialog showDialog = new MessageDialog(str);
            showDialog.Commands.Add(new UICommand("OK")
            {
                Id = 1
            });
            showDialog.DefaultCommandIndex = 0;
            showDialog.CancelCommandIndex = 1;
            var result = await showDialog.ShowAsync();
        }

        private void handler_loggingBatteryTest(object sender, RoutedEventArgs e)
        {
            info("unimplemented");
        }


        private void handler_loggingStop(object sender, RoutedEventArgs e)
        {
            clicked_logging_stop = true;
        }

        private async Task<int> text2number(string str, int default_value)
        {
            int result = 0;
            if (System.Int32.TryParse(str, out result))
            {
                return result;
            }
            MessageDialog dialog = new MessageDialog("warning: " + str + " is not a valid number, using " + default_value + " instead");
            await dialog.ShowAsync();
            return default_value;
        }


        private bool setup_logging_signals()
        {
            try
            {
                mbl_mw_logging_clear_entries(getBoard());
            } catch (Exception e)
            {
                info("failed to clear log on MetaWear device");
            }
            mbl_mw_metawearboard_set_time_for_response(getBoard(), 500);
            setup_device_counters = 0;
            
            IntPtr accSignal = getAccSignal(false);
            if (accSignal.ToInt64() == 0)
            {
                return false;
            }

            IntPtr gyroSignal = getGyroSignal();
            if (gyroSignal.ToInt64() == 0)
            {
                return false;
            }            
            config_acc(accSignal);
            config_gyro(gyroSignal);
            setup_GPIO();

            System.Diagnostics.Debug.WriteLine("GOOD: setup data signals, now registering logger");

            var logAccFn = new Fn_IntPtr((IntPtr _accLogger) =>
            {
                accLogger = _accLogger;
                if (accLogger.ToInt64() != 0)
                {
                    System.Diagnostics.Debug.WriteLine("GOOD: acc-logger ready");
                    mbl_mw_logger_subscribe(accLogger, accDataHandler);
                    failed_to_create_acc_logger = false;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: failed to create the acc-logger");
                    failed_to_create_acc_logger = true;
                }
                setup_device_counters++;
            });

            var logGyroFn = new Fn_IntPtr((IntPtr _gyroLogger) =>
            {
                gyroLogger = _gyroLogger;
                if (gyroLogger.ToInt64() != 0)
                {
                    System.Diagnostics.Debug.WriteLine("GOOD: gyro-logger ready");
                    mbl_mw_logger_subscribe(gyroLogger, gyroDataHandler);
                    failed_to_create_gyro_logger = false;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: failed to create the gyro-logger");
                    failed_to_create_gyro_logger = true;
                }
                setup_device_counters++;
            });

            if (useAccel)
            {
                mbl_mw_datasignal_log(accSignal, logAccFn);
            }
            if (useGyro)
            {
                mbl_mw_datasignal_log(gyroSignal, logGyroFn);
            }
            string dir = ApplicationData.Current.LocalFolder.Path;
            this.textBox.Text = "storing data in: " + dir;

            disableOptions();
            return true;
        }

        private void stop_logging_signals()
        {
            enableOptions();

            System.Diagnostics.Debug.WriteLine("stop log-cycle");
            if (gyroLogger != null && gyroLogger.ToInt64() != 0 && useGyro)
            {
                mbl_mw_logger_remove(gyroLogger);
            }
            if (accLogger != null && accLogger.ToInt64() != 0 && useAccel)
            {
                mbl_mw_logger_remove(accLogger);
            }
            gyroLogger = default(IntPtr);
            accLogger = default(IntPtr);
                        
            shutdown_accel();
            shutdown_gyro();

            this.shutdown_GPIO();
            this.loggingStart.Background = new SolidColorBrush(Colors.LightGray);
        }
        


        private async void handler_loggingStart(object sender, RoutedEventArgs e)
        {
            if (! await haveControlFile())
            {
                return;
            }
            if (! retrieveSamplingRate())
            {
                return;
            }

            if (started_logging)
            {
                Debug.WriteLine("already logging, not doing it in duplicate!");
                return;
            }
            started_logging = true;

            if (!setup_logging_signals())
            {
                System.Diagnostics.Debug.WriteLine("failed to setup the logging signals");
                started_logging = false;
                return;
            }
            

            while (!clicked_logging_stop && (!(failed_to_create_acc_logger || failed_to_create_gyro_logger)))
            {
                start_logging();
                await wait_timestep();

                if (has_drop_of_liquid_on_sensor())
                {
                    stop_logging();
                    await DownloadHelpers.download_log(accLogger, gyroLogger, downloadProgressBar, getBoard(), textBox, samplingFrequency, "hello");
                    clicked_logging_stop = true;
                    last_retrieved_gpio_value = false;

                    if (registered_acc_log_entries == 0 && registered_gyro_log_entries > 0)
                    {
                        info("metawear log was corrupted: no accel log entries but have " + registered_gyro_log_entries + " gyro log entries");
                    }
                }
                else
                {
                    stop_logging();
                }
                if (USE_DISCONNECT_TO_ALLOW_RESET_OF_DEVICE_LOG)
                {
                    await try_to_disconnect_and_reconnect();
                }
            }
            clicked_logging_stop = false;

            await DownloadHelpers.download_log(accLogger, gyroLogger, downloadProgressBar, getBoard(), textBox, samplingFrequency, "hello");
            
            stop_logging_signals();
            started_logging = false;
        }

        private async Task<bool> try_to_disconnect_and_reconnect()
        {
            //mbl_mw_debug_disconnect(getBoard());
            mbl_mw_debug_reset_after_gc(getBoard());
            //mbl_mw_debug_reset(getBoard());

            this.board = default(IntPtr);

            await Task.Delay(2000);

            while (this.getBoard() == default(IntPtr))
            {
                if (await retrieveBoard())
                {
                    Debug.WriteLine("SUCCESS: retrieved BLE device from windows");
                    break;
                }
                await Task.Delay(100);
                Debug.WriteLine("retrying retrieval of BLE device");
            }
            if (!setup_logging_signals())
            {
                System.Diagnostics.Debug.WriteLine("failed to setup the logging signals");
                started_logging = false;
                return false;
            }
            return true;
        }

        private async Task<bool> retrieveBoard()
        {
            DeviceInformationCollection set = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelector());

            foreach (var devInfo in set)
            {
                try
                {
                    Debug.WriteLine("device = " + devInfo.Name + ", " + devInfo.Pairing);
                    if (devInfo.Name.Contains("MetaWear"))
                    {
                        BluetoothLEDevice selectedDevice = await BluetoothLEDevice.FromIdAsync(devInfo.Id);
                        if (selectedDevice != null)
                        {
                            var newBoard = MetaWearBoard.getMetaWearBoardInstance(selectedDevice);
                            var initResult = await newBoard.Initialize();
                            if (initResult == 0)
                            {
                                board = newBoard.cppBoard;
                                return true;
                            }
                        }
                        else
                        {
                            Debug.WriteLine("windows is confused; found a BLE device-info (" + devInfo.Name + ") but could not find it");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }
            return false;
        }

        async Task<bool> wait_timestep()
        {
            var log_duration = await text2number(this.logDuration.Text, DEFAULT_LOG_TIMESTEP);
            System.Diagnostics.Debug.WriteLine("GOOD:----------------- logging for " + log_duration + " ms");
            
            Stopwatch stopwatch = new Stopwatch();   
            stopwatch.Start();

            double start = DateTime.Now.TimeOfDay.TotalMilliseconds;
            double end = start + log_duration;
            double now = start;
            bool colored = false;
            while (now < end)
            {
                if (failed_to_create_acc_logger && failed_to_create_gyro_logger && useGyro && useAccel)
                {
                    info("failed to create accel/gyro logger, battery low? or metawear needs reset (reset it)");
                    break;
                }
                if (failed_to_create_acc_logger && useAccel)
                {
                    info("failed to create accel logger, battery low, data-rate too high, or metawear needs reset (reset it)");
                    break;
                }
                if (failed_to_create_gyro_logger && useGyro)
                {
                    info("failed to create gyro logger, battery low, data-rate too high, or metawear needs reset (reset it)");
                    break;
                }
                if (!colored)
                {
                    if (gyroLogger.ToInt64() != 0)
                    {
                        this.loggingStart.Background = new SolidColorBrush(Colors.Red);
                        colored = true;
                    }
                }
                
                now = DateTime.Now.TimeOfDay.TotalMilliseconds;
            }
            stopwatch.Stop();
            System.Diagnostics.Debug.WriteLine("GOOD:----------------- DONE logging: " + stopwatch.ElapsedMilliseconds);
            return true;
        }

        void start_logging()
        {
            if (! is_actively_logging)
            {
                mbl_mw_logging_clear_entries(getBoard());
                mbl_mw_logging_start(getBoard(), 1);

                if (useAccel)
                {
                    mbl_mw_acc_enable_acceleration_sampling(getBoard());
                    mbl_mw_acc_start(getBoard());
                }

                if (useGyro)
                {
                    mbl_mw_gyro_bmi160_enable_rotation_sampling(getBoard());
                    mbl_mw_gyro_bmi160_start(getBoard());
                }
                is_actively_logging = true;
            }
        }

        void stop_logging()
        {
            if (is_actively_logging)
            {
                if (useGyro)
                {
                    mbl_mw_gyro_bmi160_stop(getBoard());
                    mbl_mw_gyro_bmi160_disable_rotation_sampling(getBoard());
                }

                if (useAccel)
                {
                    mbl_mw_acc_stop(getBoard());
                    mbl_mw_acc_disable_acceleration_sampling(getBoard());
                }

                mbl_mw_logging_stop(getBoard());
                is_actively_logging = false;
            }
        }

        private void gyroCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            this.useGyro = convertToBool(this.gyroCheckbox.IsChecked);
            Debug.WriteLine(" use gyro now: " + useGyro);
        }

        private void accelCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            this.useAccel = convertToBool(this.accelCheckbox.IsChecked);
            Debug.WriteLine(" use accel now: " + useAccel);
        }

        private void spillCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            this.useSpill = convertToBool(this.spillCheckbox.IsChecked);
            Debug.WriteLine(" use spill now: " + useSpill);
        }

        private bool convertToBool(bool? isChecked)
        {
            if (isChecked.HasValue)
            {
                return isChecked.Value;
            }
            return true;
        }

        void disableOptions()
        {
            this.accelCheckbox.IsEnabled = false;
            this.gyroCheckbox.IsEnabled = false;
            this.spillCheckbox.IsEnabled = false;
        }

        void enableOptions()
        {
            this.accelCheckbox.IsEnabled = true;
            this.gyroCheckbox.IsEnabled = true;
            this.spillCheckbox.IsEnabled = true;
        }
    }
}