Using Azure function:

Get File list on Sever:
POST to Azure Fucntion URL with Json Payload:

{
  "host": "192.168.39.147",
  "username": "tester",
  "password": "password",
  "operation": "listFiles"
}

Upload file to ftp server
POST to Azure Fucntion URL with Json Payload:

{
  "host": "192.168.39.147",
  "username": "tester",
  "password": "password",
  "operation": "upload",
  "uploadPath": "test.txt",
  "content": "This is a test file for my blog post about Azure Functions and SFTP!"
}

Download file to ftp server
POST to Azure Fucntion URL with Json Payload:

{
  "host": "192.168.39.147",
  "username": "tester",
  "password": "password",
  "operation": "download",
  "downloadPath": "test.txt"
}
