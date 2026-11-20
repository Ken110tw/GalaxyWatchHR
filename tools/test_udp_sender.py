import json
import socket
import sys
import time


PORT = 19888


def discover_pc(timeout: float = 3.0) -> str | None:
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    sock.settimeout(timeout)
    try:
        probe = json.dumps({"type": "discover", "device": "SimulatedWatch"}).encode("utf-8")
        sock.sendto(probe, ("255.255.255.255", PORT))
        sock.sendto(probe, ("127.0.0.1", PORT))
        data, addr = sock.recvfrom(1024)
        payload = json.loads(data.decode("utf-8"))
        if payload.get("type") == "offer":
            print(f"[Discovery OK] Found PC '{payload.get('host')}' at {addr[0]}:{payload.get('port')}")
            return addr[0]
    except socket.timeout:
        print("[Discovery] Timeout - is WatchHROverlay.exe running?")
    finally:
        sock.close()
    return None


def send_sample_stream(target_ip: str, count: int = 8) -> None:
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sample_bpms = [72, 76, 84, 98, 118, 145, 132, 88]
    try:
        for i in range(count):
            bpm = sample_bpms[i % len(sample_bpms)]
            msg = json.dumps({
                "type": "hr",
                "bpm": bpm,
                "ts": int(time.time()),
                "battery": 88,
                "accuracy": 3,
            }).encode("utf-8")
            sock.sendto(msg, (target_ip, PORT))
            print(f"[Sent] -> {target_ip}:{PORT} bpm={bpm}")
            time.sleep(1.0)
    finally:
        sock.close()


if __name__ == "__main__":
    arg_target = sys.argv[1].strip() if len(sys.argv) > 1 else ""
    target = arg_target if (arg_target and arg_target.lower() != "auto") else discover_pc()
    if not target:
        sys.exit(1)
    count = int(sys.argv[2]) if len(sys.argv) > 2 else 8
    send_sample_stream(target, count)
