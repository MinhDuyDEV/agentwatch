# AgentWatch 0.15.10 (macOS)

- **Gửi lời sửa của bạn cho team memory (tùy chọn, mặc định tắt).** Bật ở tab Tools: khi bạn sửa Claude Code hay Codex hoặc dặn một quy tắc lâu dài, Watch gửi câu đó về Studio của bạn (`POST /v1/memory/signal`) để team memory học, sau khi đã làm sạch và che thông tin nhạy cảm; mỗi câu tối đa 1.000 ký tự, tối đa 20 câu một ngày, mỗi câu chỉ gửi một lần. Cuộc trò chuyện Piagent không bị gửi (Piagent tự báo). Cần key trực tiếp.
- **Xem memory đã đưa gì cho Piagent.** Trong chi tiết một phiên Piagent, Watch hiện các brief team memory đã đưa cho agent, và với key trực tiếp thì hiện cả cách member đánh giá chúng (`POST /v1/memory/served`). Cần Agent Studio 0.37.0.

Tải `AgentWatchMac-0.15.10.zip` cho macOS 14 trở lên (Apple Silicon và Intel). `SHA256SUMS` và `release.json` xác định chính xác các file và source được phát hành. App được ký bằng chứng chỉ cố định "Agent Watch Signing".

Kiểm tra trước phát hành: `swift test` (356 test); build Release chạy ổn định 10 giây.
