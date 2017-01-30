using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace WatchFolderService
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main(string[] args)
        {
            WatchFolderService service = new WatchFolderService();

            if (Environment.UserInteractive)
            {
                service.RunInteractive(args);
            }
            else
            {
                ServiceBase.Run(service);
            }
        }
    }
}
