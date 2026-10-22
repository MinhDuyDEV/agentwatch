# AgentWatch 0.15.12 (macOS)

- **Codex CLI 0.162.1 dùng được với key Studio.** Watch trước đây chỉ nhận đúng Codex CLI 0.155.1, nên máy có Codex tự cập nhật lên 0.162.1 báo "CLI chưa kiểm chứng" và không đồng bộ, không mở được Codex với key trực tiếp. Giờ Watch nhận cả 0.162.1 và 0.155.1; bản khác vẫn bị chặn trước khi nhận key. 0.162.1 đã được kiểm chứng với cấu hình Codex thật (endpoint, model, sandbox, quyền chạy; `notify` vẫn bị từ chối) và một lượt chạy thật qua cấu hình đồng bộ (lệnh lấy key của Watch, `/v1/responses`, danh mục model Codex của Studio). Màn hình kiểm tra hiện đúng phiên bản Codex trên máy.

Tải `AgentWatchMac-0.15.12.zip` cho macOS 14 trở lên (Apple Silicon và Intel). `SHA256SUMS` và `release.json` xác định chính xác các file và source được phát hành. App được ký bằng chứng chỉ cố định "Agent Watch Signing".

Kiểm tra trước phát hành: `swift test` (357 test, gồm preflight Codex 0.162.1 thật); build Release chạy ổn định 10 giây.
