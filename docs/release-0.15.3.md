# AgentWatch 0.15.3 (macOS)

- **Mang `fast_mode` của Harness sang Piagent.** Khi Harness của team trong Agent Studio 0.26.0 cho phép Fast mode, Agent Watch chuyển cờ đó vào cấu hình công ty để Piagent 1.23.0 hiện nút Fast trong cuộc trò chuyện công ty. Watch chỉ chuyển cờ, không tự bật gì; Harness không có cờ thì Piagent không hiện nút.

Tải `AgentWatchMac-0.15.3.zip` cho macOS 14 trở lên (Apple Silicon và Intel). `SHA256SUMS` và `release.json` xác định chính xác các file và source được phát hành. App được ký bằng chứng chỉ cố định "Agent Watch Signing".

Bản Windows cùng thay đổi là `windows-v0.1.12`.

Kiểm tra trước phát hành:
- `swift test` trên máy ký (thêm kiểm tra: Harness có `fast_mode` thì cờ được chuyển sang Piagent, không có thì không gửi).
- Build app macOS (Swift 6).
