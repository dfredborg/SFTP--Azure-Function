using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Renci.SshNet;
using Microsoft.AspNetCore.Http;
using ConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace SftpDemo
{
    public class SftpHttpTrigger
    {
        private readonly ILogger<SftpHttpTrigger> _logger;

        public SftpHttpTrigger(ILogger<SftpHttpTrigger> logger)
        {
            _logger = logger;
        }

        [Function("SftpHttpTrigger")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processing a request to connect to SFTP.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);

            // Get parameters from the request or use defaults
            string host = GetParameterValue(data, req, "host") ?? "localhost";
            string username = GetParameterValue(data, req, "username") ?? "sftpuser";
            string password = GetParameterValue(data, req, "password") ?? "password";
            string operation = GetParameterValue(data, req, "operation") ?? "listFiles";
            string portString = GetParameterValue(data, req, "port") ?? "22";
            int port = int.Parse(portString);

            var response = req.CreateResponse(HttpStatusCode.OK);
            var responseData = new Dictionary<string, object>();

            try
            {
                // Create connection info with password authentication
                ConnectionInfo connectionInfo = new ConnectionInfo(host, port, username,
                    new PasswordAuthenticationMethod(username, password));

                // Connect to SFTP
                using (var client = new SftpClient(connectionInfo))
                {
                    _logger.LogInformation($"Connecting to SFTP server at {host}:{port}...");
                    client.Connect();
                    _logger.LogInformation($"Connected to {host} as {username}");

                    // Based on the requested operation
                    switch (operation.ToLower())
                    {
                        case "listfiles":
                            // List files in the root directory or specified path
                            string path = GetParameterValue(data, req, "path") ?? ".";
                            var files = client.ListDirectory(path);

                            var filesList = new List<Dictionary<string, object>>();
                            foreach (var file in files)
                            {
                                filesList.Add(new Dictionary<string, object>
                                {
                                    { "name", file.Name },
                                    { "fullName", file.FullName },
                                    { "size", file.Length },
                                    { "lastModified", file.LastWriteTime },
                                    { "isDirectory", file.IsDirectory }
                                });
                            }

                            responseData.Add("files", filesList);
                            responseData.Add("path", path);
                            break;

                            client.ConnectionInfo.Timeout = TimeSpan.FromMinutes(5); // Extend timeout for operations

                        // For the upload case, make sure buffering is handled properly:
                        case "upload":
                            // Upload a sample file
                            string uploadPath = GetParameterValue(data, req, "uploadPath") ?? "uploaded_file.txt";
                            string content = GetParameterValue(data, req, "content") ?? "This is a test file uploaded from Azure Function!";

                            byte[] contentBytes = System.Text.Encoding.UTF8.GetBytes(content);

                            using (var memStream = new MemoryStream(contentBytes))
                            {
                                _logger.LogInformation($"Uploading file to {uploadPath} with {contentBytes.Length} bytes");
                                client.BufferSize = 4096; // Set an explicit buffer size
                                client.UploadFile(memStream, uploadPath, true); // Overwrite if exists
                                _logger.LogInformation("Upload completed successfully");
                            }

                            responseData.Add("uploadedFile", uploadPath);
                            responseData.Add("contentLength", contentBytes.Length);
                            break;

                        // For the download case, similar improvements:
                        case "download":
                            // Download a file
                            string downloadPath = GetParameterValue(data, req, "downloadPath");

                            if (string.IsNullOrEmpty(downloadPath))
                            {
                                response = req.CreateResponse(HttpStatusCode.BadRequest);
                                await response.WriteStringAsync("downloadPath parameter is required for download operation");
                                return response;
                            }

                            _logger.LogInformation($"Checking if file exists: {downloadPath}");
                            if (!client.Exists(downloadPath))
                            {
                                response = req.CreateResponse(HttpStatusCode.NotFound);
                                await response.WriteStringAsync($"File not found: {downloadPath}");
                                return response;
                            }

                            _logger.LogInformation($"Starting download of {downloadPath}");
                            client.BufferSize = 4096; // Set an explicit buffer size

                            using (var memStream = new MemoryStream())
                            {
                                client.DownloadFile(downloadPath, memStream);
                                memStream.Position = 0;

                                using (var reader = new StreamReader(memStream))
                                {
                                    string fileContent = reader.ReadToEnd();
                                    responseData.Add("fileName", downloadPath);
                                    responseData.Add("content", fileContent);
                                    responseData.Add("contentLength", fileContent.Length);
                                    _logger.LogInformation($"Successfully downloaded {fileContent.Length} bytes");
                                }
                            }
                            break;

                        default:
                            response = req.CreateResponse(HttpStatusCode.BadRequest);
                            await response.WriteStringAsync($"Unknown operation: {operation}");
                            return response;
                    }

                    client.Disconnect();
                    _logger.LogInformation("Disconnected from SFTP server");
                }

                responseData.Add("status", "success");
                await response.WriteAsJsonAsync(responseData);
            }
            catch (Exception ex)
            {
                _logger.LogError($"SFTP operation failed: {ex.Message}");
                response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { status = "error", message = ex.Message, stackTrace = ex.StackTrace });
            }

            return response;
        }

        private string GetParameterValue(dynamic data, HttpRequestData req, string paramName)
        {
            string queryValue = req.Url.Query.Contains(paramName + "=") ?
                req.Url.Query.Split(new[] { paramName + '=' }, StringSplitOptions.None)[1].Split('&')[0] :
                null;

            return data?[paramName] ?? queryValue;
        }
    }
}