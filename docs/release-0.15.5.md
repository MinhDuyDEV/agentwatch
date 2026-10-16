# AgentWatch 0.15.5 (macOS)

- **Sửa lỗi 0.15.4 tắt ngay khi mở.** Bản 0.15.4 tạo cửa sổ pet nổi trong lúc SwiftUI còn đang dựng app, nên macOS dừng app ngay lúc khởi động. Từ bản này, cửa sổ pet chỉ được tạo sau khi app mở xong.

Máy đã lên 0.15.4 không tự cập nhật được vì app không mở nổi: tải `AgentWatchMac-0.15.5.zip`, giải nén và thay `AgentWatchMac.app` trong Applications.

Tải `AgentWatchMac-0.15.5.zip` cho macOS 14 trở lên (Apple Silicon và Intel). `SHA256SUMS` và `release.json` xác định chính xác các file và source được phát hành. App được ký bằng chứng chỉ cố định "Agent Watch Signing".

Kiểm tra trước phát hành:
- `swift test`.
- Build app macOS (Swift 6), mở bản build: app chạy, cửa sổ chính và pet nổi hiện ra.
