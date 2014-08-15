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
using System.Net.NetworkInformation;

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
        private int fileWaitTime = 60;
        private long defaultPartsize = 1048576;
        private string[] extensions;
        private bool inputValid = true;
        private string inputFailureMessage = "";

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
            try
            {
                elapse = Convert.ToInt32(ConfigurationManager.AppSettings["ElapseTime"]);
                if (elapse < 0)
                {
                    inputValid = false;
                    inputFailureMessage += "\n\tElapseTime invalid";
                }
            }
            catch (Exception)
            {
                inputValid = false;
                inputFailureMessage += "\n\tElapseTime invalid";
            }
            try
            {
                fileWaitTime = Convert.ToInt32(ConfigurationManager.AppSettings["FileWaitTime"]);
                if (fileWaitTime < 0)
                {
                    inputValid = false;
                    inputFailureMessage += "\n\tFileWaitTime invalid";
                }
            }
            catch (Exception)
            {
                inputValid = false;
                inputFailureMessage += "\n\tFileWaitTime invalid";
            }
            try
            {
                defaultPartsize = Convert.ToInt64(ConfigurationManager.AppSettings["PartSize"]);
                if (defaultPartsize <= 0)
                {
                    inputValid = false;
                    inputFailureMessage += "\n\tPartSize cannot be less than or equal to 0";
                }
            }
            catch (Exception)
            {
                inputValid = false;
                inputFailureMessage += "\n\tPartSize invalid";
            }
            try
            {
                verbose = Convert.ToBoolean(ConfigurationManager.AppSettings["Verbose"]);
            }
            catch (Exception)
            {
                inputValid = false;
                inputFailureMessage += "\n\tVerbose value invalid";
            } 
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

            // Check input values
            bool hasInvalidInput = false;
            if (!Directory.Exists(Path.GetDirectoryName(infoFilePath)))
            {
                hasInvalidInput = true;
                eventLog.WriteEntry("Invalid directory path for info file", EventLogEntryType.Warning, 0);
            }
            if (!Directory.Exists(watchFolder))
            {
                hasInvalidInput = true;
                eventLog.WriteEntry("WatchFolder does not exist", EventLogEntryType.Warning, 0);
            }
            if (userID.Length == 0 || userKey.Length == 0)
            {
                hasInvalidInput = true;
                eventLog.WriteEntry("Invalid user name or password", EventLogEntryType.Warning, 0);
            }
            if (folderID.Length == 0)
            {
                hasInvalidInput = true;
                eventLog.WriteEntry("Invalid Folder ID", EventLogEntryType.Warning, 0);
            }
            if (!inputValid)
            {
                hasInvalidInput = true;
                eventLog.WriteEntry("Invalid Input:" + inputFailureMessage, EventLogEntryType.Warning, 0);
            }
            if ((extensions.Length == 1 && extensions[0].Trim().Length == 0) || extensions.Length == 0)
            {
                hasInvalidInput = true;
                eventLog.WriteEntry("Invalid extensions", EventLogEntryType.Warning, 0);
            }

            if (!hasInvalidInput)
            {
                // Setup timer
                System.Timers.Timer timer = new System.Timers.Timer();
                timer.Interval = elapse;
                timer.Elapsed += new System.Timers.ElapsedEventHandler(OnTimer);
                timer.Start();
            }

            // Update the service state to Running.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_RUNNING;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            if (hasInvalidInput)
            {
                this.Stop();
            }
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
                Dictionary<string, SyncInfo> folderInfo = GetLastSyncInfo();
                Dictionary<string, SyncInfo> uploadFiles = new Dictionary<string, SyncInfo>();

                // Check files in folder for sync
                foreach (FileInfo fileInfo in GetFileInfo(dirInfo))
                {
                    if (verbose)
                    {
                        eventLog.WriteEntry("Checking File: " + fileInfo.Name, EventLogEntryType.Information, EVENT_ID);
                    }

                    bool found = false;
                    bool inSync = false;
                    bool isStable = false;

                    // Check if file exists in folderInfo
                    if (folderInfo.ContainsKey(fileInfo.Name))
                    {
                        if (verbose)
                        {
                            eventLog.WriteEntry(fileInfo.Name + " found", EventLogEntryType.Information, EVENT_ID);
                        }

                        found = true;
                        SyncInfo syncInfo = folderInfo[fileInfo.Name];
                        DateTime stored = syncInfo.LastSyncWriteTime;
                        DateTime current = fileInfo.LastWriteTime;
                        current = new DateTime(current.Ticks - (current.Ticks % TimeSpan.TicksPerSecond), current.Kind);

                        if (verbose)
                        {
                            eventLog.WriteEntry("stored: " + stored.ToString("G") +
                                                "\ncurrent: " + current.ToString("G") +
                                                "\nNewSyncWriteTime" + syncInfo.NewSyncWriteTime.ToString("G") +
                                                "\n" + syncInfo.NewSyncWriteTime.Equals(current), EventLogEntryType.Information, EVENT_ID);
                        }

                        // Check if file is in sync
                        if (stored.Equals(current))
                        {
                            if (verbose)
                            {
                                eventLog.WriteEntry(fileInfo.Name + " in sync", EventLogEntryType.Information, EVENT_ID);
                            }

                            inSync = true;
                        }
                        else
                        {
                            // Check if file's new LastWriteTime is the same as recorded
                            // Wait for given fileWaitTime amount of time before indicating its stable and ready for upload
                            // If not the same, wait time will be reseted
                            if (syncInfo.NewSyncWriteTime.Equals(current))
                            {
                                if (verbose)
                                {
                                    eventLog.WriteEntry("New write time in sync with record", EventLogEntryType.Information, EVENT_ID);
                                }

                                if (syncInfo.FileStableTime >= fileWaitTime)
                                {
                                    if (verbose)
                                    {
                                        eventLog.WriteEntry(fileInfo.Name + " is stable", EventLogEntryType.Information, EVENT_ID);
                                    }

                                    isStable = true;
                                }
                                else
                                {
                                    int timePassed = (int)Math.Ceiling(elapse / 1000.0);

                                    if (verbose)
                                    {
                                        eventLog.WriteEntry("Adding time to stable time: " + timePassed, EventLogEntryType.Information, EVENT_ID);
                                    }

                                    syncInfo.FileStableTime += timePassed;
                                }
                            }
                            else
                            {
                                syncInfo.NewSyncWriteTime = new DateTime(current.Ticks);
                                syncInfo.FileStableTime = 0;
                            }
                        }
                    }

                    // Add file to sync queue if not in sync
                    if (!inSync)
                    {
                        if (found)
                        {
                            if (isStable)
                            {
                                uploadFiles.Add(fileInfo.FullName, new SyncInfo(folderInfo[fileInfo.Name]));
                                folderInfo[fileInfo.Name].LastSyncWriteTime = fileInfo.LastWriteTime;
                                folderInfo[fileInfo.Name].FileStableTime = -1;
                            }
                        }
                        else
                        {
                            if (fileWaitTime == 0)
                            {
                                uploadFiles.Add(fileInfo.FullName, new SyncInfo(DateTime.MinValue, fileInfo.LastWriteTime, 0));
                                folderInfo.Add(fileInfo.Name, new SyncInfo(DateTime.MinValue, fileInfo.LastWriteTime, -1));
                            }
                            else
                            {
                                folderInfo.Add(fileInfo.Name, new SyncInfo(DateTime.MinValue, fileInfo.LastWriteTime, 0));
                            }
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
        private void ProcessUpload(Dictionary<string, SyncInfo> uploadFiles, Dictionary<string, SyncInfo> folderInfo)
        {
            // Upload each file and handle any exception
            foreach (string filePath in uploadFiles.Keys)
            {
                try
                {
                    // Event Log Record
                    if (verbose)
                    {
                        eventLog.WriteEntry("Uploading: " + filePath, EventLogEntryType.Information, EVENT_ID);
                        eventLog.WriteEntry("Upload Param: " + userID + ", " + "USER PASSWORD" + ", " + folderID + ", " + Path.GetFileName(filePath) +
                                            ", " + filePath + ", " + defaultPartsize, EventLogEntryType.Information, EVENT_ID);
                    }

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
                    {
                        eventLog.WriteEntry("Stack Trace: " + ex.StackTrace, EventLogEntryType.Warning, EVENT_ID);
                    }

                    folderInfo[Path.GetFileName(filePath)] = uploadFiles[filePath];
                }
            }
        }

        /// <summary>
        /// Generate a Dictionary containing files in the folder and their last write time base on sync info file's content
        /// </summary>
        /// <returns>Dictionary containing files in folder and their last write time</returns>
        private Dictionary<string, SyncInfo> GetLastSyncInfo()
        {
            Dictionary<string, SyncInfo> folderInfo = new Dictionary<string, SyncInfo>();

            if (!File.Exists(Path.GetFullPath(infoFilePath)))
            {
                return folderInfo;
            }

            // Put info in sync file into dictionary for use
            string[] infoFileLines = File.ReadAllLines(Path.GetFullPath(infoFilePath));

            foreach (string line in infoFileLines)
            {
                if (verbose)
                {
                    eventLog.WriteEntry("Reading Line: " + line, EventLogEntryType.Information, EVENT_ID);
                }

                string[] info = line.Split(';');
                if (info.Length != 4)
                {
                    continue;
                }

                SyncInfo syncInfo = new SyncInfo();
                syncInfo.LastSyncWriteTime = GetDateTime(info[1]);
                syncInfo.NewSyncWriteTime = GetDateTime(info[2]);
                syncInfo.FileStableTime = Convert.ToInt32(info[3]);

                folderInfo.Add(info[0], syncInfo);
            }

            return folderInfo;
        }

        /// <summary>
        /// Store into InfoFile the files and last write time contained in parameter info
        /// </summary>
        /// <param name="info">Dictionary that stores files and their last write time</param>
        private void SetSyncInfo(Dictionary<string, SyncInfo> info)
        {
            using (System.IO.StreamWriter infoFile = new System.IO.StreamWriter(Path.GetFullPath(infoFilePath), false))
            {
                foreach (string fileName in info.Keys)
                {
                    string line = fileName + ";" + info[fileName].LastSyncWriteTime.ToString("G") + ";" 
                                  + info[fileName].NewSyncWriteTime.ToString("G") + ";" + info[fileName].FileStableTime;

                    if (verbose)
                    {
                        eventLog.WriteEntry("Writing to InfoFile: " + line, EventLogEntryType.Information, EVENT_ID);
                    }
                    
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
                {
                    eventLog.WriteEntry("Unable to access file: " + fileInfo.Name, EventLogEntryType.Warning, EVENT_ID);
                }

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
