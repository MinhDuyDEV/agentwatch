# Agent Watch for Windows 0.1.11

- **“Kiểm tra log” đo luôn tiến trình Piagent khi trang đang chờ.** Trong lúc gọi các lượt đọc của trang dashboard, Agent Watch lấy 16 mẫu trong 8 giây của dashboard và của Gateway công ty trong Ubuntu: đang ngủ, đang chạy hay bị chặn chờ đĩa/file; đang ở lời gọi hệ thống nào và trên file nào; tiến trình con, thư mục làm việc, file đang mở. Tiến trình bị chặn chờ đĩa/file ở từ 4 mẫu trở lên được ghi là lỗi. Chỉ đọc `/proc` bằng tài khoản của thành viên; không cần gõ lệnh nào trong Ubuntu.

Báo cáo vẫn không có key, tên model, link mở trang hay nội dung hội thoại; có thể có đường dẫn file trên máy.

Bản Windows (x64 và arm64) phát hành qua tag `windows-v0.1.11`. Không có bản macOS mới.

Kiểm tra trước phát hành: CI Windows trên commit được gắn tag; lệnh đo đã chạy trên Linux x86_64 với tiến trình giả.
