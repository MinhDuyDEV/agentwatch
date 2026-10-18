# Agent Watch for Windows 0.1.14

- **Dừng một lượt không còn làm hỏng các lượt sau.** Khi Studio không xác nhận được việc đóng run (mất kết nối, lỗi 5xx), broker từng giữ run cũ và từ chối mọi lần mở lượt mới cho đến khi mở lại Piagent. Giờ broker bỏ run đó; Studio tự đóng nó khi lease hết hạn. Đi cùng Agent Studio 0.28.1 và Piagent 1.29.0.

Bản Windows (x64 và arm64) phát hành qua tag `windows-v0.1.14`. Bản macOS mang cùng thay đổi là 0.15.7.

Kiểm tra trước phát hành: `AgentWatch.Core.Tests`; CI Windows trên commit được gắn tag.
