using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WatchFolderService
{
    /// <summary>
    /// Object holding information returned by REST API for upload
    /// </summary>
    class Upload : BaseObject
    {
        public Upload() { }

        public Upload(string sessionID, string sessionName)
        {
            SessionID = sessionID;
            UploadTarget = sessionName;
        }

        public string SessionID { get; set; }

        public string UploadTarget { get; set; }
    }
}
