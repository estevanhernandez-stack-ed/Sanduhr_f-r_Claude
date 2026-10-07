import SwiftUI

/// Settings, Accounts (item 36), which replaced Settings, Credentials: the accounts with the
/// active one marked, and for the selected one its key fields, Rename, Sign Out, Remove Account
/// and Make Active; Add Account takes a label and a key. With no accounts yet it is the old
/// first-run form, whose first key creates Personal.
///
/// Secrets are write-only here, as before: a key is never read back onto the screen.
struct AccountsSettings: View {
    @Bindable var vm: UsageViewModel
    /// Carries an account to select on arrival (from the Claude Usage page).
    var navigation: SettingsNavigation?
    @State private var selection: String?
    /// Scroll to the Data section once, after arriving for an account.
    @State private var scrollToData = false

    /// The Data section's scroll anchor.
    static let dataAnchor = "account-data"
    @State private var adding = false

    var body: some View {
        Group {
            if vm.accountLabels.isEmpty {
                FirstAccountForm(vm: vm)
            } else {
                accounts
            }
        }
        .padding(20)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private var accounts: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Each account has its own session key and usage history. The widget, the menu bar, Desk and the notch show the active one.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack(alignment: .top, spacing: 16) {
                AccountList(vm: vm, selection: $selection, adding: $adding)
                    .frame(width: 190)
                // The Data section makes the account taller than the window.
                ScrollViewReader { proxy in
                    ScrollView {
                        detail
                            .frame(maxWidth: .infinity, alignment: .topLeading)
                            .padding(.trailing, 12)
                    }
                    .onChange(of: scrollToData) { _, go in
                        guard go else { return }
                        scrollToData = false
                        DispatchQueue.main.async {
                            withAnimation { proxy.scrollTo(Self.dataAnchor, anchor: .top) }
                        }
                    }
                }
            }
            if vm.showsAccounts {
                Divider()
                FollowSetting(vm: vm)
            }
            Spacer(minLength: 0)
        }
        .onAppear {
            if let wanted = navigation?.accountToShow, vm.accountLabels.contains(wanted) {
                selection = wanted
                adding = false
                scrollToData = true
            } else if selection == nil {
                selection = vm.activeAccount
            }
            navigation?.accountToShow = nil
        }
        // A removed or renamed account leaves the selection pointing at nothing.
        .onChange(of: vm.accountLabels) { _, labels in
            if let s = selection, !labels.contains(s) { selection = vm.activeAccount }
        }
    }

    @ViewBuilder
    private var detail: some View {
        if adding {
            AddAccountForm(vm: vm) { added in
                adding = false
                if let added { selection = added }
            }
        } else if let label = selection, vm.accountLabels.contains(label) {
            // A fresh view per account, so typed fields and notes never carry over.
            AccountDetail(vm: vm, label: label) { selection = $0 }
                .id(label)
        } else {
            Text("Select an account.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }
}

/// Follow the account I'm using (AccountFollow), shown with two or more accounts.
private struct FollowSetting: View {
    @Bindable var vm: UsageViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Toggle("Follow the account I'm using", isOn: $vm.followEnabled)
            Text("Every 15 minutes Sanduhr checks your other signed-in accounts and switches to the one whose session meter is climbing, when the active one is idle. Switching by hand pauses it for up to 3 hours.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// The list of accounts: the active one marked, a signed-out one says so; Add Account below.
private struct AccountList: View {
    var vm: UsageViewModel
    @Binding var selection: String?
    @Binding var adding: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            List(vm.accountLabels, id: \.self, selection: Binding(
                get: { adding ? nil : selection },
                set: { if let s = $0 { selection = s; adding = false } })) { label in
                AccountRow(label: label, active: label == vm.activeAccount,
                           signedIn: vm.signedInAccounts.contains(label))
                    .tag(label)
            }
            .frame(height: 180)
            Button("Add Account…") { adding = true }
                .disabled(adding)
        }
    }
}

private struct AccountRow: View {
    let label: String
    let active: Bool
    let signedIn: Bool

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: active ? "checkmark.circle.fill" : "circle")
                .foregroundStyle(active ? Color.accentColor : Color.secondary)
                .font(.caption)
            VStack(alignment: .leading, spacing: 1) {
                Text(label)
                if !signedIn {
                    Text("Signed out").font(.caption2).foregroundStyle(.secondary)
                }
            }
            Spacer(minLength: 0)
            if active {
                Text("Active").font(.caption2).foregroundStyle(.secondary)
            }
        }
        .accessibilityElement(children: .combine)
    }
}

