using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Net;

namespace WatchFolderService
{
    public partial class WatchFolderService : ServiceBase
    {
        private static Object INFOFILE_LOCK = new Object(); // Lock for InfoFile that contains sync information
        private static bool SELF_SIGNED = true; // Target server is a self-signed server
        private static bool initialized = false;

        // Upload required information
        private string server = null;
        private string infoFilePath = null;
        private string watchFolder = null;
        private string userID = null;
        private string userKey = null;
        private string folderID = null;
        private int elapse = 60000;
        private long defaultPartsize = 1048576;
        private string[] extensions;

        // Event log variables
        private System.Diagnostics.EventLog eventLog;
        private static int EVENT_ID = 1;
        private bool verbose = false;

        public WatchFolderService()
        {
            InitializeComponent();
            this.AutoLog = false; // System Generated Event Log

            // Parse config file
            server = ConfigurationManager.AppSettings["Server"];
            infoFilePath = ConfigurationManager.AppSettings["InfoFilePath"];
            watchFolder = ConfigurationManager.AppSettings["WatchFolder"];
            userID = ConfigurationManager.AppSettings["UserID"];
            userKey = ConfigurationManager.AppSettings["UserKey"];
            folderID = ConfigurationManager.AppSettings["FolderID"];
            elapse = Convert.ToInt32(ConfigurationManager.AppSettings["ElapseTime"]);
            defaultPartsize = Convert.ToInt64(ConfigurationManager.AppSettings["PartSize"]);
            verbose = Convert.ToBoolean(ConfigurationManager.AppSettings["Verbose"]);
            extensions = ConfigurationManager.AppSettings["UploadExtensions"].Split(';');

            Common.SetServer(server);

            if (SELF_SIGNED)
            {
                // For self-signed servers
                EnsureCertificateValidation();
            }

            // Event Log setup
            eventLog = new System.Diagnostics.EventLog();
            if (!System.Diagnostics.EventLog.SourceExists("PanoptoWatchFolderService"))
            {
                System.Diagnostics.EventLog.CreateEventSource(
                    "PanoptoWatchFolderService", "PanoptoWatchFolderServiceLog");
            }

            eventLog.Source = "PanoptoWatchFolderService";
            eventLog.Log = "PanoptoWatchFolderServiceLog";
            
        }

