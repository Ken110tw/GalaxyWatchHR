using System.Text.Json;

namespace WatchHROverlay.Models;

public sealed record HeartRatePacket(
    int Bpm,
    int? Battery,
    int? Accuracy,
    DateTime ReceivedAtUtc)
{
    public const int MaxPacketBytes = 1024;
    public const int MinValidBpm = 0;
    public const int MaxValidBpm = 250;

    public static bool TryParse(ReadOnlySpan<byte> utf8Payload, out HeartRatePacket? packet, out string? packetType)
    {
        packet = null;
        packetType = null;

        if (utf8Payload.IsEmpty || utf8Payload.Length > MaxPacketBytes)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(utf8Payload.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
            {
                packetType = typeProp.GetString();
            }

            if (string.Equals(packetType, "discover", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!root.TryGetProperty("bpm", out var bpmProp) || !bpmProp.TryGetInt32(out var bpm))
            {
                return false;
            }

            if (bpm < MinValidBpm || bpm > MaxValidBpm)
            {
                return false;
            }

            int? battery = null;
            if (root.TryGetProperty("battery", out var batProp) && batProp.TryGetInt32(out var batVal) && batVal is >= 0 and <= 100)
            {
                battery = batVal;
            }

            int? accuracy = null;
            if (root.TryGetProperty("accuracy", out var accProp) && accProp.TryGetInt32(out var accVal))
            {
                accuracy = accVal;
            }

            packetType ??= "hr";
            packet = new HeartRatePacket(bpm, battery, accuracy, DateTime.UtcNow);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
