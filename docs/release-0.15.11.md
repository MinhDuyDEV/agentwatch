# AgentWatch 0.15.11 (macOS)

- **Lần đóng run Studio chưa xác nhận được gửi lại thật.** Khi đóng run lỗi (Studio không trả lời, lỗi 5xx), Watch trước đây bỏ luôn run đó, nên lần Piagent hỏi đóng lại trả về thành công mà không gửi gì tới Studio: Studio giữ run "đang chạy" tới khi hết lease và mất báo cáo quy trình. Giờ Watch giữ run chưa xác nhận để lần hỏi lại đến được Studio (đóng hai lần là an toàn ở Studio), tới khi Studio xác nhận hoặc tới lần bắt đầu run mới; run mới không bao giờ bị chặn. Watch báo `close-retry` cho Piagent biết. Piagent 1.37.1 nhận biết Watch cũ không có `close-retry` và không ghi run là đã đóng.

Tải `AgentWatchMac-0.15.11.zip` cho macOS 14 trở lên (Apple Silicon và Intel). `SHA256SUMS` và `release.json` xác định chính xác các file và source được phát hành. App được ký bằng chứng chỉ cố định "Agent Watch Signing".

Kiểm tra trước phát hành: `swift test` (357 test); build Release chạy ổn định 10 giây.
