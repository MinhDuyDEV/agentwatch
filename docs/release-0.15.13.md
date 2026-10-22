# AgentWatch 0.15.13 (macOS)

- **Codex tự sửa `config.toml` không còn chặn đồng bộ.** Codex tự ghi vào `~/.codex/config.toml` của nó (chọn model, đổi reasoning effort, tin cậy hook, thông báo chuyển model). Watch trước đây coi mọi thay đổi đó là "Cấu hình bị sửa ngoài app" và từ chối đồng bộ lại, kể cả khi key chỉ đổi danh sách model; `codex` thường khi đó vẫn trỏ về model key không còn được cấp. Giờ Watch chỉ đối chiếu phần nó ghi (`model_provider`, `model_catalog_json` và khối AgentWatch Studio): các phần đó còn nguyên thì Watch nhận file hiện tại làm mốc và đồng bộ tiếp, giữ thay đổi của Codex (model đang chọn nếu còn được cấp, effort, hook, thông báo). Sửa endpoint, provider, danh mục model hoặc lặp khối Studio vẫn bị chặn như cũ. Ngắt kết nối vẫn trả `config.toml` về bản trước khi Watch ghi.

Tải `AgentWatchMac-0.15.13.zip` cho macOS 14 trở lên (Apple Silicon và Intel). `SHA256SUMS` và `release.json` xác định chính xác các file và source được phát hành. App được ký bằng chứng chỉ cố định "Agent Watch Signing".

Kiểm tra trước phát hành: `swift test` (359 test, gồm preflight Codex 0.162.1 thật); build Release chạy ổn định 10 giây.
