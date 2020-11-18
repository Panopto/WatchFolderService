Panopto Watch Folder Service
============================
This service uses Panopto RESTful API and C# to accomplish upload to Panopto server.

This service watches the designated folder and uploads any files with designated extensions that has not been uploaded before or has been uploaded but had its last write time changed since the last upload.
When uploading file that is too big to be completely uploaded within the elapse time, the new sync processes called will hold until the previous upload has been completed.

This code is write as a reference implementation for Panopto users' custom application to upload files.

This can be also used as-is by installing the pre-compiled binary.

Setup with installer
--------------------
1. Download MSI installer package from https://github.com/Panopto/WatchFolderService/releases
1. Run the installer. Note that this does not start the service automatically.
1. Navigate to C:\Program Files (x86)\Panopto\PanoptoWatchFolderService if you are on a 64-bit machine or C:\Program Files\Panopto\PanoptoWatchFolderService for 32-bit machine.
1. Open WatchFolderService.exe.config to setup the service's configuration. See explanation below.
1. Run services.msc and find Panopto Watch Folder Service in the services list and start it. Since the service is not started when it is just installed, but it will start automatically the next time the system is booted.
1. Configuration can be changed at anytime but will only take effect when the service is restarted.

Configuration items
-------------------
- Server: URI of server uploading to
- InfoFilePath: path to the file to keep sync information, program will create a file at the path if the file does not exist
- WatchFolder: uri of folder to watch by the service
- UserID: user name to log into the server
- UserKey: password to log into the server
- FolderID: folder ID of the folder to upload to on the server
- Verbose: set to true to enable debug evenlog messages to appear in evenlog, false to only report important messages
- PartSize: amount of data to upload in each upload request
- ElapseTime: time interval between two calls by the service to check and sync the folder
- FileWaitTime: amount of time to wait since last time the LastWriteTime of a file has changed before uploading it (to ensure the file a complete file)
- UploadExtensions: extension types of files to be uploaded, any file that has extension type not mentioned or no extension will not be uploaded

Build environment
-----------------
This code can be built by Visual Studio 2019 Professional or upper editions. This solution automatically downloads AWS SDK and WiX by NuGet Package Manager.

Panopto does not test if Visual Studio community edition or older versions of Visual Studio may build this.


Tip for development
-------------------
You may install and uninstall the service without installer.

1. Open the Developer Command Prompt in Visual Studio Tools
1. Navigate to the folder containing the .exe file for the service
1. Use the command: installutil.exe WatchFolderService.exe
1. Run services.msc to view the full list of service. Find Panopto Watch Folder Service and start the service
1. The service can be uninstalled using the command: installutil.exe /u WatchFolderService.exe

