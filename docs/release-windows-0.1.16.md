# Agent Watch for Windows 0.1.16

- **Lần đóng run Studio chưa xác nhận được gửi lại thật.** Khi đóng run lỗi (Studio không trả lời, lỗi 5xx), Watch trước đây bỏ luôn run đó, nên lần Piagent hỏi đóng lại trả về thành công mà không gửi gì tới Studio. Giờ Watch giữ run chưa xác nhận để lần hỏi lại đến được Studio, tới khi Studio xác nhận hoặc tới lần bắt đầu run mới; run mới không bao giờ bị chặn. Watch báo `close-retry` cho Piagent biết.

Bản Windows (x64 và arm64) phát hành qua tag `windows-v0.1.16`. Bản macOS mang cùng thay đổi là 0.15.11.

Kiểm tra trước phát hành: `AgentWatch.Core.Tests` (56 test); CI Windows trên commit được gắn tag.
