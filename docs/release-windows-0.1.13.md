# Agent Watch for Windows 0.1.13

- **Tự nối lại Piagent trong WSL sau khi Piagent cập nhật.** Bản nối giữa Piagent và key công ty ghim launcher Piagent và Node bằng SHA-256. Trước đây, sau mỗi lần cập nhật Piagent trong Ubuntu, `piagent studio` báo `managed-launch-binding-changed` cho đến khi thành viên bấm “Kiểm tra lại”. Giờ Agent Watch tự kiểm tra trong lúc chạy: 5 giây một lần khi có tiến trình Piagent trong bản WSL đã nối, 30 giây một lần khi không có. Thấy bản nối cũ là nối lại ngay, qua đúng các bước kiểm tra như khi bấm “Kiểm tra lại” (Studio, Harness, WSL2, sandbox, Pi 0.87.1, launcher và Node đọc lại trong WSL). Piagent 1.28.2 chờ tối đa 90 giây cho bước này, nên thành viên không phải làm gì.
- Lần kiểm tra chỉ đọc (file nối, file runtime của Piagent, SHA-256 của launcher và Node), không ghi gì. Agent Watch không kiểm tra khi bản WSL đang tắt, nên không làm Ubuntu khởi động hay chạy mãi. Nếu nối lại không được (Studio mất kết nối), Agent Watch thử lại mỗi phút; một bản cập nhật Piagent mới nữa thì thử ngay.

Bản Windows (x64 và arm64) phát hành qua tag `windows-v0.1.13`. Bản macOS mang cùng thay đổi là 0.15.6.

Kiểm tra trước phát hành: `AgentWatch.Core.Tests` (48/48) trong `mcr.microsoft.com/dotnet/sdk:8.0`, gồm chạy thật lệnh kiểm tra bằng `sh` trên thư mục tạm; app WPF build được với `EnableWindowsTargeting`; CI Windows trên commit được gắn tag.
