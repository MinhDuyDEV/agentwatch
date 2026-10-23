# Agent Watch for Windows 0.1.17

- **Bypass cho cuộc trò chuyện company, admin Studio bật/tắt được.** Watch chuyển công tắc **Bypass** trong Harness (`harness.configuration.bypass_mode`, Studio 0.38.0) sang Piagent 1.38.0. Admin tắt thì Piagent chỉ còn Hỏi trước và Toàn quyền.

Bản Windows (x64 và arm64) phát hành qua tag `windows-v0.1.17`. Bản macOS mang cùng thay đổi là 0.15.14.

Kiểm tra trước phát hành: `AgentWatch.Core.Tests` (56 test); CI Windows trên commit được gắn tag.
