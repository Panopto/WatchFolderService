using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace WatchFolderService
{
    class UploadAPIWrapper
    {
        /// <summary>
        /// Upload file to given destination
        /// </summary>
        /// <param name="userName">user name</param>
        /// <param name="userPassword">user password</param>
        /// <param name="folderID">folder to upload to</param>
        /// <param name="sessionName">upload session display name</param>
        /// <param name="filePath">file to upload</param>
        /// <param name="partSize">size to be uploaded for each part of multipart upload</param>
        public static void UploadFile(string userName, string userPassword, string folderID, string sessionName, string filePath, long partSize)
        {
            string adminAuthCookie;
            try
            {
                adminAuthCookie = Common.LogonAndGetCookie(userName, userPassword);
            }
            catch (Exception)
            {
                throw new InvalidDataException("Login Failed: Cannot connect to server or invalid user name and password combination");
            }

            string sessionID;
            try
            {
                sessionID = CreateSession(adminAuthCookie, folderID, sessionName);
            }
            catch (Exception)
            {
                throw new InvalidDataException("Create Session Failed: Invalid folder ID");
            }

            Upload uploadInfo;
            try
            {
                uploadInfo = CreateUpload(adminAuthCookie, sessionID, sessionName);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Create Upload Failed: " + ex.Message);
            }

            try
            {
                UploadSingleFile(uploadInfo.UploadTarget, filePath, partSize);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Upload File Failed: " + ex.Message);
            }

            try
            {
                ProcessSession(uploadInfo, adminAuthCookie, sessionID);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Finalize Upload Failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Create a session
        /// </summary>
        /// <param name="authCookie">stored authentication cookie</param>
        /// <param name="parentFolderID">destination folder ID</param>
        /// <param name="sessionName">name of upload session</param>
        /// <returns>Session ID of this upload session</returns>
        public static string CreateSession(string authCookie, string parentFolderID, string sessionName)
        {
            Session body = new Session(sessionName, parentFolderID);
            HttpWebRequest request = Common.CreateRequest(
                "POST",
                "session",
                authCookie,
                Common.SerializeAsJson(body));

            Session response = Common.GetResponse<Session>(
                HttpStatusCode.Created,
                request);

            return response.ID.ToString();
        }

        /// <summary>
        /// Create an upload
        /// </summary>
        /// <param name="authCookie">authorization cookie</param>
        /// <param name="sessionID">file Session ID</param>
        /// <param name="sessionName">session display name</param>
        /// <returns>Upload struct containing upload info</returns>
        public static Upload CreateUpload(string authCookie, string sessionID, string sessionName)
        {
            Upload upload = Common.CreateRestObject<Upload>(
                authCookie,
                "upload",
                new Upload()
                {
                    SessionID = sessionID,
                    UploadTarget = sessionName
                });

            return upload;
        }

        /// <summary>
        /// Upload the file to given destination
        /// </summary>
        /// <param name="uploadTarget">Destination of upload</param>
        /// <param name="filePath">file to upload</param>
        public static void UploadSingleFile(string uploadTarget, string filePath, long partSize)
        {
            AmazonS3Client client = Common.CreateS3Client(uploadTarget);
            Amazon.S3.Model.InitiateMultipartUploadResponse response = Common.OpenUpload(client, uploadTarget, filePath);
            List<UploadPartResponse> partResponse = Common.UploadParts(client, uploadTarget, filePath, response.UploadId, partSize);
            Common.CloseUpload(client, uploadTarget, filePath, response.UploadId, partResponse);
        }

        /// <summary>
        /// Finish upload and tells server to start processing session
        /// </summary>
        /// <param name="upload">upload struct containing upload info</param>
        /// <param name="authCookie">authorization cookie</param>
        /// <param name="sessionID">upload Session ID</param>
        public static void ProcessSession(Upload upload, string authCookie, string sessionID)
        {
            Process process = Common.UpdateRestObject<Process>(
                authCookie,
                "upload",
                new Process()
                {
                    SessionID = sessionID,
                    ID = upload.ID,
                    UploadTarget = upload.UploadTarget,
                    State = 1
                });
        }
    }
}
