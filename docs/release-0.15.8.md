# AgentWatch 0.15.8 (macOS)

- **Watch không còn báo "Chưa thể xác minh" khi key vẫn tốt.** Studio đánh dấu `interrupted` cho lượt gọi bị ngắt giữa chừng (dừng lượt, mạng chậm bị cắt), tính theo usage lượt đó đã báo. Watch trước đây không đọc được trạng thái này. Chỉ cần một lượt như vậy trong 20 lượt gần nhất là bảng Usage bị từ chối, và cả kết nối Studio bị đánh là chưa xác minh. Giờ Usage hiện các lượt này với biểu tượng riêng ("Lượt bị ngắt giữa chừng").
- **Lỗi ở bảng Usage không còn làm mất xác minh key.** Chỉ key sai, đổi danh tính hoặc Studio không tương thích mới đưa kết nối về "Chưa thể xác minh". Một báo cáo Usage không đọc được chỉ hiện lỗi trong tab Usage.

Tải `AgentWatchMac-0.15.8.zip` cho macOS 14 trở lên (Apple Silicon và Intel). `SHA256SUMS` và `release.json` xác định chính xác các file và source được phát hành. App được ký bằng chứng chỉ cố định "Agent Watch Signing".

Kiểm tra trước phát hành: `swift test`; trên máy thật mở Studio → Chẩn đoán, trạng thái là "Đã xác minh" khi tài khoản có lượt bị ngắt trong tháng.