        /// <summary>
        /// Start of service
        /// </summary>
        /// <param name="args">Arguments</param>
        protected override void OnStart(string[] args)
        {
            // Update the service state to Start Pending.
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_START_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            eventLog.WriteEntry("Service Started Successfully"); // Event Log Record

            // Setup timer
            System.Timers.Timer timer = new System.Timers.Timer();
            timer.Interval = elapse;
            timer.Elapsed += new System.Timers.ElapsedEventHandler(OnTimer);
            timer.Start();

            // Update the service state to Running.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_RUNNING;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        /// <summary>
        /// End function of service
        /// </summary>
        protected override void OnStop()
        {
            // Update the service state to Start Pending.
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOP_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            eventLog.WriteEntry("Service Stopped Successfully"); // Event Log Record
            eventLog.Close();

            // Update the service state to Running.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOPPED;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        /// <summary>
        /// Service event that syncs the folder that happens on given interval
        /// </summary>
        /// <param name="sender">Timer object</param>
        /// <param name="args">Arguments</param>
        private void OnTimer(object sender, System.Timers.ElapsedEventArgs args)
        {
            // Hold lock on sync info file to prevent data race
            lock (INFOFILE_LOCK)
            {
                // Obtain info in sync info file
                DirectoryInfo dirInfo = new DirectoryInfo(Path.GetFullPath(watchFolder));
                Dictionary<string, DateTime> folderInfo = GetLastSyncInfo();
                Dictionary<string, DateTime> uploadFiles = new Dictionary<string, DateTime>();

                // Check files in folder for sync
                foreach (FileInfo fileInfo in GetFileInfo(dirInfo))
                {
                    bool inSync = false;
                    bool found = false;

                    // Check if file is in sync
                    if (folderInfo.ContainsKey(fileInfo.Name))
                    {
                        found = true;
                        DateTime stored = folderInfo[fileInfo.Name];
                        DateTime current = fileInfo.LastWriteTime;
                        current = new DateTime(current.Ticks - (current.Ticks % TimeSpan.TicksPerSecond), current.Kind);

                        if (stored.Equals(current))
                        {
                            inSync = true;
                        }
                    }

                    // Add file to sync queue if not in sync
                    if (!inSync)
                    {
                        if (found)
                        {
                            uploadFiles.Add(fileInfo.FullName, folderInfo[fileInfo.Name]);
                            folderInfo[fileInfo.Name] = fileInfo.LastWriteTime;
                        }
                        else
                        {
                            uploadFiles.Add(fileInfo.FullName, DateTime.MinValue);
                            folderInfo.Add(fileInfo.Name, fileInfo.LastWriteTime);
                        }
                    }
                }

                // Upload file and store new sync info
                ProcessUpload(uploadFiles, folderInfo);

                SetSyncInfo(folderInfo);

                EVENT_ID++;
            }
        }

        /// <summary>
        /// Uploads files in uploadFiles, revert LastWriteTime contained in folderInfo if file upload fails
        /// </summary>
        /// <param name="uploadFiles">Files to be uploaded</param>
        /// <param name="folderInfo">Information about file and its last write time</param>
        private void ProcessUpload(Dictionary<string, DateTime> uploadFiles, Dictionary<string, DateTime> folderInfo)
        {
            // Upload each file and handle any exception
            foreach (string filePath in uploadFiles.Keys)
            {
                try
                {
                    // Event Log Record
                    if (verbose)
                        eventLog.WriteEntry("Uploading: " + filePath, EventLogEntryType.Information, EVENT_ID);

                    UploadAPIWrapper.UploadFile(userID,
                                                userKey, 
                                                folderID, 
                                                Path.GetFileName(filePath), 
                                                filePath, 
                                                defaultPartsize);
                }
                catch (Exception ex)
                {
                    // Event Log Record
                    eventLog.WriteEntry("Uploading " + filePath + " Failed: " + ex.Message, 
                                         EventLogEntryType.Warning, 
                                         EVENT_ID);
                    if (verbose)
                        eventLog.WriteEntry("Stack Trace: " + ex.StackTrace, EventLogEntryType.Warning, EVENT_ID);

                    folderInfo[Path.GetFileName(filePath)] = uploadFiles[filePath];
                }
            }
        }

        /// <summary>
        /// Generate a Dictionary containing files in the folder and their last write time base on sync info file's content
        /// </summary>
        /// <returns>Dictionary containing files in folder and their last write time</returns>
        private Dictionary<string, DateTime> GetLastSyncInfo()
        {
            Dictionary<string, DateTime> folderInfo = new Dictionary<string, DateTime>();

            if (!File.Exists(Path.GetFullPath(infoFilePath)))
            {
                return folderInfo;
            }

            // Put info in sync file into dictionary for use
            string[] infoFileLines = File.ReadAllLines(Path.GetFullPath(infoFilePath));

            foreach (string line in infoFileLines)
            {
                string[] info = line.Split(';');
                if (info.Length != 2)
                {
                    continue;
                }

                DateTime writeTime = GetDateTime(info[1]);

                folderInfo.Add(info[0], writeTime);
            }

            return folderInfo;
        }

        /// <summary>
        /// Store into InfoFile the files and last write time contained in parameter info
        /// </summary>
        /// <param name="info">Dictionary that stores files and their last write time</param>
        private void SetSyncInfo(Dictionary<string, DateTime> info)
        {
            using (System.IO.StreamWriter infoFile = new System.IO.StreamWriter(Path.GetFullPath(infoFilePath), false))
            {
                foreach (string fileName in info.Keys)
                {
                    string line = fileName + ";" + info[fileName].ToString("G");

                    if (verbose)
                        eventLog.WriteEntry("Writing to InfoFile: " + line, EventLogEntryType.Information, EVENT_ID);
                    
                    infoFile.WriteLine(line);
                }
            }
        }

        /// <summary>
        /// Generate a DateTime object from given string
        /// </summary>
        /// <param name="timeString">String of date and time</param>
        /// <returns>DateTime created from timeString</returns>
        private DateTime GetDateTime(string timeString)
        {
            string[] timeInfo = timeString.Split(' ');
            string[] dateString = timeInfo[0].Split('/');
            string[] time = timeInfo[1].Split(':');

            int year = Convert.ToInt16(dateString[2]);
            int month = Convert.ToInt16(dateString[0]);
            int date = Convert.ToInt16(dateString[1]);

            int hr = Convert.ToInt16(time[0]);
            int min = Convert.ToInt16(time[1]);
            int sec = Convert.ToInt16(time[2]);

            if (timeInfo[2].Equals("PM") && hr != 12)
            {
                hr += 12;
            }

            if (timeInfo[2].Equals("AM") && hr == 12)
            {
                hr = 0;
            }

            return new DateTime(year, month, date, hr, min, sec, DateTimeKind.Local);
        }

        /// <summary>
        /// Obtain the FileInfo struct for each file that is present in directory represented by DirectoryInfo
        /// </summary>
        /// <param name="dirInfo">DirectoryInfo of directory to look in</param>
        /// <returns>Array of FileInfo of files in the given directory</returns>
        private FileInfo[] GetFileInfo(DirectoryInfo dirInfo)
        {
            FileInfo[] fullFiles = dirInfo.GetFiles();
            System.Collections.ArrayList resultArray = new System.Collections.ArrayList();

            foreach (FileInfo fileInfo in fullFiles)
            {
                foreach (string ext in extensions)
                {
                    if (fileInfo.Extension.Equals(ext) && IsFileAccessible(fileInfo))
                    {
                        resultArray.Add(fileInfo);
                        break;
                    }
                }
            }

            FileInfo[] result = new FileInfo[resultArray.Count];

            int i = 0;
            foreach (FileInfo fileInfo in resultArray)
            {
                result[i] = fileInfo;
                i++;
            }

            return result;
        }

        /// <summary>
        /// Check if file given in FileInfo fi is accessible
        /// </summary>
        /// <param name="fileInfo">FileInfo for file to check</param>
        /// <returns>True if file is accessible, false otherwise</returns>
        private bool IsFileAccessible(FileInfo fileInfo)
        {
            FileStream fs = null;

            try
            {
                fs = fileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                // Event log report
                if (verbose)
                    eventLog.WriteEntry("Unable to access file: " + fileInfo.Name, EventLogEntryType.Warning, EVENT_ID);

                return false;
            }
            finally
            {
                if (fs != null)
                    fs.Close();
            }

            return true;
        }

        //======================== Service Status

        /// <summary>
        /// Service state status codes
        /// </summary>
        public enum ServiceState
        {
            SERVICE_STOPPED = 0x00000001,
            SERVICE_START_PENDING = 0x00000002,
            SERVICE_STOP_PENDING = 0x00000003,
            SERVICE_RUNNING = 0x00000004,
            SERVICE_CONTINUE_PENDING = 0x00000005,
            SERVICE_PAUSE_PENDING = 0x00000006,
            SERVICE_PAUSED = 0x00000007,
        }

        /// <summary>
        /// Service Status struct
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ServiceStatus
        {
            public long dwServiceType;
            public ServiceState dwCurrentState;
            public long dwControlsAccepted;
            public long dwWin32ExitCode;
            public long dwServiceSpecificExitCode;
            public long dwCheckPoint;
            public long dwWaitHint;
        };

        /// <summary>
        /// Sets the service status
        /// </summary>
        /// <param name="handle">Service handle</param>
        /// <param name="serviceStatus">Service status code</param>
        /// <returns></returns>
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus serviceStatus);

        //========================= Needed to use self-signed servers

        /// <summary>
        /// Ensures that our custom certificate validation has been applied
        /// </summary>
        public static void EnsureCertificateValidation()
        {
            if (!initialized)
            {
                ServicePointManager.ServerCertificateValidationCallback += new System.Net.Security.RemoteCertificateValidationCallback(CustomCertificateValidation);
                initialized = true;
            }
        }

        /// <summary>
        /// Ensures that server certificate is authenticated
        /// </summary>
        private static bool CustomCertificateValidation(object sender, X509Certificate cert, X509Chain chain, System.Net.Security.SslPolicyErrors error)
        {
            return true;
        }
    }
}
