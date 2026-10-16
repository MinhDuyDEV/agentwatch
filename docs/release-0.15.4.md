# AgentWatch 0.15.4 (macOS)

- **Trang Usage không còn hiện model.** Bỏ thẻ "Theo model · tháng này", ô chọn model ở Hạn mức key và tên model trong Request gần đây. Trang chỉ còn số token, request, hạn mức của key và mã request; model nào được dùng xem trong Agent Studio, giống bản Windows.

Tải `AgentWatchMac-0.15.4.zip` cho macOS 14 trở lên (Apple Silicon và Intel). `SHA256SUMS` và `release.json` xác định chính xác các file và source được phát hành. App được ký bằng chứng chỉ cố định "Agent Watch Signing".

Kiểm tra trước phát hành:
- `swift test` trên máy ký.
- Build app macOS (Swift 6).
