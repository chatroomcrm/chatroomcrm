/* =========================================================================
   CHATFLOW WHATSAPP CRM - SETTINGS & PLATFORM ADMIN SERVICES (settings.js)
   ========================================================================= */

const Settings = {
    activeGlowStart: '#00f2fe',
    activeGlowEnd: '#4facfe',
    activeLogoUrl: null,
    tenantsPage: 1,
    tenantsSearch: '',
    usersPage: 1,
    usersSearch: '',
    logsPage: 1,
    logsSearch: '',
    logsLevel: '',

    async initialize() {
        this.renderPane();
        const currentUser = Auth.getUser();
        if (currentUser) {
            if (currentUser.role === UserRoles.SuperAdmin) {
                await this.loadPlatformAnalytics();
            } else if (currentUser.role === UserRoles.TenantAdmin) {
                await this.loadTeamRegistry();
                await this.loadCurrentBranding();
                await this.loadCurrentMessaging();
            }
        }

        // Set up real-time platform analytics periodic refresh interval for SuperAdmin (every 10 seconds)
        if (window.platformAnalyticsInterval) {
            clearInterval(window.platformAnalyticsInterval);
        }
        window.platformAnalyticsInterval = setInterval(async () => {
            const activePane = document.querySelector('.saas-content .view-pane.active');
            const user = Auth.getUser();
            if (Auth.isAuthenticated() && user && user.role === UserRoles.SuperAdmin && activePane && activePane.id === 'view-settings') {
                await this.loadPlatformAnalytics();
            }
        }, 10000);
    },

    renderPane() {
        const pane = document.getElementById('view-settings');
        if (!pane) return;

        const currentUser = Auth.getUser();

        // 1. Check strict role access boundaries
        if (currentUser && currentUser.role === UserRoles.Agent) {
            pane.innerHTML = `
                <div style="padding: 5rem; text-align: center;">
                    <div style="font-size: 3rem; margin-bottom: 1rem;">🔒</div>
                    <h3 style="font-family: var(--font-display); font-weight:600; color: var(--accent-rose); font-size:1.25rem;">Access Restricted</h3>
                    <p style="color: var(--text-muted); font-size: 0.85rem; margin-top: 0.5rem; max-width: 420px; margin-left: auto; margin-right: auto;">
                        Organization custom branding, live presets, and platform administration options are reserved for Tenant Administrators and platform Super Administrators.
                    </p>
                </div>
            `;
            return;
        }

        // 2. Build template based on user role (TenantAdmin white-label vs SuperAdmin platform controllers)
        if (currentUser && currentUser.role === UserRoles.SuperAdmin) {
            pane.innerHTML = `
                <div style="display: flex; flex-direction: column; gap: 2rem;">
                    <div>
                        <h2 style="font-family: var(--font-display); font-size: 1.5rem; font-weight: 700; background: linear-gradient(135deg, #fff 40%, var(--theme-glow) 100%); -webkit-background-clip: text; -webkit-text-fill-color: transparent;">
                            Administration
                        </h2>
                        <p style="color: var(--text-muted); font-size: 0.8rem; margin-top: 0.2rem;">
                            View aggregate analytics, manage tenant organizations and system access, and inspect server execution trace logs.
                        </p>
                    </div>

                    <!-- Platform aggregate statistics -->
                    <div class="platform-metrics-grid">
                        <div class="platform-metric-card">
                            <span class="title">Total Tenants</span>
                            <div class="val" id="sa-total-tenants">1</div>
                        </div>
                        <div class="platform-metric-card">
                            <span class="title">Total Staff</span>
                            <div class="val" id="sa-total-users">3</div>
                        </div>
                        <div class="platform-metric-card">
                            <span class="title">Active Leads</span>
                            <div class="val" id="sa-total-leads">4</div>
                        </div>
                        <div class="platform-metric-card">
                            <span class="title">Messages Routed</span>
                            <div class="val" id="sa-total-messages">5</div>
                        </div>
                        <div class="platform-metric-card" style="border-color: rgba(139, 92, 246, 0.25);">
                            <span class="title" style="color: var(--accent-violet);">Sys Log Entries</span>
                            <div class="val" id="sa-total-logs">0</div>
                        </div>
                    </div>

                    <!-- Tenant Directory and Users Registry Split Layout -->
                    <div class="platform-split-layout">
                        <!-- Tenant Management -->
                        <div class="platform-card">
                            <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:0.75rem; flex-wrap:wrap; gap:0.5rem;">
                                <h3 style="margin:0;">🏢 Organizations</h3>
                                <button class="login-mode-btn active" style="padding: 0.25rem 0.6rem; font-size: 0.7rem;" onclick="Settings.showAddOrganizationModal()">+ Add Organization</button>
                            </div>
                            <div style="margin-bottom: 0.75rem;">
                                <input type="text" id="sa-tenants-search" placeholder="🔍 Search organization..." oninput="Settings.handleTenantsSearch(this.value)" style="width:100%; padding:0.45rem 0.6rem; font-size:0.75rem; background:rgba(255,255,255,0.03); border:1px solid rgba(255,255,255,0.08); border-radius:6px; color:var(--text-main); box-sizing:border-box;">
                            </div>
                            <div class="platform-table-wrapper">
                                <table class="platform-table">
                                    <thead>
                                        <tr>
                                            <th>Organization Name</th>
                                            <th>Leads</th>
                                            <th>Status</th>
                                            <th>Actions</th>
                                        </tr>
                                    </thead>
                                    <tbody id="sa-tenants-list">
                                        <tr><td colspan="4" style="text-align:center; color:var(--text-muted);">Loading tenants...</td></tr>
                                    </tbody>
                                </table>
                            </div>
                            <div class="pagination-controls" style="display:flex; justify-content:space-between; align-items:center; margin-top:0.75rem; font-size:0.7rem; color:var(--text-muted);">
                                <button class="login-mode-btn" id="btn-tenants-prev" onclick="Settings.changeTenantsPage(-1)" style="padding:0.25rem 0.5rem; font-size:0.65rem;">◀ Prev</button>
                                <span id="info-tenants-page">Page 1</span>
                                <button class="login-mode-btn" id="btn-tenants-next" onclick="Settings.changeTenantsPage(1)" style="padding:0.25rem 0.5rem; font-size:0.65rem;">Next ▶</button>
                            </div>
                        </div>

                        <!-- User Management -->
                        <div class="platform-card">
                            <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:0.75rem; flex-wrap:wrap; gap:0.5rem;">
                                <h3 style="margin:0;">👥 Users</h3>
                                <button class="login-mode-btn active" style="padding: 0.25rem 0.6rem; font-size: 0.7rem;" onclick="Settings.showAddUserModal()">+ Add User</button>
                            </div>
                            <div style="margin-bottom: 0.75rem;">
                                <input type="text" id="sa-users-search" placeholder="🔍 Search user name, email, role..." oninput="Settings.handleUsersSearch(this.value)" style="width:100%; padding:0.45rem 0.6rem; font-size:0.75rem; background:rgba(255,255,255,0.03); border:1px solid rgba(255,255,255,0.08); border-radius:6px; color:var(--text-main); box-sizing:border-box;">
                            </div>
                            <div class="platform-table-wrapper">
                                <table class="platform-table">
                                    <thead>
                                        <tr>
                                            <th>User / Email</th>
                                            <th>Phone</th>
                                            <th>Role</th>
                                            <th>Status</th>
                                            <th>Actions</th>
                                        </tr>
                                    </thead>
                                    <tbody id="sa-users-list">
                                        <tr><td colspan="4" style="text-align:center; color:var(--text-muted);">Loading registry...</td></tr>
                                    </tbody>
                                </table>
                            </div>
                            <div class="pagination-controls" style="display:flex; justify-content:space-between; align-items:center; margin-top:0.75rem; font-size:0.7rem; color:var(--text-muted);">
                                <button class="login-mode-btn" id="btn-users-prev" onclick="Settings.changeUsersPage(-1)" style="padding:0.25rem 0.5rem; font-size:0.65rem;">◀ Prev</button>
                                <span id="info-users-page">Page 1</span>
                                <button class="login-mode-btn" id="btn-users-next" onclick="Settings.changeUsersPage(1)" style="padding:0.25rem 0.5rem; font-size:0.65rem;">Next ▶</button>
                            </div>
                        </div>
                    </div>

                    <!-- Server Exception Logger Panel -->
                    <div class="logs-inspector-panel">
                        <div class="logs-header-actions" style="flex-wrap:wrap; gap:1rem;">
                            <h3 style="font-family: var(--font-display); font-size: 1rem; font-weight: 600; color: var(--text-main); display: flex; align-items: center; gap: 0.5rem; margin:0;">
                                📜 Exception Trace & Performance Logs
                            </h3>
                            <div class="logs-filter-group" style="display:flex; gap:0.5rem; align-items:center; flex-wrap:wrap;">
                                <input type="text" id="logs-search-input" placeholder="🔍 Search logs..." oninput="Settings.handleLogsSearch(this.value)" style="padding:0.35rem 0.55rem; font-size:0.7rem; background:rgba(255,255,255,0.03); border:1px solid rgba(255,255,255,0.08); border-radius:4px; color:var(--text-main); width:150px;">
                                <label for="logs-level-filter">Filter:</label>
                                <select id="logs-level-filter" class="logs-filter-select" onchange="Settings.filterSystemLogs(this.value)">
                                    <option value="">ALL LEVELS</option>
                                    <option value="Info">INFO</option>
                                    <option value="Warning">WARNING</option>
                                    <option value="Error">ERROR</option>
                                </select>
                                <button class="login-mode-btn active" style="padding: 0.25rem 0.5rem; font-size: 0.7rem;" onclick="Settings.loadPlatformAnalytics()">🔄 Refresh</button>
                            </div>
                        </div>
                        <div class="logs-list-wrapper" id="sa-logs-stream">
                            <div style="color:var(--text-muted); text-align:center; padding:2rem;">No entries found.</div>
                        </div>
                        <div class="pagination-controls" style="display:flex; justify-content:space-between; align-items:center; margin-top:0.75rem; padding:0.25rem 0.5rem; font-size:0.7rem; color:var(--text-muted);">
                            <button class="login-mode-btn" id="btn-logs-prev" onclick="Settings.changeLogsPage(-1)" style="padding:0.25rem 0.5rem; font-size:0.65rem;">◀ Prev</button>
                            <span id="info-logs-page">Page 1</span>
                            <button class="login-mode-btn" id="btn-logs-next" onclick="Settings.changeLogsPage(1)" style="padding:0.25rem 0.5rem; font-size:0.65rem;">Next ▶</button>
                        </div>
                    </div>
                </div>
            `;
        } else {
            // TenantAdmin gets standard white-label setup + Agent User Management
            pane.innerHTML = `
                <div class="settings-grid">
                    <div class="settings-card">
                        <h3 style="font-family: var(--font-display); font-size: 1.1rem; font-weight: 600; margin-bottom: 1rem; border-left: 3px solid var(--theme-glow); padding-left: 0.5rem;">
                            Brand Customization
                        </h3>
                        <p style="color:var(--text-muted); font-size:0.75rem; margin-bottom: 1.5rem;">
                            Customize the brand header label, logo tag abbreviation, and dynamic styling colors of your WhatsApp CRM instance.
                        </p>
                        
                        <div class="form-group">
                            <label for="settings-brand-input" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">
                                Custom Brand Header Name
                            </label>
                            <input type="text" id="settings-brand-input" value="${currentUser.tenantName}" oninput="Settings.updateWhiteLabelBrand()" style="margin-top:0.3rem;">
                        </div>

                        <div class="form-group" style="margin-top: 1.25rem;">
                            <label style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">
                                Custom Brand Logo (DB Stored)
                            </label>
                            <div class="logo-upload-wrapper">
                                <div class="logo-preview-box" id="settings-logo-preview">
                                    ${Settings.activeLogoUrl && (Settings.activeLogoUrl.startsWith('data:image/') || Settings.activeLogoUrl.startsWith('http://') || Settings.activeLogoUrl.startsWith('https://')) ? 
                                        `<img src="${Settings.activeLogoUrl}" alt="Logo preview">` : 
                                        `<img src="images/logo-mark.png?v=3" alt="Default Logo preview">`
                                    }
                                </div>
                                <div class="logo-action-group">
                                    <button class="logo-upload-btn" onclick="document.getElementById('settings-logo-file-input').click()">
                                        📤 Upload Logo Image
                                    </button>
                                    <input type="file" id="settings-logo-file-input" accept="image/*" style="display:none;" onchange="Settings.handleLogoUpload(event)">
                                    <button class="logo-reset-btn" id="settings-logo-reset-btn" onclick="Settings.resetLogoToDefault()" style="${Settings.activeLogoUrl && (Settings.activeLogoUrl.startsWith('data:image/') || Settings.activeLogoUrl.startsWith('http://') || Settings.activeLogoUrl.startsWith('https://')) ? 'display:block;' : 'display:none;'}">
                                        ❌ Reset to Default
                                    </button>
                                </div>
                            </div>
                        </div>

                        <div class="form-group" style="margin-top: 1.25rem;">
                            <label style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">
                                Color Presets (Theme Variables Glow)
                            </label>
                            <div class="color-presets" style="margin-top:0.5rem;">
                                <button class="color-btn active" style="background:#00f2fe;" onclick="Settings.changeThemeGlow('#00f2fe', '#4facfe', this)"></button>
                                <button class="color-btn" style="background:#a78bfa;" onclick="Settings.changeThemeGlow('#a78bfa', '#ec4899', this)"></button>
                                <button class="color-btn" style="background:#10b981;" onclick="Settings.changeThemeGlow('#10b981', '#059669', this)"></button>
                                <button class="color-btn" style="background:#f59e0b;" onclick="Settings.changeThemeGlow('#f59e0b', '#d97706', this)"></button>
                            </div>
                        </div>
                        
                        <button onclick="Settings.saveBrandingSettings()" class="login-submit-btn" style="margin-top: 1.5rem; width: auto; padding: 0.5rem 1.25rem; font-size: 0.75rem;">
                            Save Branding 💾
                        </button>
                    </div>

                    <!-- Staff & Agent Registry panel for Tenant Admin -->
                    <div class="settings-card" style="display:flex; flex-direction:column; gap:1rem;">
                        <div style="display:flex; justify-content:space-between; align-items:center;">
                            <h3 style="font-family: var(--font-display); font-size: 1.1rem; font-weight: 600; border-left: 3px solid var(--theme-glow); padding-left: 0.5rem; margin:0;">
                                👥 Staff & Agent Registry
                            </h3>
                            <button class="login-mode-btn active" style="padding: 0.25rem 0.6rem; font-size: 0.7rem;" onclick="Settings.showAddUserModal()">+ Add Agent</button>
                        </div>
                        <p style="color:var(--text-muted); font-size:0.75rem; margin:0;">
                            Create and manage staff member accounts. Tenant Admins can only register <strong>Agent</strong> users for their organization.
                        </p>
                        <div class="platform-table-wrapper" style="margin-top:0.5rem; max-height:220px; overflow-y:auto;">
                            <table class="platform-table">
                                <thead>
                                    <tr>
                                        <th>Name / Email</th>
                                        <th>Phone</th>
                                        <th>Role</th>
                                        <th>Status</th>
                                        <th>Actions</th>
                                    </tr>
                                </thead>
                                <tbody id="ta-users-list">
                                    <tr><td colspan="3" style="text-align:center; color:var(--text-muted);">Loading registry...</td></tr>
                                </tbody>
                            </table>
                        </div>
                    </div>

                    <!-- Unified WhatsApp Channel Configuration Panel -->
                    <div class="settings-card" style="display:flex; flex-direction:column; gap:1.25rem; grid-column: span 2;">
                        <h3 style="font-family: var(--font-display); font-size: 1.1rem; font-weight: 600; border-left: 3px solid var(--theme-glow); padding-left: 0.5rem; margin:0; display:flex; align-items:center; gap:0.5rem;">
                            💬 WhatsApp Channel Configuration
                        </h3>
                        <p style="color:var(--text-muted); font-size:0.75rem; margin:0;">
                            Configure your custom messaging channel dynamically. Select your preferred provider (Twilio or Meta Cloud API), fill in the shared credentials, and all incoming/outgoing logs will route through this tenant space automatically.
                        </p>

                        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 1.25rem; margin-top: 0.5rem;">
                            <div class="form-group">
                                <label for="settings-msg-provider" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">
                                    Active Messaging Provider
                                </label>
                                <select id="settings-msg-provider" class="login-input" onchange="Settings.onMessagingProviderChange(this.value)" style="background: var(--bg-secondary); color: var(--text-main); border: 1px solid var(--border-color); margin-top: 0.3rem;">
                                    <option value="None">None / Disabled (Simulated Sandbox)</option>
                                    <option value="Twilio">Twilio WhatsApp Service</option>
                                    <option value="Meta">Meta Cloud API (WhatsApp Business)</option>
                                </select>
                            </div>

                            <div class="form-group">
                                <label for="settings-msg-number" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">
                                    Customer-Facing WhatsApp Number
                                </label>
                                <input type="text" id="settings-msg-number" class="login-input" placeholder="e.g. +918143712528" style="margin-top: 0.3rem;">
                            </div>
                        </div>

                        <!-- Dynamic Twilio configuration block -->
                        <div id="settings-twilio-section" style="display:none; flex-direction:column; gap:1.25rem; border-top: 1px solid rgba(255,255,255,0.05); padding-top: 1.25rem; margin-top: 0.5rem;">
                            <h4 style="margin: 0; font-size: 0.8rem; color: var(--theme-glow); font-family: var(--font-display); font-weight: 600;">🔑 Twilio Credentials Configuration</h4>
                            <div style="display:grid; grid-template-columns: 1fr 1fr; gap:1.25rem;">
                                <div class="form-group">
                                    <label for="settings-twilio-sid" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Account SID</label>
                                    <input type="text" id="settings-twilio-sid" class="login-input" placeholder="Enter Twilio Account SID (e.g. AC...)" style="margin-top:0.3rem;">
                                </div>
                                <div class="form-group">
                                    <label for="settings-twilio-token" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Auth Token</label>
                                    <input type="password" id="settings-twilio-token" class="login-input" placeholder="Enter Twilio Auth Token" style="margin-top:0.3rem;">
                                </div>
                            </div>
                        </div>

                        <!-- Dynamic Meta configuration block -->
                        <div id="settings-meta-section" style="display:none; flex-direction:column; gap:1.25rem; border-top: 1px solid rgba(255,255,255,0.05); padding-top: 1.25rem; margin-top: 0.5rem;">
                            <h4 style="margin: 0; font-size: 0.8rem; color: var(--theme-glow); font-family: var(--font-display); font-weight: 600;">🔑 Meta Cloud API Credentials Configuration</h4>
                            <div style="display:grid; grid-template-columns: 1fr 1fr; gap:1.25rem; row-gap: 1rem;">
                                <div class="form-group">
                                    <label for="settings-meta-waba-id" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">WhatsApp Business Account ID (WABA ID)</label>
                                    <input type="text" id="settings-meta-waba-id" class="login-input" placeholder="Enter Meta WABA ID" style="margin-top:0.3rem;">
                                </div>
                                <div class="form-group">
                                    <label for="settings-meta-phone-id" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Phone Number ID</label>
                                    <input type="text" id="settings-meta-phone-id" class="login-input" placeholder="Enter Meta Phone Number ID" style="margin-top:0.3rem;">
                                </div>
                                <div class="form-group" style="grid-column: span 2;">
                                    <label for="settings-meta-token" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Meta Access Token (System User Permanent Token)</label>
                                    <textarea id="settings-meta-token" class="login-input" rows="2" placeholder="Enter Permanent Meta Access Token" style="margin-top:0.3rem; height: auto; resize: vertical; font-family:var(--font-mono); font-size:0.7rem; padding: 0.5rem;"></textarea>
                                </div>
                            </div>
                        </div>

                        <div style="display:flex; justify-content:space-between; align-items:center; border-top: 1px solid rgba(255,255,255,0.05); padding-top: 1.25rem; margin-top: 0.5rem;">
                            <button onclick="Settings.saveMessagingSettings()" class="login-submit-btn" style="width: auto; padding: 0.5rem 1.5rem; font-size: 0.75rem; margin:0;">
                                Save Messaging Config 💾
                            </button>
                            <span id="messaging-settings-status" style="color:var(--text-muted); font-size:0.7rem;"></span>
                        </div>
                    </div>
                </div>
            `;
        }
        }
    },

    // ----------------------------------------------------
    // SUPER ADMIN ANALYTICS METHODS
    // ----------------------------------------------------
    async loadPlatformAnalytics() {
        try {
            logConsole(`[API Request] GET /api/superadmin/analytics...`);
            const res = await Auth.apiFetch('/api/superadmin/analytics');
            const data = await res.json();

            // Populate analytics UI
            const elTotalTenants = document.getElementById('sa-total-tenants');
            if (elTotalTenants) elTotalTenants.innerText = data.totalTenants;
            const elTotalUsers = document.getElementById('sa-total-users');
            if (elTotalUsers) elTotalUsers.innerText = data.totalUsers;
            const elTotalLeads = document.getElementById('sa-total-leads');
            if (elTotalLeads) elTotalLeads.innerText = data.totalLeads;
            const elTotalMessages = document.getElementById('sa-total-messages');
            if (elTotalMessages) elTotalMessages.innerText = data.totalMessages;
            const elTotalLogs = document.getElementById('sa-total-logs');
            if (elTotalLogs) elTotalLogs.innerText = data.totalLogs;

            // Load all three paginated sections
            await this.loadTenants();
            await this.loadUsers();
            await this.loadSystemLogs();
        } catch (err) {
            logConsole(`[SuperAdmin API Error] Failed: ${err.message}`);
        }
    },

    async loadTenants() {
        try {
            const query = `?page=${this.tenantsPage}&pageSize=10&search=${encodeURIComponent(this.tenantsSearch)}`;
            logConsole(`[API Request] GET /api/superadmin/tenants${query}...`);
            const res = await Auth.apiFetch(`/api/superadmin/tenants${query}`);
            const tenants = await res.json();
            this.renderTenantsList(tenants);

            const totalHeader = res.headers.get('X-Pagination-Total-Count');
            const total = totalHeader ? parseInt(totalHeader) : tenants.length;
            const totalPages = Math.ceil(total / 10) || 1;

            const infoEl = document.getElementById('info-tenants-page');
            if (infoEl) infoEl.innerText = `Page ${this.tenantsPage} of ${totalPages}`;

            const prevBtn = document.getElementById('btn-tenants-prev');
            if (prevBtn) prevBtn.disabled = this.tenantsPage <= 1;

            const nextBtn = document.getElementById('btn-tenants-next');
            if (nextBtn) nextBtn.disabled = this.tenantsPage >= totalPages;
        } catch (err) {
            logConsole(`[API Error] loadTenants failed: ${err.message}`);
        }
    },

    async loadUsers() {
        try {
            const query = `?page=${this.usersPage}&pageSize=10&search=${encodeURIComponent(this.usersSearch)}`;
            logConsole(`[API Request] GET /api/superadmin/users${query}...`);
            const res = await Auth.apiFetch(`/api/superadmin/users${query}`);
            const users = await res.json();
            this.renderUsersList(users);

            const totalHeader = res.headers.get('X-Pagination-Total-Count');
            const total = totalHeader ? parseInt(totalHeader) : users.length;
            const totalPages = Math.ceil(total / 10) || 1;

            const infoEl = document.getElementById('info-users-page');
            if (infoEl) infoEl.innerText = `Page ${this.usersPage} of ${totalPages}`;

            const prevBtn = document.getElementById('btn-users-prev');
            if (prevBtn) prevBtn.disabled = this.usersPage <= 1;

            const nextBtn = document.getElementById('btn-users-next');
            if (nextBtn) nextBtn.disabled = this.usersPage >= totalPages;
        } catch (err) {
            logConsole(`[API Error] loadUsers failed: ${err.message}`);
        }
    },

    async loadSystemLogs() {
        try {
            const levelParam = this.logsLevel ? `&logLevel=${this.logsLevel}` : '';
            const query = `?page=${this.logsPage}&pageSize=10&search=${encodeURIComponent(this.logsSearch)}${levelParam}`;
            logConsole(`[API Request] GET /api/superadmin/logs${query}...`);
            const res = await Auth.apiFetch(`/api/superadmin/logs${query}`);
            const logs = await res.json();
            this.renderLogsStream(logs);

            const totalHeader = res.headers.get('X-Pagination-Total-Count');
            const total = totalHeader ? parseInt(totalHeader) : logs.length;
            const totalPages = Math.ceil(total / 10) || 1;

            const infoEl = document.getElementById('info-logs-page');
            if (infoEl) infoEl.innerText = `Page ${this.logsPage} of ${totalPages}`;

            const prevBtn = document.getElementById('btn-logs-prev');
            if (prevBtn) prevBtn.disabled = this.logsPage <= 1;

            const nextBtn = document.getElementById('btn-logs-next');
            if (nextBtn) nextBtn.disabled = this.logsPage >= totalPages;
        } catch (err) {
            logConsole(`[API Error] loadSystemLogs failed: ${err.message}`);
        }
    },

    handleTenantsSearch(val) {
        this.tenantsSearch = val;
        this.tenantsPage = 1;
        this.loadTenants();
    },

    changeTenantsPage(direction) {
        this.tenantsPage += direction;
        if (this.tenantsPage < 1) this.tenantsPage = 1;
        this.loadTenants();
    },

    handleUsersSearch(val) {
        this.usersSearch = val;
        this.usersPage = 1;
        this.loadUsers();
    },

    changeUsersPage(direction) {
        this.usersPage += direction;
        if (this.usersPage < 1) this.usersPage = 1;
        this.loadUsers();
    },

    handleLogsSearch(val) {
        this.logsSearch = val;
        this.logsPage = 1;
        this.loadSystemLogs();
    },

    changeLogsPage(direction) {
        this.logsPage += direction;
        if (this.logsPage < 1) this.logsPage = 1;
        this.loadSystemLogs();
    },



    renderTenantsList(tenants) {
        const container = document.getElementById('sa-tenants-list');
        if (!container) return;
        container.innerHTML = '';

        tenants.forEach(t => {
            const badge = t.isBlocked ? `<span class="status-badge suspended">Suspended</span>` : `<span class="status-badge active">Active</span>`;
            const actionBtn = t.isBlocked ? 
                `<button class="btn-action-activate" onclick="Settings.toggleTenantBlock('${t.id}')">Activate</button>` :
                `<button class="btn-action-suspend" onclick="Settings.toggleTenantBlock('${t.id}')">Suspend</button>`;

            container.innerHTML += `
                <tr>
                    <td style="font-weight:600;">${t.name}</td>
                    <td>${t.leadsCount}</td>
                    <td>${badge}</td>
                    <td>${actionBtn}</td>
                </tr>
            `;
        });
    },

    renderUsersList(users) {
        const container = document.getElementById('sa-users-list');
        if (!container) return;
        container.innerHTML = '';

        users.forEach(u => {
            const badge = u.isBlocked ? `<span class="status-badge suspended">Suspended</span>` : `<span class="status-badge active">Active</span>`;
            const isSuper = u.role === UserRoles.SuperAdmin;
            const phoneVal = u.phone || '-';
            const escapedName = u.name.replace(/'/g, "\\'");
            const escapedEmail = u.email.replace(/'/g, "\\'");
            const escapedPhone = (u.phone || '').replace(/'/g, "\\'");
            const toggleBtn = u.isBlocked ? 
                `<button class="btn-action-activate" onclick="Settings.toggleUserBlock('${u.id}')" style="padding: 0.25rem 0.5rem; font-size: 0.65rem; border-radius: 4px; cursor: pointer; transition: all 0.2s;">Activate</button>` :
                `<button class="btn-action-suspend" onclick="Settings.toggleUserBlock('${u.id}')" style="padding: 0.25rem 0.5rem; font-size: 0.65rem; border-radius: 4px; cursor: pointer; transition: all 0.2s;">Suspend</button>`;

            const actionsHtml = isSuper ? 
                `<div style="display:flex; align-items:center; gap:0.4rem;"><span style="color:var(--text-muted); font-size:0.65rem;">System Owner</span></div>` :
                `<div style="display:flex; align-items:center; gap:0.4rem;">${toggleBtn}</div>`;

            container.innerHTML += `
                <tr>
                    <td>
                        <div style="font-weight:600;">${u.name}</div>
                        <div style="font-size:0.6rem; color:var(--text-muted);">${u.email}</div>
                    </td>
                    <td style="font-size:0.75rem;">${phoneVal}</td>
                    <td>${u.role}</td>
                    <td>${badge}</td>
                    <td>${actionsHtml}</td>
                </tr>
            `;
        });
    },

    renderLogsStream(logs) {
        const container = document.getElementById('sa-logs-stream');
        if (!container) return;
        container.innerHTML = '';

        if (!logs || logs.length === 0) {
            container.innerHTML = `<div style="color:var(--text-muted); text-align:center; padding:2rem;">No logs found.</div>`;
            return;
        }

        logs.forEach(log => {
            const levelClass = (log.logLevel || 'info').toLowerCase();
            const source = log.source || 'Sys.Diagnostics';
            const time = new Date(log.timestamp).toLocaleTimeString();

            container.innerHTML += `
                <div class="log-entry-item">
                    <span class="timestamp">[${time}]</span>
                    <span class="level ${levelClass}">${log.logLevel}</span>
                    <span class="source">${source}</span>
                    <span class="message">${log.message}</span>
                </div>
            `;
        });
    },

    async toggleTenantBlock(tenantId) {
        try {
            logConsole(`[API Request] POST /api/superadmin/tenants/${tenantId}/toggle-block...`);
            const res = await Auth.apiFetch(`/api/superadmin/tenants/${tenantId}/toggle-block`, {
                method: 'POST'
            });
            const data = await res.json();
            triggerToast("Platform Admin Action", data.message);
            await this.loadPlatformAnalytics();
        } catch (err) {
            logConsole(`[SuperAdmin API Error] toggleTenantBlock failed: ${err.message}`);
        }
    },

    async toggleUserBlock(userId) {
        try {
            logConsole(`[API Request] POST /api/superadmin/users/${userId}/toggle-block...`);
            const res = await Auth.apiFetch(`/api/superadmin/users/${userId}/toggle-block`, {
                method: 'POST'
            });
            const data = await res.json();
            triggerToast("Platform Admin Action", data.message);
            await this.loadPlatformAnalytics();
        } catch (err) {
            logConsole(`[SuperAdmin API Error] toggleUserBlock failed: ${err.message}`);
        }
    },

    // ----------------------------------------------------
    // LIVE TEAM / AGENT RETRIEVAL & MANAGEMENT
    // ----------------------------------------------------
    async loadTeamRegistry() {
        try {
            logConsole(`[API Request] GET /api/auth/team...`);
            const res = await Auth.apiFetch('/api/auth/team');
            const users = await res.json();
            this.renderTeamList(users);
        } catch (err) {
            logConsole(`[Team API Error] Failed: ${err.message}`);
        }
    },

    renderTeamList(users) {
        const container = document.getElementById('ta-users-list');
        if (!container) return;
        container.innerHTML = '';

        if (!users || users.length === 0) {
            container.innerHTML = `<tr><td colspan="5" style="text-align:center; color:var(--text-muted);">No staff registered yet.</td></tr>`;
            return;
        }

        users.forEach(u => {
            const badge = u.isBlocked ? `<span class="status-badge suspended">Suspended</span>` : `<span class="status-badge active">Active</span>`;
            const phoneVal = u.phone || '-';
            const escapedName = u.name.replace(/'/g, "\\'");
            const escapedEmail = u.email.replace(/'/g, "\\'");
            const escapedPhone = (u.phone || '').replace(/'/g, "\\'");
            const editBtn = `<button class="btn-action-activate" onclick="Settings.showEditUserModal('${u.id}', '${escapedName}', '${escapedEmail}', '${escapedPhone}', '${u.role}')" style="background: rgba(0, 242, 254, 0.15); border-color: rgba(0, 242, 254, 0.25); color: var(--theme-glow); padding: 0.25rem 0.5rem; font-size: 0.65rem; border-radius: 4px; cursor: pointer; transition: all 0.2s;">Edit ✏️</button>`;

            container.innerHTML += `
                <tr>
                    <td>
                        <div style="font-weight:600;">${u.name}</div>
                        <div style="font-size:0.6rem; color:var(--text-muted);">${u.email}</div>
                    </td>
                    <td style="font-size:0.75rem;">${phoneVal}</td>
                    <td>
                        <span style="font-size:0.65rem; font-family:var(--font-mono); color:var(--theme-glow); font-weight:600; text-transform:uppercase;">${u.role}</span>
                    </td>
                    <td>${badge}</td>
                    <td>${editBtn}</td>
                </tr>
            `;
        });
    },

    async showAddUserModal() {
        // Remove any existing modal
        const existing = document.getElementById('add-user-modal');
        if (existing) existing.remove();

        const currentUser = Auth.getUser();
        if (!currentUser) return;

        let roleOptionsHtml = '';
        let tenantFieldHtml = '';

        if (currentUser.role === UserRoles.TenantAdmin) {
            roleOptionsHtml = `
                <div class="form-group" style="margin-top: 0.75rem;">
                    <label style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Account Role</label>
                    <input type="text" class="login-input" value="Agent" readonly style="background: rgba(255,255,255,0.02); color: var(--theme-glow); cursor: not-allowed; border-color: rgba(255,255,255,0.05); margin-top: 0.3rem;">
                    <small style="color:var(--text-muted); font-size:0.65rem; margin-top:0.2rem; display:block;">Tenant Admins can only register Agent users.</small>
                    <input type="hidden" id="modal-user-role" value="${UserRoles.Agent}">
                </div>
            `;
        } else if (currentUser.role === UserRoles.SuperAdmin) {
            // Fetch tenants list for dropdown
            let tenants = [];
            try {
                const res = await Auth.apiFetch('/api/superadmin/tenants');
                tenants = await res.json();
            } catch (e) {
                logConsole(`Error fetching tenants: ${e.message}`);
            }

            const tenantOptions = tenants.map(t => `<option value="${t.id}">${t.name}</option>`).join('');

            roleOptionsHtml = `
                <div class="form-group" style="margin-top: 0.75rem;">
                    <label for="modal-user-role" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Account Role</label>
                    <select id="modal-user-role" class="login-input" onchange="Settings.toggleModalTenantFields(this.value)" style="background: var(--bg-secondary); color: var(--text-main); border: 1px solid var(--border-color); margin-top: 0.3rem;">
                        <option value="${UserRoles.Agent}">Agent (Standard Staff)</option>
                        <option value="${UserRoles.TenantAdmin}">TenantAdmin (Business Owner)</option>
                        <option value="${UserRoles.SuperAdmin}">SuperAdmin (Platform Owner)</option>
                    </select>
                </div>
            `;

            tenantFieldHtml = `
                <div id="modal-tenant-section" class="form-group" style="margin-top: 1rem; display: flex; flex-direction: column; gap: 0.75rem;">
                    <div>
                        <label for="modal-tenant-select" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Assign to Organization</label>
                        <select id="modal-tenant-select" class="login-input" style="background: var(--bg-secondary); color: var(--text-main); border: 1px solid var(--border-color); margin-top: 0.3rem;">
                            <option value="">-- Select Existing Tenant --</option>
                            ${tenantOptions}
                        </select>
                    </div>
                    <div>
                        <label for="modal-tenant-name" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Or Create New Organization</label>
                        <input type="text" id="modal-tenant-name" class="login-input" placeholder="e.g. Beta Corp" style="margin-top: 0.3rem;">
                    </div>
                </div>
            `;
        }

        const modalHtml = `
            <div id="add-user-modal" style="position: fixed; top: 0; left: 0; width: 100vw; height: 100vh; background: rgba(8, 12, 22, 0.7); backdrop-filter: blur(8px); display: flex; align-items: flex-start; justify-content: center; overflow-y: auto; padding: 2rem 1rem; box-sizing: border-box; z-index: 1000; animation: modalFadeIn 0.3s cubic-bezier(0.16, 1, 0.3, 1);">
                <div class="login-card" style="width: 440px; border: 1px solid var(--border-color); background: rgba(15, 22, 42, 0.9); box-shadow: 0 20px 40px rgba(0,0,0,0.5); padding: 2.25rem; position: relative; margin: auto;">
                    <button onclick="document.getElementById('add-user-modal').remove()" style="position: absolute; top: 1.25rem; right: 1.25rem; background: transparent; border: none; color: var(--text-muted); font-size: 1.5rem; cursor: pointer; transition: color 0.2s; line-height: 1;" onmouseover="this.style.color='var(--accent-rose)'" onmouseout="this.style.color='var(--text-muted)'">&times;</button>
                    
                    <div style="text-align: center; margin-bottom: 1.5rem;">
                        <h3 style="font-family: var(--font-display); font-size: 1.25rem; font-weight: 700; color: var(--text-main);">👥 Add New User Account</h3>
                        <p style="color: var(--text-muted); font-size: 0.75rem; margin-top: 0.25rem;">Register a new staff member to the CRM platform.</p>
                    </div>

                    <div id="modal-err-box" style="display: none; background: rgba(244, 63, 94, 0.15); border: 1px solid var(--accent-rose); color: #fda4af; padding: 0.75rem; border-radius: 8px; font-size: 0.75rem; margin-bottom: 1.25rem; text-align: center;"></div>

                    <div style="display: flex; flex-direction: column; gap: 0.85rem;">
                        <div class="form-group">
                            <label for="modal-user-name" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Full Name</label>
                            <input type="text" id="modal-user-name" class="login-input" placeholder="e.g. Alex Mercer" required style="margin-top: 0.3rem;">
                        </div>

                        <div class="form-group">
                            <label for="modal-user-email" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Email Address</label>
                            <input type="email" id="modal-user-email" class="login-input" placeholder="e.g. alex@company.com" required style="margin-top: 0.3rem;">
                        </div>

                        <div class="form-group">
                            <label for="modal-user-password" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Password</label>
                            <input type="password" id="modal-user-password" class="login-input" placeholder="Enter secure password" required style="margin-top: 0.3rem;">
                        </div>

                        <div class="form-group">
                            <label for="modal-user-phone" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Phone Number</label>
                            <input type="text" id="modal-user-phone" class="login-input" placeholder="e.g. +15550100" required style="margin-top: 0.3rem;">
                        </div>

                        ${roleOptionsHtml}
                        ${tenantFieldHtml}

                        <button onclick="Settings.submitAddUserForm()" class="login-submit-btn" style="margin-top: 1.25rem;">
                            Create Account 👤
                        </button>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHtml);
    },

    toggleModalTenantFields(role) {
        const tenantSection = document.getElementById('modal-tenant-section');
        if (!tenantSection) return;
        if (role === UserRoles.SuperAdmin) {
            tenantSection.style.display = 'none';
        } else {
            tenantSection.style.display = 'flex';
        }
    },

    async submitAddUserForm() {
        const errBox = document.getElementById('modal-err-box');
        errBox.style.display = 'none';

        const name = document.getElementById('modal-user-name').value.trim();
        const email = document.getElementById('modal-user-email').value.trim();
        const password = document.getElementById('modal-user-password').value.trim();
        const phone = document.getElementById('modal-user-phone').value.trim();
        const role = document.getElementById('modal-user-role').value;

        if (!name || !email || !password || !phone) {
            errBox.innerText = "Please fill in all required fields.";
            errBox.style.display = 'block';
            return;
        }

        const payload = { name, email, password, phone, role };

        // For SuperAdmin, resolve tenant variables
        const currentUser = Auth.getUser();
        if (currentUser.role === UserRoles.SuperAdmin && role !== UserRoles.SuperAdmin) {
            const tenantSelect = document.getElementById('modal-tenant-select').value;
            const tenantName = document.getElementById('modal-tenant-name').value.trim();

            if (tenantSelect) {
                payload.tenantId = tenantSelect;
            } else if (tenantName) {
                payload.tenantName = tenantName;
            } else {
                errBox.innerText = "Tenant Assignment (Select or New Name) is required.";
                errBox.style.display = 'block';
                return;
            }
        }

        try {
            logConsole(`[API Request] POST /api/auth/register...`);
            const res = await Auth.apiFetch('/api/auth/register', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (!res.ok) {
                const errData = await res.json();
                throw new Error(errData.message || "Registration failed.");
            }

            triggerToast("User Created", `Successfully registered ${name} as ${role}`);
            document.getElementById('add-user-modal').remove();

            // Refresh corresponding views
            if (currentUser.role === 'SuperAdmin') {
                await this.loadPlatformAnalytics();
            } else if (currentUser.role === UserRoles.TenantAdmin) {
                await this.loadTeamRegistry();
            }
        } catch (err) {
            errBox.innerText = err.message;
            errBox.style.display = 'block';
        }
    },

    showEditUserModal(userId, name, email, phone, role) {
        // Remove any existing modal
        const existing = document.getElementById('edit-user-modal');
        if (existing) existing.remove();

        const currentUser = Auth.getUser();
        if (!currentUser) return;

        let roleOptionsHtml = '';

        if (currentUser.role === UserRoles.TenantAdmin) {
            roleOptionsHtml = `
                <div class="form-group" style="margin-top: 0.75rem;">
                    <label style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Account Role</label>
                    <input type="text" class="login-input" value="${role}" readonly style="background: rgba(255,255,255,0.02); color: var(--theme-glow); cursor: not-allowed; border-color: rgba(255,255,255,0.05); margin-top: 0.3rem;">
                    <input type="hidden" id="edit-user-role" value="${role}">
                </div>
            `;
        } else if (currentUser.role === UserRoles.SuperAdmin) {
            roleOptionsHtml = `
                <div class="form-group" style="margin-top: 0.75rem;">
                    <label for="edit-user-role" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Account Role</label>
                    <select id="edit-user-role" class="login-input" style="background: var(--bg-secondary); color: var(--text-main); border: 1px solid var(--border-color); margin-top: 0.3rem;">
                        <option value="${UserRoles.Agent}" ${role === UserRoles.Agent ? 'selected' : ''}>Agent (Standard Staff)</option>
                        <option value="${UserRoles.TenantAdmin}" ${role === UserRoles.TenantAdmin ? 'selected' : ''}>TenantAdmin (Business Owner)</option>
                        <option value="${UserRoles.SuperAdmin}" ${role === UserRoles.SuperAdmin ? 'selected' : ''}>SuperAdmin (Platform Owner)</option>
                    </select>
                </div>
            `;
        }

        const modalHtml = `
            <div id="edit-user-modal" style="position: fixed; top: 0; left: 0; width: 100vw; height: 100vh; background: rgba(8, 12, 22, 0.7); backdrop-filter: blur(8px); display: flex; align-items: flex-start; justify-content: center; overflow-y: auto; padding: 2rem 1rem; box-sizing: border-box; z-index: 1000; animation: modalFadeIn 0.3s cubic-bezier(0.16, 1, 0.3, 1);">
                <div class="login-card" style="width: 440px; border: 1px solid var(--border-color); background: rgba(15, 22, 42, 0.9); box-shadow: 0 20px 40px rgba(0,0,0,0.5); padding: 2.25rem; position: relative; margin: auto;">
                    <button onclick="document.getElementById('edit-user-modal').remove()" style="position: absolute; top: 1.25rem; right: 1.25rem; background: transparent; border: none; color: var(--text-muted); font-size: 1.5rem; cursor: pointer; transition: color 0.2s; line-height: 1;" onmouseover="this.style.color='var(--accent-rose)'" onmouseout="this.style.color='var(--text-muted)'">&times;</button>
                    
                    <div style="text-align: center; margin-bottom: 1.5rem;">
                        <h3 style="font-family: var(--font-display); font-size: 1.25rem; font-weight: 700; color: var(--text-main);">✏️ Edit User Details</h3>
                        <p style="color: var(--text-muted); font-size: 0.75rem; margin-top: 0.25rem;">Update the user account parameters.</p>
                    </div>

                    <div id="edit-modal-err-box" style="display: none; background: rgba(244, 63, 94, 0.15); border: 1px solid var(--accent-rose); color: #fda4af; padding: 0.75rem; border-radius: 8px; font-size: 0.75rem; margin-bottom: 1.25rem; text-align: center;"></div>

                    <div style="display: flex; flex-direction: column; gap: 0.85rem;">
                        <div class="form-group">
                            <label for="edit-user-name" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Full Name</label>
                            <input type="text" id="edit-user-name" class="login-input" value="${name}" required style="margin-top: 0.3rem;">
                        </div>

                        <div class="form-group">
                            <label for="edit-user-email" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Email Address</label>
                            <input type="email" id="edit-user-email" class="login-input" value="${email}" required style="margin-top: 0.3rem;">
                        </div>

                        <div class="form-group">
                            <label for="edit-user-password" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Password (Leave blank to keep current)</label>
                            <input type="password" id="edit-user-password" class="login-input" placeholder="Enter new secure password" style="margin-top: 0.3rem;">
                        </div>

                        <div class="form-group">
                            <label for="edit-user-phone" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Phone Number</label>
                            <input type="text" id="edit-user-phone" class="login-input" value="${phone}" required style="margin-top: 0.3rem;">
                        </div>

                        ${roleOptionsHtml}

                        <button onclick="Settings.submitEditUserForm('${userId}')" class="login-submit-btn" style="margin-top: 1.25rem;">
                            Save Changes 💾
                        </button>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHtml);
    },

    async submitEditUserForm(userId) {
        const name = document.getElementById('edit-user-name').value.trim();
        const email = document.getElementById('edit-user-email').value.trim();
        const password = document.getElementById('edit-user-password').value;
        const phone = document.getElementById('edit-user-phone').value.trim();
        
        const errBox = document.getElementById('edit-modal-err-box');
        if (errBox) {
            errBox.style.display = 'none';
            errBox.innerText = '';
        }

        if (!name || !email || !phone) {
            if (errBox) {
                errBox.style.display = 'block';
                errBox.innerText = 'Full Name, Email, and Phone Number are required.';
            }
            return;
        }

        try {
            const body = { name, email, phone };
            if (password) {
                body.password = password;
            }

            logConsole(`[API Request] PUT /api/auth/users/${userId}...`);
            const res = await Auth.apiFetch(`/api/auth/users/${userId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });

            const data = await res.json();
            if (!res.ok) {
                if (errBox) {
                    errBox.style.display = 'block';
                    errBox.innerText = data.message || 'Failed to update user account details.';
                }
                return;
            }

            triggerToast("User Updated", data.message || "User account details updated successfully.");
            
            // Close modal
            const modal = document.getElementById('edit-user-modal');
            if (modal) modal.remove();

            // Refresh UI list
            const currentUser = Auth.getUser();
            if (currentUser) {
                if (currentUser.role === UserRoles.SuperAdmin) {
                    await this.loadPlatformAnalytics();
                } else if (currentUser.role === UserRoles.TenantAdmin) {
                    await this.loadTeamRegistry();
                }
            }
        } catch (err) {
            logConsole(`[Edit User Error] Failed: ${err.message}`);
            if (errBox) {
                errBox.style.display = 'block';
                errBox.innerText = 'Connection error: ' + err.message;
            }
        }
    },

    async showAddOrganizationModal() {
        const existing = document.getElementById('add-org-modal');
        if (existing) existing.remove();

        const modalHtml = `
            <div id="add-org-modal" style="position: fixed; top: 0; left: 0; width: 100vw; height: 100vh; background: rgba(8, 12, 22, 0.7); backdrop-filter: blur(8px); display: flex; align-items: flex-start; justify-content: center; overflow-y: auto; padding: 2rem 1rem; box-sizing: border-box; z-index: 1000; animation: modalFadeIn 0.3s cubic-bezier(0.16, 1, 0.3, 1);">
                <div class="login-card" style="width: 440px; border: 1px solid var(--border-color); background: rgba(15, 22, 42, 0.9); box-shadow: 0 20px 40px rgba(0,0,0,0.5); padding: 2.25rem; position: relative; margin: auto;">
                    <button onclick="document.getElementById('add-org-modal').remove()" style="position: absolute; top: 1.25rem; right: 1.25rem; background: transparent; border: none; color: var(--text-muted); font-size: 1.5rem; cursor: pointer; transition: color 0.2s; line-height: 1;" onmouseover="this.style.color='var(--accent-rose)'" onmouseout="this.style.color='var(--text-muted)'">&times;</button>
                    
                    <div style="text-align: center; margin-bottom: 1.5rem;">
                        <h3 style="font-family: var(--font-display); font-size: 1.25rem; font-weight: 700; color: var(--text-main);">🏢 Add New Organization</h3>
                        <p style="color: var(--text-muted); font-size: 0.75rem; margin-top: 0.25rem;">Register a new standalone tenant organization in the platform.</p>
                    </div>

                    <div id="modal-org-err-box" style="display: none; background: rgba(244, 63, 94, 0.15); border: 1px solid var(--accent-rose); color: #fda4af; padding: 0.75rem; border-radius: 8px; font-size: 0.75rem; margin-bottom: 1.25rem; text-align: center;"></div>

                    <div style="display: flex; flex-direction: column; gap: 0.85rem;">
                        <div class="form-group">
                            <label for="modal-org-name" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Organization Name</label>
                            <input type="text" id="modal-org-name" class="login-input" placeholder="e.g. Gamma Labs" required oninput="Settings.autoFillOrgAcronym(this.value)" style="margin-top: 0.3rem;">
                        </div>

                        <div class="form-group">
                            <label for="modal-org-logo" style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">Logo Acronym / Tag</label>
                            <input type="text" id="modal-org-logo" class="login-input" placeholder="e.g. GL" maxlength="3" style="margin-top: 0.3rem;">
                            <small style="color:var(--text-muted); font-size:0.65rem; margin-top:0.2rem; display:block;">2 or 3 letters representing the brand tag.</small>
                        </div>

                        <div class="form-group" style="margin-top: 0.5rem;">
                            <label style="font-size:0.7rem; font-weight:700; color:var(--text-muted); text-transform:uppercase;">
                                Dynamic Theme Preset Glow
                            </label>
                            <div class="color-presets" style="margin-top:0.5rem;">
                                <button class="color-btn active" style="background:#00f2fe;" onclick="Settings.selectOrgTheme('#00f2fe', '#4facfe', this)"></button>
                                <button class="color-btn" style="background:#a78bfa;" onclick="Settings.selectOrgTheme('#a78bfa', '#ec4899', this)"></button>
                                <button class="color-btn" style="background:#10b981;" onclick="Settings.selectOrgTheme('#10b981', '#059669', this)"></button>
                                <button class="color-btn" style="background:#f59e0b;" onclick="Settings.selectOrgTheme('#f59e0b', '#d97706', this)"></button>
                            </div>
                            <input type="hidden" id="modal-org-theme" value="#00f2fe|#4facfe">
                        </div>

                        <button onclick="Settings.submitAddOrganizationForm()" class="login-submit-btn" style="margin-top: 1.25rem;">
                            Create Organization 🏢
                        </button>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHtml);
    },

    autoFillOrgAcronym(val) {
        const input = document.getElementById('modal-org-logo');
        if (!input) return;
        const acronym = val.split(' ').map(w => w[0]).join('').substring(0, 3).toUpperCase();
        input.value = acronym;
    },

    selectOrgTheme(start, end, btn) {
        const modal = document.getElementById('add-org-modal');
        if (!modal) return;
        modal.querySelectorAll('.color-presets .color-btn').forEach(b => b.classList.remove('active'));
        if (btn) btn.classList.add('active');
        const themeInput = document.getElementById('modal-org-theme');
        if (themeInput) {
            themeInput.value = `${start}|${end}`;
        }
    },

    async submitAddOrganizationForm() {
        const errBox = document.getElementById('modal-org-err-box');
        errBox.style.display = 'none';

        const name = document.getElementById('modal-org-name').value.trim();
        const logoUrl = document.getElementById('modal-org-logo').value.trim();
        const themeColor = document.getElementById('modal-org-theme').value;

        if (!name) {
            errBox.innerText = "Organization Name is required.";
            errBox.style.display = 'block';
            return;
        }

        const payload = { name, logoUrl, themeColor };

        try {
            logConsole(`[API Request] POST /api/superadmin/tenants...`);
            const res = await Auth.apiFetch('/api/superadmin/tenants', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (!res.ok) {
                const errData = await res.json();
                throw new Error(errData.message || "Failed to create organization.");
            }

            const data = await res.json();
            triggerToast("Organization Created 🏢", data.message);
            document.getElementById('add-org-modal').remove();

            // Refresh SuperAdmin stats and list
            await this.loadPlatformAnalytics();
        } catch (err) {
            errBox.innerText = err.message;
            errBox.style.display = 'block';
        }
    },

    async filterSystemLogs(level) {
        this.logsLevel = level;
        this.logsPage = 1;
        await this.loadSystemLogs();
    },

    // ----------------------------------------------------
    // WHITE-LABEL BRAND CHANGES METHODS (TENANT ADMIN)
    // ----------------------------------------------------
    async loadCurrentBranding() {
        try {
            logConsole(`[API Request] GET /api/auth/branding...`);
            const res = await Auth.apiFetch('/api/auth/branding');
            if (res.ok) {
                const branding = await res.json();
                const input = document.getElementById('settings-brand-input');
                if (input) {
                    input.value = branding.name;
                }

                // Load Logo URL from database
                this.activeLogoUrl = branding.logoUrl;
                const preview = document.getElementById('settings-logo-preview');
                const resetBtn = document.getElementById('settings-logo-reset-btn');
                if (preview) {
                    if (branding.logoUrl && (branding.logoUrl.startsWith('data:image/') || branding.logoUrl.startsWith('http://') || branding.logoUrl.startsWith('https://'))) {
                        preview.innerHTML = `<img src="${branding.logoUrl}" alt="Logo preview">`;
                        preview.classList.add('has-image');
                        if (resetBtn) resetBtn.style.display = 'block';
                    } else {
                        preview.innerHTML = `<img src="images/logo-mark.png?v=3" alt="Default Logo preview">`;
                        preview.classList.remove('has-image');
                        if (resetBtn) resetBtn.style.display = 'none';
                    }
                }

                if (branding.themeColor) {
                    const colors = branding.themeColor.split('|');
                    this.activeGlowStart = colors[0];
                    this.activeGlowEnd = colors[1] || colors[0];

                    // Set theme custom properties
                    document.documentElement.style.setProperty('--theme-glow', this.activeGlowStart);
                    document.documentElement.style.setProperty('--theme-glow-end', this.activeGlowEnd);

                    // Find and activate the matching preset button
                    const presetButtons = document.querySelectorAll('.color-presets .color-btn');
                    presetButtons.forEach(btn => {
                        btn.classList.remove('active');
                        // Get button style background color
                        const bg = btn.style.backgroundColor;
                        if (bg) {
                            const hex = this.rgbToHex(bg);
                            if (hex && hex.toLowerCase() === this.activeGlowStart.toLowerCase()) {
                                btn.classList.add('active');
                            }
                        }
                    });
                }
            }
        } catch (err) {
            logConsole(`[Branding API Error] Failed to load branding settings: ${err.message}`);
        }
    },

    rgbToHex(rgbStr) {
        if (rgbStr.startsWith('#')) return rgbStr;
        const matches = rgbStr.match(/\d+/g);
        if (!matches || matches.length < 3) return null;
        const r = parseInt(matches[0]);
        const g = parseInt(matches[1]);
        const b = parseInt(matches[2]);
        return "#" + ((1 << 24) + (r << 16) + (g << 8) + b).toString(16).slice(1);
    },

    updateWhiteLabelBrand() {
        const input = document.getElementById('settings-brand-input');
        const val = input.value.trim() || "Acme WhatsApp CRM";
        
        // Update header branding text
        document.getElementById('saas-brand-lbl').innerText = val;
        
        // Logo Icon calculation
        const acronym = this.activeLogoUrl && (this.activeLogoUrl.startsWith('data:image/') || this.activeLogoUrl.startsWith('http://') || this.activeLogoUrl.startsWith('https://')) ? 
            this.activeLogoUrl : null;
        
        updateHeaderLogoUI(acronym, val);
        
        // Update preview image if no image uploaded
        const preview = document.getElementById('settings-logo-preview');
        if (preview && (!this.activeLogoUrl || !(this.activeLogoUrl.startsWith('data:image/') || this.activeLogoUrl.startsWith('http://') || this.activeLogoUrl.startsWith('https://')))) {
            preview.innerHTML = `<img src="images/logo-mark.png?v=3" alt="Default Logo preview">`;
        }
    },

    changeThemeGlow(start, end, btn) {
        document.querySelectorAll('.color-presets .color-btn').forEach(b => b.classList.remove('active'));
        if (btn) btn.classList.add('active');

        this.activeGlowStart = start;
        this.activeGlowEnd = end;

        document.documentElement.style.setProperty('--theme-glow', start);
        document.documentElement.style.setProperty('--theme-glow-end', end);

        // Update preview header dynamically
        const brandInput = document.getElementById('settings-brand-input');
        const nameVal = brandInput ? brandInput.value.trim() : "Acme WhatsApp CRM";
        document.getElementById('saas-brand-lbl').innerText = nameVal;
        const acronym = this.activeLogoUrl && (this.activeLogoUrl.startsWith('data:image/') || this.activeLogoUrl.startsWith('http://') || this.activeLogoUrl.startsWith('https://') || this.activeLogoUrl.includes('/')) ? 
            this.activeLogoUrl : nameVal.split(' ').map(w => w[0]).join('').substring(0, 2).toUpperCase();
        
        updateHeaderLogoUI(acronym, nameVal);

        logConsole(`[White-Label Theme] Selected glowing gradient presets: ${start} to ${end}`);
    },

    handleLogoUpload(event) {
        const file = event.target.files[0];
        if (!file) return;

        // Size limit: 300KB
        if (file.size > 300 * 1024) {
            triggerToast("Logo Upload Error", "Image file too large. Max allowed size is 300KB.");
            return;
        }

        const reader = new FileReader();
        reader.onload = (e) => {
            const base64Str = e.target.result;
            this.activeLogoUrl = base64Str;

            // Update settings preview box
            const preview = document.getElementById('settings-logo-preview');
            if (preview) {
                preview.innerHTML = `<img src="${base64Str}" alt="Logo preview">`;
                preview.classList.add('has-image');
            }

            // Show reset button
            const resetBtn = document.getElementById('settings-logo-reset-btn');
            if (resetBtn) resetBtn.style.display = 'block';

            // Instantly update header logo preview
            const brandInput = document.getElementById('settings-brand-input');
            const nameVal = brandInput ? brandInput.value.trim() : "Acme WhatsApp CRM";
            updateHeaderLogoUI(base64Str, nameVal);

            triggerToast("Logo Loaded 🖼️", "Click 'Save Branding 💾' at the bottom to apply changes permanently.");
        };
        reader.readAsDataURL(file);
    },

    resetLogoToDefault() {
        const brandInput = document.getElementById('settings-brand-input');
        const nameVal = brandInput ? brandInput.value.trim() : "Acme WhatsApp CRM";
        const acronym = nameVal.split(' ').map(w => w[0]).join('').substring(0, 2).toUpperCase();

        this.activeLogoUrl = acronym;

        // Reset settings preview box
        const preview = document.getElementById('settings-logo-preview');
        if (preview) {
            preview.innerHTML = `<img src="images/logo-mark.png?v=3" alt="Default Logo preview">`;
            preview.classList.remove('has-image');
        }

        // Hide reset button
        const resetBtn = document.getElementById('settings-logo-reset-btn');
        if (resetBtn) resetBtn.style.display = 'none';

        // Clear file input
        const fileInput = document.getElementById('settings-logo-file-input');
        if (fileInput) fileInput.value = '';

        // Instantly reset header logo
        updateHeaderLogoUI(acronym, nameVal);

        triggerToast("Logo Reset", "Logo reverted to initials. Click 'Save Branding 💾' to save.");
    },

    async saveBrandingSettings() {
        const input = document.getElementById('settings-brand-input');
        if (!input) return;
        const name = input.value.trim();
        if (!name) {
            triggerToast("Validation Error", "Custom Brand Header Name cannot be empty.");
            return;
        }

        const acronym = name.split(' ').map(w => w[0]).join('').substring(0, 2).toUpperCase();
        const themeColor = `${this.activeGlowStart}|${this.activeGlowEnd}`;

        const payload = {
            name: name,
            logoUrl: this.activeLogoUrl || acronym,
            themeColor: themeColor
        };

        try {
            logConsole(`[API Request] PUT /api/auth/branding...`);
            const res = await Auth.apiFetch('/api/auth/branding', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (!res.ok) {
                const errData = await res.json();
                throw new Error(errData.message || "Failed to update branding.");
            }

            triggerToast("Branding Saved 💾", "Settings saved successfully to database!");

            // Update cached session details in local storage so that it matches
            const currentUser = Auth.getUser();
            if (currentUser) {
                currentUser.tenantName = name;
                localStorage.setItem(AUTH_STORAGE_KEYS.USER, JSON.stringify(currentUser));
            }
        } catch (err) {
            triggerToast("Save Error", err.message);
            logConsole(`[Branding API Error] saveBrandingSettings failed: ${err.message}`);
        }
    },

    onMessagingProviderChange(provider) {
        const twilioSection = document.getElementById('settings-twilio-section');
        const metaSection = document.getElementById('settings-meta-section');
        
        if (twilioSection && metaSection) {
            if (provider === 'Twilio') {
                twilioSection.style.display = 'flex';
                metaSection.style.display = 'none';
            } else if (provider === 'Meta') {
                twilioSection.style.display = 'none';
                metaSection.style.display = 'flex';
            } else {
                twilioSection.style.display = 'none';
                metaSection.style.display = 'none';
            }
        }
    },

    async loadCurrentMessaging() {
        try {
            logConsole(`[API Request] GET /api/auth/messaging...`);
            const res = await Auth.apiFetch('/api/auth/messaging');
            if (res.ok) {
                const config = await res.json();
                
                const providerSelect = document.getElementById('settings-msg-provider');
                const numberInput = document.getElementById('settings-msg-number');
                const twilioSid = document.getElementById('settings-twilio-sid');
                const twilioToken = document.getElementById('settings-twilio-token');
                const metaWabaId = document.getElementById('settings-meta-waba-id');
                const metaPhoneId = document.getElementById('settings-meta-phone-id');
                const metaToken = document.getElementById('settings-meta-token');

                if (providerSelect) {
                    providerSelect.value = config.messagingProvider || 'None';
                    this.onMessagingProviderChange(config.messagingProvider || 'None');
                }
                if (numberInput) numberInput.value = config.whatsAppNumber || '';
                
                if (config.messagingProvider === 'Twilio') {
                    if (twilioSid) twilioSid.value = config.providerAccountId || '';
                    if (twilioToken) twilioToken.value = config.providerApiKey || '';
                } else if (config.messagingProvider === 'Meta') {
                    if (metaWabaId) metaWabaId.value = config.providerAccountId || '';
                    if (metaPhoneId) metaPhoneId.value = config.providerSenderId || '';
                    if (metaToken) metaToken.value = config.providerApiKey || '';
                }
            }
        } catch (err) {
            logConsole(`[Messaging API Error] Failed to load messaging settings: ${err.message}`);
        }
    },

    async saveMessagingSettings() {
        const providerSelect = document.getElementById('settings-msg-provider');
        const numberInput = document.getElementById('settings-msg-number');
        if (!providerSelect || !numberInput) return;

        const provider = providerSelect.value;
        const whatsAppNumber = numberInput.value.trim();

        const statusLabel = document.getElementById('messaging-settings-status');
        if (statusLabel) {
            statusLabel.innerText = 'Saving...';
            statusLabel.style.color = 'var(--text-muted)';
        }

        const payload = {
            messagingProvider: provider === 'None' ? null : provider,
            whatsAppNumber: whatsAppNumber
        };

        if (provider === 'Twilio') {
            const twilioSid = document.getElementById('settings-twilio-sid').value.trim();
            const twilioToken = document.getElementById('settings-twilio-token').value.trim();
            
            if (!twilioSid || !twilioToken) {
                triggerToast("Validation Error", "Twilio Account SID and Auth Token are required when Twilio is active.");
                if (statusLabel) statusLabel.innerText = '';
                return;
            }
            payload.providerAccountId = twilioSid;
            payload.providerApiKey = twilioToken;
        } else if (provider === 'Meta') {
            const metaWabaId = document.getElementById('settings-meta-waba-id').value.trim();
            const metaPhoneId = document.getElementById('settings-meta-phone-id').value.trim();
            const metaToken = document.getElementById('settings-meta-token').value.trim();

            if (!metaWabaId || !metaPhoneId || !metaToken) {
                triggerToast("Validation Error", "Meta WABA ID, Phone Number ID, and Access Token are required when Meta Cloud API is active.");
                if (statusLabel) statusLabel.innerText = '';
                return;
            }
            payload.providerAccountId = metaWabaId;
            payload.providerSenderId = metaPhoneId;
            payload.providerApiKey = metaToken;
        }

        try {
            logConsole(`[API Request] PUT /api/auth/messaging...`);
            const res = await Auth.apiFetch('/api/auth/messaging', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (res.ok) {
                triggerToast("Settings Saved", "Unified WhatsApp Channel configuration saved successfully.");
                if (statusLabel) {
                    statusLabel.innerText = 'Saved 💾';
                    statusLabel.style.color = 'var(--accent-emerald)';
                }
                
                // Reload session dynamically
                await this.loadCurrentMessaging();
            } else {
                const errData = await res.json();
                triggerToast("Save Error", errData.message || "Failed to save messaging configurations.");
                if (statusLabel) {
                    statusLabel.innerText = 'Error ❌';
                    statusLabel.style.color = 'var(--accent-rose)';
                }
            }
        } catch (err) {
            triggerToast("Save Error", err.message);
            logConsole(`[Messaging API Error] saveMessagingSettings failed: ${err.message}`);
            if (statusLabel) {
                statusLabel.innerText = 'Connection Error ❌';
                statusLabel.style.color = 'var(--accent-rose)';
            }
        }
    },

    copyToClipboard(text, message) {
        navigator.clipboard.writeText(text).then(() => {
            triggerToast("Copied!", message || "Text copied to clipboard!");
        }).catch(err => {
            logConsole(`[Clipboard Error] Failed to copy text: ${err}`);
            // Fallback for older browsers
            const input = document.createElement('textarea');
            input.value = text;
            input.style.position = 'fixed';
            input.style.opacity = '0';
            document.body.appendChild(input);
            input.select();
            try {
                document.execCommand('copy');
                triggerToast("Copied!", message || "Text copied to clipboard!");
            } catch (e) {
                triggerToast("Error", "Failed to copy text.");
            }
            document.body.removeChild(input);
        });
    }
};
