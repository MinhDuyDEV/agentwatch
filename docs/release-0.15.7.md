# AgentWatch 0.15.7 (macOS)

- **Agent Watch tự cập nhật xong là Piagent dùng được ngay.** Watch giờ phát hiện chính app của nó vừa được thay (so mã băm broker khi file mới hơn cấu hình) và ghi lại cấu hình trong vài giây, thay vì đợi vòng đồng bộ 5–10 phút.
- **Dừng một lượt không còn làm hỏng các lượt sau.** Khi Studio không xác nhận được việc đóng run (mất kết nối, lỗi 5xx), broker từng giữ run cũ và từ chối mọi lần mở lượt mới, nên cuộc trò chuyện công ty báo "Studio could not start the run [managed-broker:serverUnavailable]" cho đến khi mở lại Piagent. Giờ broker bỏ run đó; Studio tự đóng nó khi lease hết hạn. Đi cùng Agent Studio 0.28.1 (sửa nguyên nhân các đợt lỗi 503 khoảng một phút trên toàn Studio) và Piagent 1.29.0.

Tải `AgentWatchMac-0.15.7.zip` cho macOS 14 trở lên (Apple Silicon và Intel). `SHA256SUMS` và `release.json` xác định chính xác các file và source được phát hành. App được ký bằng chứng chỉ cố định "Agent Watch Signing".

Kiểm tra trước phát hành: `swift test`; trên máy thật mở `piagent studio`, dừng một lượt đang chạy lệnh shell rồi gửi tiếp.
