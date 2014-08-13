Panopto-WatchFolderService
=====================

Panopto Watch Folder Service

Installation and Setup using InstallShield:

	Navigate to WatchFolderServiceSetup\WatchFolderServiceSetup\Express\SingleImage\DiskImages\DISK1 and run setup.exe to install the service

	After installation, navigate to C:\Program Files (x86)\Panopto\PanoptoWatchFolderService if you are on a 64-bit machine or C:\Program Files\Panopto\PanoptoWatchFolderService for 32-bit machine

	Open WatchFolderService.exe.config to setup the service's configuration

	Here is an explanation of each field:

		Server: uri of server uploading to

		InfoFilePath: path to the file to keep sync information, program will create a file at the path if the file does not exist

		WatchFolder: uri of folder to watch by the service

		UserID: user name to log into the server

		UserKey: password to log into the server

		FolderID: folder ID of the folder to upload to on the server

		Verbose: set to true to enable debug evenlog messages to appear in evenlog, false to only report important messages

		PartSize: amount of data to upload in each upload request

		ElapseTime: time interval between two calls by the service to check and sync the folder

		FileWaitTime: amount of time to wait since last time the LastWriteTime of a file has changed before uploading it (to ensure the file a complete file)

		UploadExtensions: extension types of files to be uploaded, any file that has extension type not mentioned or no extension will not be uploaded

	After setting the configuration to desired values, run services.msc and find Panopto Watch Folder Service in the services list and start it (Since the service is not started when it is just installed, but it will start automatically the next time the system is booted)

	Configuration can be changed at anytime but will only take effect when the service is restarted. Run services.msc and find Panopto Watch Folder Service in services list and press restart to restart the service (or start if the service is stopped)
