using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;
using System.Configuration;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Globalization;
using System.ComponentModel;
using System.Threading;

namespace WatchFolderService
{
    public partial class WatchFolderService : ServiceBase
    {
        private const string DATETIME_FORMAT = "MM/dd/yyyy HH:mm:ss";
        private static bool SELF_SIGNED = false; // Set this if the server does not have the cert from trusted root
        private static bool initialized = false;

        // Upload required information
        private string server = null;
        private string infoFilePath = null;
        private string watchFolder = null;
        private string userID = null;
        private string userKey = null;
        private string folderID = null;
        private TimeSpan monitorInterval = TimeSpan.Zero;
        private Thread workerThread = null;
        private ManualResetEvent stopRequested = null;
        private int fileWaitTime = 60;
        private long defaultPartsize = 1048576;
        private string[] extensions;
        private bool inputValid = true;
        private string inputFailureMessage = "";
        private int maxNumberOfAttempts = 3;
        private bool verbose = false;

        // This name must be something different from "PanoptoWatchFolderService".
        // That name was once used associated with custom log and the association seems
        // persistent forever even after DeleteEventSource is called.
        // Giving up using the same name and use a new one.
        private const string EventLogSourceName = "WatchFolderService";

        public WatchFolderService()
        {
            InitializeComponent();

            // Setup event log
            this.AutoLog = true;
            ((ISupportInitialize)this.EventLog).BeginInit();
            if (!EventLog.SourceExists(WatchFolderService.EventLogSourceName))
            {
                EventLog.CreateEventSource(WatchFolderService.EventLogSourceName, "Application");
            }
            ((ISupportInitialize)this.EventLog).EndInit();

            this.EventLog.Source = WatchFolderService.EventLogSourceName;
            this.EventLog.Log = "Application";

            // Parse config file
            server = ConfigurationManager.AppSettings["Server"];
            infoFilePath = ConfigurationManager.AppSettings["InfoFilePath"];
            watchFolder = ConfigurationManager.AppSettings["WatchFolder"];
            userID = ConfigurationManager.AppSettings["UserID"];
            userKey = ConfigurationManager.AppSettings["UserKey"];
            folderID = ConfigurationManager.AppSettings["FolderID"];
            try
            {
                this.monitorInterval = TimeSpan.FromMilliseconds(
                    Convert.ToInt32(ConfigurationManager.AppSettings["ElapseTime"]));
                if (this.monitorInterval <= TimeSpan.Zero)
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
            try
            {
                maxNumberOfAttempts = Int32.Parse(ConfigurationManager.AppSettings["AllowedNumberFailedAttempts"]);
                if (maxNumberOfAttempts < 1)
                {
                    inputValid = false;
                    inputFailureMessage += "\n\tAllowedNumberFailedAttempts invalid";
                }
            }
            catch (Exception)
            {
                inputValid = false;
                inputFailureMessage += "\n\tAllowedNumberFailedAttempts invalid";
            }
            Common.SetServer(server);

            if (SELF_SIGNED)
            {
                // For self-signed servers
                EnsureCertificateValidation();
            }
        }

        public void RunInteractive(string[] args)
        {
            this.OnStart(args);
            Console.WriteLine("Press any key to stop WatchFolderService in interactive mode.");
            Console.Read();
            this.OnStop();
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

            this.EventLog.WriteEntry("Service Started Successfully"); // Event Log Record

            // Check input values
            bool hasInvalidInput = false;
            if (!Directory.Exists(Path.GetDirectoryName(infoFilePath)))
            {
                hasInvalidInput = true;
                this.EventLog.WriteEntry("Invalid directory path for info file", EventLogEntryType.Warning);
            }
            if (!Directory.Exists(watchFolder))
            {
                hasInvalidInput = true;
                this.EventLog.WriteEntry("WatchFolder does not exist", EventLogEntryType.Warning);
            }
            if (userID.Length == 0 || userKey.Length == 0)
            {
                hasInvalidInput = true;
                this.EventLog.WriteEntry("Invalid user name or password", EventLogEntryType.Warning);
            }
            if (folderID.Length == 0)
            {
                hasInvalidInput = true;
                this.EventLog.WriteEntry("Invalid Folder ID", EventLogEntryType.Warning);
            }
            if (!inputValid)
            {
                hasInvalidInput = true;
                this.EventLog.WriteEntry("Invalid Input:" + inputFailureMessage, EventLogEntryType.Warning);
            }
            if ((extensions.Length == 1 && extensions[0].Trim().Length == 0) || extensions.Length == 0)
            {
                hasInvalidInput = true;
                this.EventLog.WriteEntry("Invalid extensions", EventLogEntryType.Warning);
            }

            if (!hasInvalidInput)
            {
                // Start up worker thread
                this.stopRequested = new ManualResetEvent(false);
                this.workerThread = new Thread(this.WorkerThreadFunction);
                this.workerThread.Start();
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
            // Stop worker thread
            if (this.workerThread != null)
            {
                this.EventLog.WriteEntry("Service stop requested. Waiting for the worker thread to exit."); // Event Log Record
                this.stopRequested.Set();
                this.workerThread.Join();
            }

            // Update the service state to Start Pending.
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOP_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            this.EventLog.WriteEntry("Service Stopped Successfully"); // Event Log Record
            this.EventLog.Close();

            // Update the service state to Running.
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOPPED;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        /// <summary>
        /// Worker thread that syncs the folder that happens on given interval
        /// </summary>
        private void WorkerThreadFunction()
        {
            while (!this.stopRequested.WaitOne(0))
            {
                try
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
                            this.EventLog.WriteEntry("Checking File: " + fileInfo.Name, EventLogEntryType.Information);
                        }

                        bool found = false;
                        bool inSync = false;
                        bool isStable = false;
                        bool maxFailedAttemptReached = false;
                        // Check if file exists in folderInfo
                        if (folderInfo.ContainsKey(fileInfo.Name))
                        {
                            if (verbose)
                            {
                                this.EventLog.WriteEntry(fileInfo.Name + " found", EventLogEntryType.Information);
                            }

                            found = true;
                            SyncInfo syncInfo = folderInfo[fileInfo.Name];
                            DateTime stored = syncInfo.LastSyncWriteTime;
                            DateTime current = fileInfo.LastWriteTime;
                            current = new DateTime(current.Ticks - (current.Ticks % TimeSpan.TicksPerSecond), current.Kind);

                            if (verbose)
                            {
                                this.EventLog.WriteEntry("stored: " + stored.ToString("G") +
                                                    "\ncurrent: " + current.ToString("G") +
                                                    "\nNewSyncWriteTime" + syncInfo.NewSyncWriteTime.ToString("G") +
                                                    "\n" + syncInfo.NewSyncWriteTime.Equals(current), EventLogEntryType.Information);
                            }

                            if (syncInfo.NumberOfAttempts >= maxNumberOfAttempts)
                            {
                                maxFailedAttemptReached = true;

                                if (verbose)
                                {
                                    this.EventLog.WriteEntry("Max retry attempts has been reached.", EventLogEntryType.Information);
                                }
                            }

                            // Check if file is in sync
                            if (stored.Equals(current))
                            {
                                if (verbose)
                                {
                                    this.EventLog.WriteEntry(fileInfo.Name + " in sync", EventLogEntryType.Information);
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
                                        this.EventLog.WriteEntry("New write time in sync with record", EventLogEntryType.Information);
                                    }

                                    if (syncInfo.FileStableTime >= fileWaitTime)
                                    {
                                        if (verbose)
                                        {
                                            this.EventLog.WriteEntry(fileInfo.Name + " is stable", EventLogEntryType.Information);
                                        }

                                        isStable = true;
                                    }
                                    else
                                    {
                                        int timePassed = (int)this.monitorInterval.TotalSeconds;

                                        if (verbose)
                                        {
                                            this.EventLog.WriteEntry("Adding time to stable time: " + timePassed, EventLogEntryType.Information);
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
                                if (isStable && !maxFailedAttemptReached)
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
                }
                catch (Exception e)
                {
                    this.EventLog.WriteEntry("Exception caught on worker thread: " + e.Message, EventLogEntryType.Error);
                    // Continue worker thread
                }

                Thread.Sleep(this.monitorInterval);
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
                        this.EventLog.WriteEntry("Uploading: " + filePath, EventLogEntryType.Information);
                        this.EventLog.WriteEntry("Upload Param: " + userID + ", " + "USER PASSWORD" + ", " + folderID + ", " + Path.GetFileName(filePath) +
                                            ", " + filePath + ", " + defaultPartsize, EventLogEntryType.Information);
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
                    this.EventLog.WriteEntry("Uploading " + filePath + " Failed: " + ex.Message,
                                            EventLogEntryType.Warning);
                    if (verbose)
                    {
                        this.EventLog.WriteEntry("Stack Trace: " + ex.StackTrace, EventLogEntryType.Warning);
                    }

                    // Increment number of attempt
                    uploadFiles[filePath].NumberOfAttempts++;

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
                    this.EventLog.WriteEntry("Reading Line: " + line, EventLogEntryType.Information);
                }

                string[] info = line.Split(';');
                SyncInfo syncInfo = new SyncInfo();

                // Backward Compatibility for older versions with no number of attempts associated in textfile
                if (info.Length == 4)
                {
                    syncInfo.NumberOfAttempts = 0;
                }
                else if (info.Length == 5)
                {
                    syncInfo.NumberOfAttempts = Convert.ToInt32(info[4]);
                }
                if (info.Length != 5)
                {
                    continue;
                }

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
                    string line =
                        fileName + ";"
                        + info[fileName].LastSyncWriteTime.ToString(DATETIME_FORMAT, CultureInfo.InvariantCulture) + ";" 
                        + info[fileName].NewSyncWriteTime.ToString(DATETIME_FORMAT, CultureInfo.InvariantCulture) + ";"
                        + info[fileName].FileStableTime + ";"
                        + info[fileName].NumberOfAttempts;

                    if (verbose)
                    {
                        this.EventLog.WriteEntry("Writing to InfoFile: " + line, EventLogEntryType.Information);
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
            return DateTime.ParseExact(timeString,DATETIME_FORMAT,CultureInfo.InvariantCulture);
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
                    this.EventLog.WriteEntry("Unable to access file: " + fileInfo.Name, EventLogEntryType.Warning);
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
        public enum ServiceState : uint
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
            public uint dwServiceType;
            public ServiceState dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
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
