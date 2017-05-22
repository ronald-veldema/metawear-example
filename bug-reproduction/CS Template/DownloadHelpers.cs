using MbientLab.MetaWear.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml.Controls;

using static MbientLab.MetaWear.Functions;

/** download the accel/gyro data from the device.
 */
namespace MbientLab.MetaWear.Template
{

    class DownloadHelpers
    {
        private const byte NUM_NOTIFIES_PROGRESS_BAR = 20;
        public static long finished_download;
        
        public static long numOfficialEntries, numEntries;
        public static double percentage_downloaded;

        public static Fn_Uint_Uint handler_progress_update = new Fn_Uint_Uint((uint _nEntries, uint totalEntries) =>
        {
            if (totalEntries > 0)
                numOfficialEntries = totalEntries;
            var nEntries = _nEntries;
            if (totalEntries > 0)
            {
                percentage_downloaded = 100.0 * ((double)(totalEntries - nEntries) / (double)totalEntries);
            }
            System.Diagnostics.Debug.WriteLine("Progress: Num of entries: " + nEntries + " total entries:" + totalEntries + ", registered: " + DeviceSetup.registered_acc_log_entries + " / " + DeviceSetup.registered_gyro_log_entries);
            if (nEntries == 0)
            {
                System.Diagnostics.Debug.WriteLine("Download Complete!");

                Interlocked.Add(ref finished_download, -1);
            }
        });

        public static Fn_Ubyte_Long_ByteArray handler_receive_unknown_entry = new Fn_Ubyte_Long_ByteArray((byte id, long epoch, IntPtr value, byte length) =>
        {
        });



        public static async Task<bool> download_log(IntPtr accLogger, IntPtr gyroLogger, ProgressBar downloadProgressBar, IntPtr cppBoard, TextBox textBox, DeviceSetup.SamplingFrequency samplingFrequency)
        {
            if (Interlocked.Read(ref finished_download) != 0)
            {
                return false;
            }

            textBox.Text = "Downloading log!!";
            numOfficialEntries = 0;
            numEntries = 0;
            Stopwatch stopwatch = new Stopwatch();

            stopwatch.Start();

            var handler = new LogDownloadHandler();

            int id_acc_logger = -1;
            int id_gyro_logger = -1;

            if (accLogger.ToInt64() != 0)
                id_acc_logger = mbl_mw_logger_get_id(accLogger);

            if (gyroLogger.ToInt64() != 0)
                id_gyro_logger = mbl_mw_logger_get_id(gyroLogger);

            handler.receivedProgressUpdate = handler_progress_update;
            handler.receivedUnknownEntry = handler_receive_unknown_entry;


            Debug.Assert(Interlocked.Read(ref finished_download) == 0);
            Interlocked.Add(ref finished_download, 1);
            Debug.Assert(Interlocked.Read(ref finished_download) == 1);
            mbl_mw_logging_download(cppBoard, NUM_NOTIFIES_PROGRESS_BAR, ref handler);

            // wait for download to complete...
            DownloadHelpers.percentage_downloaded = 0;
            downloadProgressBar.Value = percentage_downloaded;

            Debug.WriteLine(" FINISHED DOWNLAOD: " + Interlocked.Read(ref finished_download));
            while (Interlocked.Read(ref finished_download) == 1)
            {
                Debug.WriteLine(" FINISHED DOWNLAOD: " + Interlocked.Read(ref finished_download));
                Debug.Assert(Interlocked.Read(ref finished_download) <= 1);
                await Task.Delay(100);
                downloadProgressBar.Value = percentage_downloaded;
            }
            Debug.Assert(Interlocked.Read(ref finished_download) == 0);

            stopwatch.Stop();

            textBox.Text = "download took: " + stopwatch.ElapsedMilliseconds + " ms. Got " + numEntries + " unknown, " + numOfficialEntries + " official. Have logent:" + DeviceSetup.registered_acc_log_entries + " / " + DeviceSetup.registered_gyro_log_entries;

            string n = string.Format("logged-on-device-data-{0:yyyy-MM-dd_hh-mm-ss-tt}.csv",
                DateTime.Now);

            string filename = "log-" + n + ".csv";
            //System.Diagnostics.Debug.WriteLine("write to file: " + filename);
            await DeviceSetup.log.dumpLogToFile(filename, true, false, (int)samplingFrequency);
            return true;
        }
    }
}
