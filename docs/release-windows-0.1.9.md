# Agent Watch for Windows 0.1.9

- **Bấm “Kiểm tra log” không còn làm app tắt.** Ở 0.1.8, app đóng ngay khi vẽ kết quả kiểm tra: app chạy ở chế độ không dùng văn hoá vùng, còn phần vẽ chữ của WPF cần tạo văn hoá “en” khi đo font và ký tự của báo cáo. App nay nhận tên văn hoá đó.
- **Kết quả đọc và chép được.** Thẻ “Kết quả kiểm tra log” là ô chữ bôi đen được, có nút **Sao chép kết quả** và **Mở file log**. Kết quả được ghi sau từng bước vào `%LOCALAPPDATA%\AgentWatch\support-check.txt` (`agentwatch check-log` cũng ghi file này).
- **Lỗi chưa xử lý không còn tắt im lặng.** Lỗi được ghi vào `%LOCALAPPDATA%\AgentWatch\crash.log`; lỗi trên cửa sổ thì app báo và chạy tiếp. Lần kiểm tra log sau ghi “Lỗi app gần nhất”.

Bản Windows (x64 và arm64) phát hành qua tag `windows-v0.1.9`. Không có bản macOS mới.

Kiểm tra trước phát hành: CI Windows (build, test lõi, test bộ cài, smoke wizard) trên commit được gắn tag; smoke nay bấm **Kiểm tra log** trên Windows thật và hỏng khi thiếu bản sửa.
