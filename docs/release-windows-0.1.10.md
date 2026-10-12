# Agent Watch for Windows 0.1.10

- **“Kiểm tra log” mở dashboard như trình duyệt.** Một dashboard cứ quay loading vẫn qua hết các bước kiểm tra cũ (đang chạy, Windows tới được cổng, doctor đạt), vì chưa bước nào hỏi trang lấy dữ liệu. Nay Agent Watch lấy một link mở trang dùng một lần từ dashboard đang chạy, đăng nhập từ Windows qua cổng của WSL, rồi gọi đúng những gì trang gọi khi mở: phiên đăng nhập, danh sách hội thoại, trạng thái đang chạy, tuỳ chọn cuộc trò chuyện mới, trạng thái công ty và kết nối trực tiếp (WebSocket). Mỗi bước ghi mã HTTP, mã lỗi và thời gian; bước “không trả lời sau 30s” chính là chỗ trang quay mãi.

Báo cáo vẫn không có key, tên model, link mở trang hay nội dung hội thoại.

Bản Windows (x64 và arm64) phát hành qua tag `windows-v0.1.10`. Không có bản macOS mới.

Kiểm tra trước phát hành: CI Windows trên commit được gắn tag; phần mở trang đã chạy với một dashboard Piagent thật (đăng nhập, năm lượt đọc, WebSocket).
