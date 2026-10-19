# Agent Watch for Windows 0.1.15

- **Chạy được với Piagent 1.33 (Pi 1.3.0) trong WSL.** Watch trước đây chỉ nhận Pi 0.87.1 và báo `wsl-runtime-unsupported` sau khi Piagent chuyển lên Pi 1.3.0. Giờ Watch nhận đúng bản Pi mà Piagent trong WSL ghim (0.87.1 trước Piagent 1.33, 1.3.0 từ Piagent 1.33).
- **Báo khi agent cần bạn.** Với Piagent 1.34 trở lên, Watch hiện thông báo ở khay hệ thống khi một cuộc trò chuyện trong WSL đang chờ bạn duyệt, đang hỏi bạn hoặc cần đăng nhập, và khi lượt của agent xong, dừng hoặc lỗi. Watch chỉ đọc khi bản WSL đang chạy và không tự khởi động nó.

Bản Windows (x64 và arm64) phát hành qua tag `windows-v0.1.15`. Bản macOS mang cùng thay đổi là 0.15.9.

Kiểm tra trước phát hành: `AgentWatch.Core.Tests` (56 test); CI Windows trên commit được gắn tag.
