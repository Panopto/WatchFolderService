Panopto-WatchFolderService
=====================

Panopto Watch Folder Service

This service uses Panopto RESTful API and C# to accomplish upload to server.

This service will need AWSSDK to run as it uses Amazon S3 Services.

AWSSDK can be downloaded from here: http://aws.amazon.com/s3/

Most options explained below can be found and modified in the App.config or WatchFolderService.exe.config file

This service uploads files using credentials stored in config file for its installer

This service watches the designated folder and uploads any files with designated extensions that has not been uploaded before or has been uploaded but had its last write time changed since the last upload

When uploading file that is too big to be completely uploaded within the elapse time, the new sync processes called will hold until the previous upload has been completed, this is achieved using a lock

Using the service: 
	Use the Developer Command Prompt in Visual Studio Tools to install the service's .exe file; navigate to the folder containing the .exe file for the service and use the command: installutil.exe WatchFolderService.exe

	Run services.msc to view the full list of service. Find Panopto Watch Folder Service and start the service

	The service can be uninstalled using the command: installutil.exe /u WatchFolderService.exe

	More information about windows service can be found at: http://msdn.microsoft.com/en-us/library/zt39148a(v=vs.110).aspx