using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using WatchHROverlay.Models;

namespace WatchHROverlay.Services;

public sealed class UdpHeartRateReceiver : IDisposable
{
    private readonly int _port;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public event Action<HeartRatePacket, IPEndPoint>? HeartRateReceived;
    public event Action<string>? StatusMessage;

    public UdpHeartRateReceiver(int port)
    {
        _port = port;
    }

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, _port));
        _udpClient.EnableBroadcast = true;

        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        if (_udpClient is null)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct).ConfigureAwait(false);
                await HandleDatagramAsync(result.Buffer, result.RemoteEndPoint, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                StatusMessage?.Invoke($"網路錯誤: {ex.SocketErrorCode}");
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleDatagramAsync(byte[] buffer, IPEndPoint remoteEndPoint, CancellationToken ct)
    {
        if (!HeartRatePacket.TryParse(buffer, out var packet, out var packetType))
        {
            return;
        }

        if (string.Equals(packetType, "discover", StringComparison.OrdinalIgnoreCase))
        {
            await SendDiscoveryOfferAsync(remoteEndPoint, ct).ConfigureAwait(false);
            return;
        }

        if (packet is not null)
        {
            HeartRateReceived?.Invoke(packet, remoteEndPoint);
        }
    }

    private async Task SendDiscoveryOfferAsync(IPEndPoint remoteEndPoint, CancellationToken ct)
    {
        if (_udpClient is null)
        {
            return;
        }

        var offer = new
        {
            type = "offer",
            host = Environment.MachineName,
            port = _port
        };

        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(offer));
        await _udpClient.SendAsync(payload, remoteEndPoint, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udpClient?.Dispose();
        _cts?.Dispose();
        _cts = null;
        _udpClient = null;
    }
}
