# AgentWatch 0.15.14 (macOS)

- **Bypass cho cuộc trò chuyện company, admin Studio bật/tắt được.** Watch chuyển công tắc **Bypass** trong Harness (`harness.configuration.bypass_mode`, Studio 0.38.0) sang Piagent. Khi công tắc bật, Piagent 1.38.0 có thêm mức Bypass không hỏi gì (lệnh mạng, Docker, push, deploy, migration chạy luôn; lệnh thường vẫn trong sandbox, secret vẫn bị che). Admin tắt thì cuộc trò chuyện đang ở Bypass về Toàn quyền ở thao tác kế tiếp.

Tải `AgentWatchMac-0.15.14.zip` cho macOS 14 trở lên (Apple Silicon và Intel). `SHA256SUMS` và `release.json` xác định chính xác các file và source được phát hành. App được ký bằng chứng chỉ cố định "Agent Watch Signing".

Kiểm tra trước phát hành: `swift test` (359 test); build Release chạy ổn định 10 giây.
