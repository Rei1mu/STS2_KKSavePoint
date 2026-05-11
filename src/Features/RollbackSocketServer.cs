using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Logging;

namespace KKSavePoint.Features
{
    public static class RollbackSocketServer
    {
        private static TcpListener _listener;
        private static bool _isRunning;
        private const int Port = 12345;

        public static void Start()
        {
            if (_isRunning) return;

            try
            {
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Start();
                _isRunning = true;
                Log.Info($"[KKSavePoint] Rollback socket server started on port {Port}");
                
                Task.Run(AcceptClientsAsync);
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Failed to start rollback socket server: {ex}");
            }
        }

        public static void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _listener?.Stop();
            Log.Info("[KKSavePoint] Rollback socket server stopped");
        }

        private static async Task AcceptClientsAsync()
        {
            while (_isRunning)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    Log.Info($"[KKSavePoint] New rollback client connected: {client.Client.RemoteEndPoint}");
                    _ = HandleClientAsync(client);
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        Log.Error($"[KKSavePoint] Error accepting rollback client: {ex}");
                    }
                }
            }
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var buffer = new byte[1024];
                    int bytesRead;

                    while (_isRunning && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        Log.Info($"[KKSavePoint] Received rollback message: {message}");
                        
                        await ProcessMessage(message, stream);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Error handling rollback client: {ex}");
            }
            finally
            {
                Log.Info($"[KKSavePoint] Rollback client disconnected: {client.Client.RemoteEndPoint}");
            }
        }

        private static async Task ProcessMessage(string message, NetworkStream stream)
        {
            try
            {
                var response = new RollbackResponse
                {
                    Success = true,
                    Message = "Message received",
                    LobbyId = SavePointFeature.PendingHostLobbyId?.ToString() ?? "",
                    SteamId = SavePointFeature.PendingHostSteamId?.ToString() ?? ""
                };

                var responseJson = JsonSerializer.Serialize(response);
                var responseBytes = Encoding.UTF8.GetBytes(responseJson);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                Log.Info($"[KKSavePoint] Sent rollback response: {responseJson}");
            }
            catch (Exception ex)
            {
                Log.Error($"[KKSavePoint] Error processing rollback message: {ex}");
            }
        }
    }

    public class RollbackRequest
    {
        public string Type { get; set; }
        public string Data { get; set; }
    }

    public class RollbackResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string LobbyId { get; set; }
        public string SteamId { get; set; }
    }
}