/// A coloured one-line result under a form: green for done, red for a refusal.
private struct FormNote: View {
    let text: String
    let isError: Bool

    var body: some View {
        Text(text)
            .font(.caption)
            .foregroundStyle(Color.hex(isError ? "f87171" : "4ade80"))
            .fixedSize(horizontal: false, vertical: true)
    }
}

/// Sign in to Claude (item 62), then the session key and cf_clearance fields, write-only, as the
/// way to paste instead. Blank keeps what is saved.
private struct KeyFields: View {
    @Binding var sessionKey: String
    @Binding var cfClearance: String
    let hasKey: Bool
    /// The saved key was refused (session expired, Cloudflare): only then does the button read
    /// "Sign In Again". A working account offers "Replace Sign-In", so the page never looks
    /// signed out when it isn't.
    var expired: Bool = false
    /// Opens the sign-in window; the form saves what it captures.
    var onSignIn: () -> Void

    private var signInTitle: String {
        if !hasKey { return "Sign In to Claude…" }
        return expired ? "Sign In Again…" : "Replace Sign-In…"
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Button(signInTitle, action: onSignIn)
                .help(hasKey && !expired
                      ? "Signs in once more and replaces this account's saved key, for example to use a different claude.ai account. You're signed in now."
                      : "Opens claude.ai's own sign-in.")
            Text("Opens claude.ai's own sign-in. An account that signs in with Google needs a pasted key: Google doesn't allow sign-in inside apps.")
                .font(.caption2)
                .foregroundStyle(.tertiary)
                .fixedSize(horizontal: false, vertical: true)
        }
        VStack(alignment: .leading, spacing: 6) {
            Text(hasKey ? "Or paste a sessionKey (replace)" : "Or paste a sessionKey")
                .font(.caption)
                .foregroundStyle(.secondary)
            Text("claude.ai → DevTools (⌥⌘I) → Application → Cookies → sessionKey")
                .font(.caption2)
                .foregroundStyle(.tertiary)
            SecureField(hasKey ? "Leave blank to keep existing key" : "sessionKey", text: $sessionKey)
                .textFieldStyle(.roundedBorder)
        }
        VStack(alignment: .leading, spacing: 6) {
            Text("cf_clearance (optional)")
                .font(.caption)
                .foregroundStyle(.secondary)
            Text("Only needed if you see a Cloudflare challenge error.")
                .font(.caption2)
                .foregroundStyle(.tertiary)
            SecureField(hasKey ? "Leave blank to keep existing" : "cf_clearance", text: $cfClearance)
                .textFieldStyle(.roundedBorder)
        }
    }
}

private extension String {
    var trimmed: String { trimmingCharacters(in: .whitespacesAndNewlines) }
}

/// Why an account change was refused, as the page shows it.
private func refusal(_ error: Error) -> String {
    guard let e = error as? AccountError else { return "That didn't work. See Console." }
    let text = e.description
    return text.prefix(1).uppercased() + text.dropFirst() + "."
}

// MARK: - The selected account

private struct AccountDetail: View {
    var vm: UsageViewModel
    let label: String
    /// The account's label changed (Rename): the page follows it.
    var onRenamed: (String) -> Void

    @State private var sessionKey = ""
    @State private var cfClearance = ""
    @State private var newLabel = ""
    @State private var note: String?
    @State private var noteIsError = false
    @State private var confirmingSignOut = false
    @State private var confirmingRemove = false

