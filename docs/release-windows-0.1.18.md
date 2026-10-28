# Agent Watch for Windows 0.1.18

- **Subagent song song cho cuộc trò chuyện company.** Khi Agent Studio 0.50.0 trở lên báo `helper_slots`, broker giữ tối đa 3 grant cho cùng một role helper (slot 2 và 3 bên cạnh slot đầu) và báo Piagent 1.46.1 qua tính năng `helper-slots`. Piagent chỉ xin thêm khi member yêu cầu nhiều subagent cùng role. Studio cũ thì không đổi gì: broker không gửi slot.

Bản Windows (x64 và arm64) phát hành qua tag `windows-v0.1.18`. Bản macOS mang cùng thay đổi là 0.15.17.

Kiểm tra trước phát hành: `AgentWatch.Core.Tests` (57 test); CI Windows trên commit được gắn tag.
