using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WatchFolderService
{
    /// <summary>
    /// Object holding information returned by REST API for concluding upload
    /// </summary>
    class Process : BaseObject
    {
        public Process() { }

        public Process(string sessionID, string target, Guid uploadID, int uploadState)
        {
            SessionID = sessionID;
            UploadTarget = target;
            State = uploadState;
            ID = uploadID;
        }

        public int State { get; set; }

        public string SessionID { get; set; }

        public string UploadTarget { get; set; }
    }
}