    private var isActive: Bool { label == vm.activeAccount }
    private var hasKey: Bool { vm.signedInAccounts.contains(label) }
    /// The active account's key was refused at its last fetch.
    private var expired: Bool { hasKey && isActive && vm.status.needsSignIn }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            header
            KeyFields(sessionKey: $sessionKey, cfClearance: $cfClearance, hasKey: hasKey, expired: expired,
                      onSignIn: signIn)
            saveRow
            Divider()
            renameRow
            Divider()
            AccountDataSection(vm: vm, label: label)
                .id(AccountsSettings.dataAnchor)
            Divider()
            endRow
            if let note { FormNote(text: note, isError: noteIsError) }
        }
        .onAppear { newLabel = label }
        // Typing hides the last note; the fields emptying after Save must not.
        .onChange(of: sessionKey) { _, new in if !new.isEmpty { note = nil } }
        .confirmationDialog("Sign out of this account?", isPresented: $confirmingSignOut,
                            titleVisibility: .visible) {
            Button("Sign Out", role: .destructive) { signOut() }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("Its session key is removed from this Mac. The account stays in the list with its usage history and settings; sign in to it again any time.")
        }
        .confirmationDialog("Remove this account?", isPresented: $confirmingRemove,
                            titleVisibility: .visible) {
            Button("Remove Account", role: .destructive) { remove() }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("Its session key, usage history and data choices are deleted from this Mac, its Claude Code folder is unlinked, and it leaves the list. This can't be undone.")
        }
    }

    private var header: some View {
        HStack {
            Text(label).font(.title3.weight(.semibold))
            if isActive {
                Text("Active").font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if !isActive {
                Button("Make Active") { vm.switchAccount(to: label) }
            }
        }
    }

    private var saveRow: some View {
        HStack {
            Button("Save") { save() }
                .keyboardShortcut(.defaultAction)
                // A signed-out account needs a key; otherwise blank keeps the current values.
                .disabled(sessionKey.trimmed.isEmpty && (!hasKey || cfClearance.trimmed.isEmpty))
            Text(!hasKey ? "Signed out. Sign in again, or paste a sessionKey."
                 : expired ? "Session expired. Sign in again, or paste a new sessionKey." : "Signed in")
                .font(.caption)
                .foregroundStyle(.secondary)
            Spacer()
        }
    }

    /// The label rule or a clash with another account (case-insensitive), as you type.
    private var renameProblem: String? {
        AccountRegistry.labelProblem(newLabel.trimmed, existing: vm.accountLabels, except: label).map(refusal)
    }

    private var renameRow: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                TextField("Label", text: $newLabel)
                    .textFieldStyle(.roundedBorder)
                    .frame(maxWidth: 220)
                Button("Rename") { rename() }
                    .disabled(newLabel.trimmed == label || newLabel.trimmed.isEmpty || renameProblem != nil)
                Spacer()
            }
            if let renameProblem { FormNote(text: renameProblem, isError: true) }
        }
    }

    private var endRow: some View {
        HStack {
            Button(hasKey || KeychainStore.accounts.anythingToSignOut(label) ? "Sign Out" : "Signed Out",
                   role: .destructive) { confirmingSignOut = true }
                .disabled(!(hasKey || KeychainStore.accounts.anythingToSignOut(label)))
            Spacer()
            Button("Remove Account…", role: .destructive) { confirmingRemove = true }
        }
    }

    private func show(_ text: String, error: Bool) {
        note = text
        noteIsError = error
    }

    /// Sign In Again / Replace Sign-In: what the window captures is saved to this account at once.
    private func signIn() {
        Task {
            guard case .signedIn(let c) = await SignInWindowController.shared.run(account: label) else { return }
            sessionKey = c.sessionKey
            cfClearance = c.cfClearance ?? ""
            save()
        }
    }

    private func save() {
        let key = sessionKey.trimmed
        let cf = cfClearance.trimmed
        do {
            try vm.saveCredentials(label, sessionKey: key.isEmpty ? nil : key, cfClearance: cf.isEmpty ? nil : cf)
            sessionKey = ""
            cfClearance = ""
            show(isActive ? "Saved. Sanduhr is fetching with the new values." : "Saved.", error: false)
        } catch {
            show(refusal(error), error: true)
        }
    }

    private func rename() {
        let new = newLabel.trimmed
        do {
            try vm.renameAccount(label, to: new)
            onRenamed(new)
        } catch {
            show(refusal(error), error: true)
        }
    }

    private func signOut() {
        let result = vm.signOut(account: label)
        sessionKey = ""
        cfClearance = ""
        show(result.succeeded
             ? "Signed out. Sign in again, or paste a sessionKey."
             : "Signed out, but \(result.failures.count) item(s) could not be removed. See Console.",
             error: !result.succeeded)
    }

    private func remove() {
        _ = vm.removeAccount(label)
    }
}

