using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Logging;

namespace KKSavePoint.Features
{
    public static class RollbackSocketClient
    {
        private const int Port = 12345;

        public static async Task<RollbackResponse> ConnectToHostAsync(string hostAddress)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    Log.Info($"[KKSavePoint] Connecting to rollback server at {hostAddress}:{Port}");
                    await client.ConnectAsync(hostAddress, Port);
                    Log.Info("[KKSavePoint] Connected to rollback server");

                    using (var stream = client.GetStream())
                    {
                        var request = new RollbackRequest
                        {
                            Type = "ROLLBACK_QUERY",
                            Data = "Requesting lobby info"
                        };

                        var requestJson = JsonSerializer.Serialize(request);
                        var requestBytes = Encoding.UTF8.GetBytes(requestJson);
                        await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
                        Log.Info($"[KKSavePoint] Sent rollback request: {requestJson}");

                        var buffer = new byte[1024];
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        Log.Info($"[KKSavePoint] Received rollback response: {responseJson}");

                        var response = JsonSerializer.Deserialize<RollbackResponse>(responseJson);
                        return response ?? new RollbackResponse { Success = false, Message = "Failed to deserialize response" };
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Failed to connect to rollback server: {ex}");
                return new RollbackResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        public static async Task<RollbackResponse> ConnectToLocalHostAsync()
        {
            return await ConnectToHostAsync("127.0.0.1");
        }

        public static async Task<RollbackResponse> TryFindHostAsync(string[] possibleAddresses)
        {
            foreach (var address in possibleAddresses)
            {
                var response = await ConnectToHostAsync(address);
                if (response.Success)
                {
                    return response;
                }
                Log.Info($"[KKSavePoint] Failed to connect to {address}, trying next...");
            }

            return new RollbackResponse
            {
                Success = false,
                Message = "Could not find any rollback server"
            };
        }
    }
}