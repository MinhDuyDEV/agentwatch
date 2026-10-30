import SwiftUI
import AppKit
import AgentWatchCore

/// Tools tab: sync list on the left; quota, granted models and the company CLI
/// launcher on the right. Everything fits one screen on a normal window.
struct StudioToolsTab: View {
    @Environment(StudioConnectionStore.self) private var studio
    let sync: StudioSyncStore
    let openUsage: () -> Void

    var body: some View {
        StudioColumns {
            StudioConfigurationView(sync: sync)
            StudioLauncherView()
            MemorySignalsCard()
        } trailing: {
            quota
            models
        }
    }

    @ViewBuilder private var quota: some View {
        let summaries = StudioQuotaSummary.from(studio.dashboard?.quota)
        if !summaries.isEmpty {
            VStack(alignment: .leading, spacing: 10) {
                HStack {
                    SectionLabel(text: "Hạn mức key")
                    Spacer()
                    Button("Chi tiết", action: openUsage).buttonStyle(.link).font(ClaudeFont.body(11))
                }
                ForEach(summaries, id: \.period) { StudioQuotaBar(summary: $0, compact: true) }
                if let at = studio.dashboard?.quota?.observed_at {
                    Text("Cập nhật \(StudioFormat.time(at))").font(ClaudeFont.body(10)).foregroundStyle(Claude.textMuted)
                }
            }.studioCard(padding: 12)
        }
    }

    private func chip(_ model: StudioModel, serving: Bool) -> some View {
        HStack(spacing: 4) {
            Text(model.displayName.isEmpty ? model.id : model.displayName).font(ClaudeFont.body(11.5))
                .strikethrough(!serving, color: Claude.textMuted)
            Text(StudioFormat.provider(model.ownedBy)).font(ClaudeFont.label(9.5)).foregroundStyle(Claude.textMuted)
        }
        .foregroundStyle(serving ? Claude.textPrimary : Claude.textMuted)
        .padding(.horizontal, 8).padding(.vertical, 4)
        .background(Claude.surfaceAlt, in: Capsule())
        .help(serving ? model.id : model.id + " · tạm chưa dùng được")
    }

    @ViewBuilder private var models: some View {
        VStack(alignment: .leading, spacing: 10) {
            if studio.profile?.credentialMode == .managed {
                SectionLabel(text: "Harness công ty")
                Text("agent-watch-auto").font(ClaudeFont.body(13))
                Text("Main agent và các subagent (khảo sát, nghiên cứu, xác minh, kiểm tra) mà Harness của team bật. Thinking chọn trong Pi hoặc Piagent.")
                    .font(ClaudeFont.body(11.5)).foregroundStyle(Claude.textMuted)
            } else { switch studio.snapshot?.models {
            case .available(let list):
                let paused = StudioModelAvailability.unavailable(granted: sync.models, serving: list)
                SectionLabel(text: "Model được cấp · \(list.count + paused.count)")
                if list.isEmpty && paused.isEmpty {
                    Text("Key chưa được cấp model.").font(ClaudeFont.body(12)).foregroundStyle(Claude.textMuted)
                } else {
                    StudioChips {
                        ForEach(list) { model in chip(model, serving: true) }
                        ForEach(paused) { model in chip(model, serving: false) }
                    }
                    if !paused.isEmpty {
                        Text(list.isEmpty ? "Tạm chưa dùng được: tài khoản AI của team đang tạm dừng hoặc chưa sẵn sàng. Liên hệ quản trị viên."
                             : "\(paused.count) model tạm chưa dùng được: tài khoản AI đang tạm dừng hoặc chưa sẵn sàng.")
                            .font(ClaudeFont.body(11)).foregroundStyle(Claude.orange).fixedSize(horizontal: false, vertical: true)
                    }
                }
            case .unavailable(let error):
                SectionLabel(text: "Model được cấp")
                Text(error.localizedDescription).font(ClaudeFont.body(11.5)).foregroundStyle(Claude.orange)
            case nil:
                SectionLabel(text: "Model được cấp")
                Text("Chưa kiểm tra.").font(ClaudeFont.body(12)).foregroundStyle(Claude.textMuted)
            } }
        }.studioCard(padding: 12)
    }
}

/// Team memory signals (MemorySignals.swift): the member chooses whether
/// their corrections to Claude Code and Codex go to their team's memory.
struct MemorySignalsCard: View {
    @Environment(StudioConnectionStore.self) private var studio
    @State private var service = MemorySignalService.shared

    var body: some View {
        let direct = studio.profile?.credentialMode == .direct
        VStack(alignment: .leading, spacing: 6) {
            SectionLabel(text: "Team memory")
            Toggle(isOn: Binding(get: { direct && service.enabled }, set: { service.enabled = $0 })) {
                Text("Gửi lời sửa agent lên team memory").font(ClaudeFont.body(13))
            }.toggleStyle(.checkbox).disabled(!direct)
            Text("Khi bạn sửa Claude Code hoặc Codex (\u{201C}đừng…\u{201D}, \u{201C}từ giờ luôn…\u{201D}, \u{201C}sai rồi, phải…\u{201D}), Agent Watch gửi đúng câu đó (đã ẩn thông tin xác thực, tối đa 1.000 ký tự) lên Studio của bạn. Studio không lưu câu chữ; admin team có thể duyệt nó thành luật. Hội thoại Piagent đã tự gửi nên không gửi lại.")
                .font(ClaudeFont.body(11)).foregroundStyle(Claude.textMuted).fixedSize(horizontal: false, vertical: true)
            if !direct {
                Text("Cần kết nối bằng key trực tiếp (key CLI).").font(ClaudeFont.body(11)).foregroundStyle(Claude.textMuted)
            } else if service.enabled, let at = service.lastRun {
                Text(service.note.isEmpty ? "Lần quét \(StudioFormat.time(at)): đã gửi \(service.lastSent)." : service.note)
                    .font(ClaudeFont.body(11)).foregroundStyle(service.note.isEmpty ? Claude.textMuted : Claude.orange)
            }
        }
        .studioCard(padding: 12)
        .onAppear { service.start() }
    }
}