// MARK: - Add Account

private struct AddAccountForm: View {
    var vm: UsageViewModel
    /// Done: the new label, or nil for Cancel.
    var onDone: (String?) -> Void

    @State private var label = ""
    @State private var sessionKey = ""
    @State private var cfClearance = ""
    @State private var makeActive = true
    @State private var error: String?

    /// The label rule or a duplicate (case-insensitive, as the registry checks), as you type.
    private var labelProblem: String? {
        AccountRegistry.labelProblem(label.trimmed, existing: vm.accountLabels).map(refusal)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Add Account").font(.title3.weight(.semibold))
            VStack(alignment: .leading, spacing: 6) {
                Text("Label").font(.caption).foregroundStyle(.secondary)
                TextField("Work", text: $label)
                    .textFieldStyle(.roundedBorder)
                    .frame(maxWidth: 220)
                if let labelProblem { FormNote(text: labelProblem, isError: true) }
            }
            KeyFields(sessionKey: $sessionKey, cfClearance: $cfClearance, hasKey: false,
                      onSignIn: signIn)
            Toggle("Make it the active account", isOn: $makeActive)
            HStack {
                Button("Add Account") { add() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(label.trimmed.isEmpty || labelProblem != nil || sessionKey.trimmed.isEmpty)
                Button("Cancel") { onDone(nil) }
                Spacer()
            }
            if let error { FormNote(text: error, isError: true) }
        }
    }

    /// Sign In: with a usable label the account is added at once; otherwise the captured key waits
    /// in the (hidden) field for a name.
    private func signIn() {
        Task {
            let l = label.trimmed
            guard case .signedIn(let c) = await SignInWindowController.shared.run(account: l.isEmpty ? nil : l)
            else { return }
            sessionKey = c.sessionKey
            cfClearance = c.cfClearance ?? ""
            if l.isEmpty || labelProblem != nil {
                error = "Signed in. Give the account a label, then Add Account."
            } else {
                add()
            }
        }
    }

    private func add() {
        let l = label.trimmed
        let cf = cfClearance.trimmed
        do {
            try vm.addAccount(l, sessionKey: sessionKey.trimmed, cfClearance: cf.isEmpty ? nil : cf,
                              makeActive: makeActive)
            onDone(l)
        } catch {
            self.error = refusal(error)
        }
    }
}

// MARK: - No accounts yet

/// The first key, as Settings, Credentials took it before accounts: saving it creates Personal
/// (KeychainStore.set), and the page turns into the list.
private struct FirstAccountForm: View {
    var vm: UsageViewModel
    @State private var sessionKey = ""
    @State private var cfClearance = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Sign in to the claude.ai account to watch, or paste its session key. It becomes your Personal account; you can add more here later.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            KeyFields(sessionKey: $sessionKey, cfClearance: $cfClearance, hasKey: false,
                      onSignIn: signIn)
            HStack {
                Button("Save") { save() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(sessionKey.trimmed.isEmpty)
                Spacer()
            }
            Spacer()
        }
    }

    private func signIn() {
        Task {
            guard case .signedIn(let c) = await SignInWindowController.shared.run() else { return }
            sessionKey = c.sessionKey
            cfClearance = c.cfClearance ?? ""
            save()
        }
    }

    private func save() {
        KeychainStore.set(sessionKey.trimmed, account: KeychainAccount.sessionKey)
        let cf = cfClearance.trimmed
        if !cf.isEmpty { KeychainStore.set(cf, account: KeychainAccount.cfClearance) }
        sessionKey = ""
        cfClearance = ""
        vm.credentialsChanged()
    }
}
