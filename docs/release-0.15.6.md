# AgentWatch 0.15.6 (macOS)

- **Piagent cập nhật xong là dùng được ngay.** Agent Watch ghim mã băm của trình khởi chạy Piagent; trước đây sau mỗi lần Piagent cập nhật, Watch chỉ ghi lại cấu hình ở lần đồng bộ kế tiếp (tối đa khoảng 5 phút), và trong lúc đó `piagent studio` báo `managed-launch-binding-changed`. Từ bản này Watch kiểm tra mỗi 5 giây và ghi lại cấu hình ngay khi trình khởi chạy đổi (tối đa một lần mỗi phút nếu chưa ghi được, ví dụ khi mất mạng). Cùng Piagent 1.28.2 (chờ Watch tối đa 90 giây), member không còn phải nhập lại cấu hình bằng tay.

Tải `AgentWatchMac-0.15.6.zip` cho macOS 14 trở lên (Apple Silicon và Intel). `SHA256SUMS` và `release.json` xác định chính xác các file và source được phát hành. App được ký bằng chứng chỉ cố định "Agent Watch Signing".

Kiểm tra trước phát hành:
- `swift test` (344 test).
- Trên máy thật: đổi trình khởi chạy Piagent rồi chạy ngay `piagent studio --check`: Watch ghi lại cấu hình trong vài giây và lệnh chạy được mà không cần nhập lại.
