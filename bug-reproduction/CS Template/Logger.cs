using MbientLab.MetaWear.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;

/** implements a cyclic log for storing accel/gyro samples.
 */
namespace MbientLab.MetaWear.Template
{
    public class Logger
    {
        private static long HISTORY_SIZE_MILLIS =  4000;

        private static String SEP = " ; ";
        
        public struct LogEntry
        {
            /// <summary>
            ///  acceleration
            /// </summary>
            private CartesianFloat acc;
            private CartesianFloat rotation;
            private Point pos;
            private bool init;
            private bool spill;

            /// <summary>
            /// time of recording this record in ticks
            /// </summary>
            private long recordTick;

            public LogEntry(long time_stamp, CartesianFloat _acc, CartesianFloat _rotation, Point _pos, bool spill)
            {
                this.spill = spill;
                this.pos        = _pos;
                this.recordTick = time_stamp; //getCurrentTick();
                this.acc        = _acc;
                this.rotation   = _rotation;
                this.init = true;
            }

            public bool isInit()
            {
                return this.init;
            }

            internal void invalidate()
            {
                init = false;
            }

            public bool fits(long tickNow, long millis)
            {
                long elapsedTicks     = tickNow - recordTick;
                double ElapsedSeconds = elapsedTicks * (1.0 / Stopwatch.Frequency);
                double elapsedMillis  = ElapsedSeconds * 1000;
                if (elapsedMillis < millis)
                {
                    return true;
                }
                return false;
            }

            public void write(StreamWriter s)
            {
                double timeStamp = recordTick * (1.0 / Stopwatch.Frequency);
                string str = "" + timeStamp + SEP + acc.x + SEP + acc.y + SEP + acc.z + SEP + rotation.x + SEP + rotation.y + SEP + rotation.z + SEP + pos.X + SEP + pos.Y + SEP + spill + SEP;
                str = str.Replace(".", ",");
                s.WriteLine(str);
            }
        };


        private const uint MAX_LOG_SIZE = (1024 * 1024);

        private LogEntry[] data = new LogEntry[MAX_LOG_SIZE];
        private uint write_index = 0;


        private uint next(uint index)
        {
            return (index + 1) % MAX_LOG_SIZE;
        }

        private uint prev(uint index)
        {
            if (index == 0)
            {
                return MAX_LOG_SIZE - 1;
            }
            return index - 1;
        }


        Object threadLock = new Object();

        /** called from metawear callback, potentially from a different thread. 
         */
        public void addLogEntry(LogEntry le)
        {
            lock (threadLock)
            {
                data[write_index] = le;
                write_index = next(write_index);
            }
        }

        private bool writeToLog(StreamWriter s, LogEntry entry, long tickNow, bool all, long millis)
        {
            if (entry.isInit())
            {
                if (all || entry.fits(tickNow, millis))
                {
                    entry.write(s);
                    return true;
                }
            }
            return false;
        }

        // this lock protects against multiple asyncs writing the same file at the same time.
        private SemaphoreSlim myLock = new SemaphoreSlim(1);

        private Dictionary<string, string> log_files = new Dictionary<string, string>();

        /** dump log to file ignore dupplicate attemps to write the same file.
         */
        public async Task<bool> dumpLogToFile(String filename, bool all, bool streaming, int freq, String jsonFileName)
        {
            await myLock.WaitAsync();

            if (log_files.ContainsKey(filename))
            {
                myLock.Release();
                return false;
            }
            log_files.Add(filename, filename);
            
            bool ret2 = await writeMWLogEntries(filename, all, streaming, freq, jsonFileName);
            myLock.Release();
            return ret2;
        }
        

        private async Task<bool> writeMWLogEntries(string filename, bool all, bool streaming, int freq, String jsonFileName)
        { 
            UInt32 numElements = 0;
            try
            {
                long tickNow = getCurrentTick();

                StorageFile file = await Windows.Storage.ApplicationData.Current.LocalFolder.CreateFileAsync(filename);
                IRandomAccessStream writeStream = await file.OpenAsync(FileAccessMode.ReadWrite);
                System.IO.Stream s = writeStream.AsStreamForWrite();
                StreamWriter sw = new StreamWriter(s);

                string now = string.Format("{0:yyyy-MM-dd_hh-mm-ss-tt}", DateTime.Now);

                sw.WriteLine("# " + now + (streaming ? ", streaming" : ", logging") + ", freq: " + freq + ", " + jsonFileName);
                sw.WriteLine("# time; acc-x; acc-y; axx-z; gyro-x; gyro-y; gyro-z; pos-x; pos-y; puck-x; puck-y; spill;");

                uint read_index = prev(write_index);
                for (int i = 0; i < MAX_LOG_SIZE; i++)
                {
                    if (!writeToLog(sw, data[read_index], tickNow, all, HISTORY_SIZE_MILLIS))
                    {
                        break;
                    }
                    data[read_index].invalidate();
                    numElements++;
                    read_index = prev(read_index);
                }

                await writeStream.FlushAsync();
                sw.Dispose();
                writeStream.Dispose();
            }
            catch (System.Exception e)
            {
                System.Diagnostics.Debug.WriteLine("failed to write file: " + e);
            }

            System.Diagnostics.Debug.WriteLine("wrote: " + filename + ", " + numElements + " #entries");
            return true;
        }

        private static long getCurrentTick()
        {
            return Stopwatch.GetTimestamp();
        }
    }

}