/// One row per coding tool: choose, see the result, fix. Apply only when needed.
struct StudioConfigurationView: View {
    @Environment(StudioConnectionStore.self) private var studio
    var showApply = true
    @State private var sync: StudioSyncStore
    @State private var background = StudioBackgroundService.shared
    @State private var expanded: StudioSyncTarget?

    init(showApply: Bool = true, sync: StudioSyncStore = .shared) {
        self.showApply = showApply
        _sync = State(initialValue: sync)
    }

    private var pending: Bool { showApply && !sync.busy && sync.selected.contains { target in !sync.results.contains { $0.target == target } } }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .center, spacing: 8) {
                SectionLabel(text: showApply ? "Công cụ đồng bộ" : "Chọn công cụ")
                Spacer()
                if showApply {
                    if sync.busy { ProgressView().controlSize(.small) }
                    else if let at = sync.lastChecked {
                        Text(StudioFormat.time(at)).font(ClaudeFont.label(10)).foregroundStyle(Claude.textMuted).help("Lần đồng bộ gần nhất")
                    }
                    applyButton
                } else {
                    Text("\(sync.selected.count)/\(StudioSyncTarget.allCases.count)").font(ClaudeFont.label()).foregroundStyle(Claude.textMuted)
                }
            }.padding(.bottom, 8)
            ForEach(StudioSyncTarget.allCases) { target in
                row(target)
                if target != StudioSyncTarget.allCases.last { Divider().opacity(0.6) }
            }
            if showApply { footer.padding(.top, 8) }
        }
        .studioCard(padding: 12)
        .disabled(sync.busy)
    }

    @ViewBuilder private var applyButton: some View {
        let button = Button(sync.busy ? "Đang đồng bộ…" : "Áp dụng") {
            sync.enabled = true; background.start()
            Task { await sync.synchronize(force: true) }
        }.controlSize(.small).disabled(sync.busy || sync.selected.isEmpty)
        if pending { button.buttonStyle(.borderedProminent).tint(Claude.orange) } else { button }
    }

    private func row(_ target: StudioSyncTarget) -> some View {
        let supported = studio.profile?.credentialMode != .managed || StudioSyncTarget.managedTargets.contains(target)
        let selected = supported && sync.selected.contains(target)
        let result = sync.results.first { $0.target == target }
        let (text, tone) = state(target, selected: selected, result: result)
        return VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 10) {
                Toggle(isOn: Binding(get: { supported && sync.selected.contains(target) }, set: { value in
                    if value { sync.selected.insert(target) } else { sync.selected.remove(target) }
                })) {
                    HStack(spacing: 8) {
                        Image(systemName: icon(target)).frame(width: 16).foregroundStyle(selected ? Claude.orange : Claude.textMuted)
                        Text(target.title).font(ClaudeFont.body(13))
                    }
                }.toggleStyle(.checkbox).disabled(!supported)
                Spacer(minLength: 8)
                if !supported {
                    Text("Dùng key CLI trực tiếp").font(ClaudeFont.body(11)).foregroundStyle(Claude.textMuted)
                } else if showApply {
                    Label(text, systemImage: tone.symbol).font(ClaudeFont.body(11.5)).foregroundStyle(tone.inline).lineLimit(1)
                    if let result, !result.success, selected {
                        Button { expanded = expanded == target ? nil : target } label: {
                            Image(systemName: expanded == target ? "chevron.up" : "chevron.down").font(.system(size: 10))
                        }.buttonStyle(.borderless).help("Xem lý do")
                    }
                }
            }
            if showApply, expanded == target, let result, !result.success {
                Text(result.message).font(ClaudeFont.body(11)).foregroundStyle(Claude.textMuted)
                    .fixedSize(horizontal: false, vertical: true).textSelection(.enabled).padding(.leading, 26)
            }
        }.padding(.vertical, 7)
    }

    private func state(_ target: StudioSyncTarget, selected: Bool, result: StudioSyncResult?) -> (String, StudioTone) {
        if !selected { return ("Không đồng bộ", .neutral) }
        if sync.busy { return ("Đang đồng bộ…", .info) }
        guard let result, sync.lastError == nil else { return ("Chờ áp dụng", .warning) }
        return result.success ? (studio.profile?.credentialMode == .managed ? "Auto sẵn sàng" : "\(result.count) model", .ok) : (StudioSyncReason.label(result.code), .danger)
    }

    @ViewBuilder private var footer: some View {
        HStack(spacing: 8) {
            Label(sync.enabled ? "Tự đồng bộ 5 phút" : "Tự đồng bộ tắt", systemImage: "arrow.triangle.2.circlepath")
            if background.needsApproval {
                Button("Bật chạy nền") { background.openSettings() }.buttonStyle(.link)
            }
            Spacer()
        }
        .font(ClaudeFont.body(11)).foregroundStyle(Claude.textMuted)
        .help(background.message)
        if let error = sync.lastError {
            Text(error).font(ClaudeFont.body(11.5)).foregroundStyle(Claude.orange).fixedSize(horizontal: false, vertical: true)
        }
    }

    private func icon(_ target: StudioSyncTarget) -> String {
        switch target { case .claude: "sparkle"; case .codex: "terminal"; case .pi: "curlybraces"; case .piagent: "square.stack.3d.up" }
    }
}
