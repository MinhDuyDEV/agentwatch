# AgentWatch 0.15.9 (macOS)

- **Chạy được với Piagent 1.33 (Pi 1.3.0).** Watch trước đây chỉ nhận Pi 0.87.1 khi nhập cấu hình công ty, nên sau khi Piagent chuyển lên Pi 1.3.0, lần nhập lại sẽ báo phiên bản không hỗ trợ. Giờ Watch nhận đúng bản Pi mà Piagent đang cài ghim (0.87.1 trước Piagent 1.33, 1.3.0 từ Piagent 1.33), trong danh sách bản đã kiểm tra.
- **Báo khi agent cần bạn.** Với Piagent 1.34 trở lên, Watch báo bằng thông báo của macOS khi một cuộc trò chuyện đang chờ bạn duyệt, đang hỏi bạn hoặc cần đăng nhập, và khi lượt của agent xong, dừng hoặc lỗi. Menu trên thanh trạng thái có mục "Đang chờ bạn". Watch chỉ đọc tên cuộc trò chuyện và tên project, không đọc nội dung.

Tải `AgentWatchMac-0.15.9.zip` cho macOS 14 trở lên (Apple Silicon và Intel). `SHA256SUMS` và `release.json` xác định chính xác các file và source được phát hành. App được ký bằng chứng chỉ cố định "Agent Watch Signing".

Kiểm tra trước phát hành: `swift test` (350 test); build Release chạy ổn định 10 giây.
