using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WatchFolderService
{
    /// <summary>
    /// Object holding information returned by REST API for session
    /// </summary>
    class Session : BaseObject
    {
        public Session() { }

        public Session(string sessionName, string parentID)
        {
            Name = sessionName;
            ParentFolderID = parentID;
        }

        public string Name { get; set; }

        public string ParentFolderID { get; set; }

    }
}
