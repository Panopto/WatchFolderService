using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WatchFolderService
{
    public class BaseObject
    {
        /// <summary>
        /// ID of this object.
        /// </summary>
        public Guid ID;

        /// <summary>
        /// Gets or sets the supplementary status message ID for this object.
        /// </summary>
        public int MessageID { get; set; }

        /// <summary>
        /// Gets or sets the supplementary status message text for this object.
        /// </summary>
        public string Message { get; set; }
    }
}
