# Agent Watch for Windows 0.1.8

- **Nút “Kiểm tra log” ở trang Chẩn đoán.** Một lần bấm kiểm tra cả ba chặng và ghi rõ chặng nào lỗi: Agent Watch (kết nối, key, `agentwatch.exe`), đường tới Agent Studio (Studio trả lời, key, Harness, kèm mã lỗi), và Piagent trong WSL (bản cài, sandbox, binding còn khớp sau khi cập nhật, dashboard đang chạy bản nào, Windows có mở được cổng của WSL không, `piagent dashboard doctor`, lỗi khởi động gần nhất, `piagent studio --doctor`). Chặng nào lỗi vẫn kiểm tra tiếp chặng sau; không thay đổi cấu hình. **Sao chép thông tin hỗ trợ** chép luôn kết quả. Dòng lệnh: `agentwatch check-log`.
- **Không hiện tên model.** App chỉ ghi số model key được cấp; báo cáo kiểm tra log không có key, tên model hay nội dung hội thoại.

Dùng khi dashboard mở nhưng cứ quay “Đang mở Piagent…”: bấm **Chẩn đoán → Kiểm tra log** rồi gửi kết quả cho quản trị viên.

Bản Windows (x64 và arm64) phát hành qua tag `windows-v0.1.8`. Không có bản macOS mới.

Kiểm tra trước phát hành: CI Windows (build, test lõi, test bộ cài, smoke wizard) trên commit được gắn tag.
