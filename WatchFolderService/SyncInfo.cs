using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WatchFolderService
{
    /// <summary>
    /// Holds sync information of file
    /// </summary>
    class SyncInfo
    {
        public SyncInfo() { }

        public SyncInfo(DateTime writeTime, DateTime newWriteTime, int stableTime)
        {
            LastSyncWriteTime = new DateTime(writeTime.Ticks);
            NewSyncWriteTime = new DateTime(newWriteTime.Ticks);
            FileStableTime = stableTime;
        }

        public SyncInfo(SyncInfo other)
        {
            LastSyncWriteTime = new DateTime(other.LastSyncWriteTime.Ticks);
            NewSyncWriteTime = new DateTime(other.NewSyncWriteTime.Ticks);
            FileStableTime = other.FileStableTime;
        }

        public DateTime LastSyncWriteTime { get; set; }

        public DateTime NewSyncWriteTime { get; set; }

        public int FileStableTime { get; set; }
    }
}
