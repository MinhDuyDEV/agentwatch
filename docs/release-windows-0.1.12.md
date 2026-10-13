# Agent Watch for Windows 0.1.12

- **Mang `fast_mode` của Harness sang Piagent.** Khi Harness của team trong Agent Studio 0.26.0 cho phép Fast mode, Agent Watch chuyển cờ đó vào cấu hình công ty để Piagent 1.23.0 hiện nút Fast. Agent Watch chỉ chuyển cờ, không tự bật gì; Harness không có cờ thì Piagent không hiện nút.

Bản Windows (x64 và arm64) phát hành qua tag `windows-v0.1.12`. Bản macOS mang cùng thay đổi (`StudioHarness.Configuration.fastMode`) được phát hành riêng từ máy ký.

Kiểm tra trước phát hành: `AgentWatch.Core.Tests` (44/44) trong `mcr.microsoft.com/dotnet/sdk:8.0`; CI Windows trên commit được gắn tag.
