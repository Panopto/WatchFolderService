Panopto-WatchFolderService
=====================

Panopto Watch Folder Service

This service uses Panopto RESTful API and C# to accomplish upload to server.

This service will need AWSSDK to run as it uses Amazon S3 Services.

AWSSDK can be downloaded from here: http://aws.amazon.com/s3/

This service uploads files using credentials stored in config file for its installer

This service watches the designated folder and uploads any .mp4 files that has not been uploaded before or has been uploaded but had its last write time changed since the last upload
