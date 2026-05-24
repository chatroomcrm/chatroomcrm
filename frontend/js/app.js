/* =========================================================================
   CHATFLOW WHATSAPP CRM - CORE APPLICATION STATE & APP CONTROLLER (app.js)
   ========================================================================= */

// ----------------------------------------------------
// IN-MEMORY DATABASE STATE (Pre-allocated for Live Data Synchronization)
// ----------------------------------------------------
const db = {
    Tenants: [],
    Users: [],
    Contacts: [],
    Leads: [],
    Messages: [],
    Tasks: []
};

let activeLeadId = null;

// ----------------------------------------------------
// DOCUMENT STARTUP LOAD BINDINGS
// ----------------------------------------------------
document.addEventListener("DOMContentLoaded", () => {
    // Configure event bindings on login form fields
    const emailInput = document.getElementById('login-email');
    if (emailInput) {
        emailInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') handleLoginSubmit();
        });
    }

    const passInput = document.getElementById('login-password');
    if (passInput) {
        passInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') handleLoginSubmit();
        });
    }

    const replyInbound = document.getElementById('crm-input-reply');
    if (replyInbound) {
        replyInbound.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') sendOutboundMessage();
        });
    }

    // Auto load session if already authenticated
    if (Auth.isAuthenticated()) {
        hideLoginOverlay();
        bootstrapApp();
    } else {
        showLoginOverlay();
    }
});

// Dynamic Header Logo Helper (supports both Text Initials and Image Logos/Base64)
function updateHeaderLogoUI(logoUrl, brandName) {
    const container = document.getElementById('saas-logo-icon');
    if (!container) return;

    // Helper to dynamically update tab favicon
    const updateFavicon = (url) => {
        let link = document.querySelector("link[rel~='icon']");
        if (!link) {
            link = document.createElement('link');
            link.rel = 'icon';
            link.type = 'image/png';
            document.getElementsByTagName('head')[0].appendChild(link);
        }
        link.href = url || 'images/logo-mark.png?v=3';
    };

    if (logoUrl && (logoUrl.startsWith('data:image/') || logoUrl.startsWith('http://') || logoUrl.startsWith('https://'))) {
        container.innerHTML = `<img src="${logoUrl}" alt="${brandName}" style="width: 100%; height: 100%; object-fit: cover; border-radius: 50%;">`;
        container.style.background = 'transparent';
        container.style.border = 'none';
        container.style.boxShadow = 'none';
        updateFavicon(logoUrl);
    } else {
        // Default circular ChatRoom logo fallback for all
        container.innerHTML = `<img src="images/logo-mark.png?v=3" alt="ChatRoom Logo" style="width: 100%; height: 100%; object-fit: cover; border-radius: 50%;">`;
        container.style.background = 'transparent';
        container.style.border = 'none';
        container.style.boxShadow = 'none';
        updateFavicon('images/logo-mark.png?v=3');
    }
}

// App Startup Bootstrapper
async function bootstrapApp() {
    const user = Auth.getUser();
    if (!user) return;

    logConsole(`[System Core] Session verified: Welcome ${user.name} (${user.role})`);
    
    // Update Header Brand (Immediate fallback before live fetch)
    document.getElementById('saas-brand-lbl').innerText = user.tenantName;
    updateHeaderLogoUI(null, user.tenantName);

    // Update User Profile Badge dynamically
    const nameSpan = document.getElementById('user-display-name');
    if (nameSpan) nameSpan.innerText = user.name || "Logged User";
    
    const roleSpan = document.getElementById('user-display-role');
    if (roleSpan) {
        roleSpan.innerText = UserRoleLabels[user.role] || user.role;
    }

    const avatarDiv = document.getElementById('user-avatar');
    if (avatarDiv) {
        const userInitials = user.name ? user.name.split(' ').map(w => w[0]).join('').substring(0, 2).toUpperCase() : 'US';
        avatarDiv.innerText = userInitials;
    }

    // Live Database-Backed Branding Customization Fetch
    try {
        logConsole(`[API Request] GET /api/auth/branding...`);
        const brandingRes = await Auth.apiFetch('/api/auth/branding');
        if (brandingRes.ok) {
            const branding = await brandingRes.json();
            document.getElementById('saas-brand-lbl').innerText = branding.name;
            updateHeaderLogoUI(branding.logoUrl, branding.name);

            // Apply DB-persisted glowing color presets
            if (branding.themeColor) {
                const colors = branding.themeColor.split('|');
                const startColor = colors[0];
                const endColor = colors[1] || colors[0];
                document.documentElement.style.setProperty('--theme-glow', startColor);
                document.documentElement.style.setProperty('--theme-glow-end', endColor);
            }
        }
    } catch (e) {
        logConsole(`[Branding Startup Error] Failed to load live branding: ${e.message}`);
    }

    // Apply strict access configuration to navigation menu based on user role!
    const navButtons = document.querySelectorAll('.saas-nav button');
    
    // Dynamically show/hide or rewrite navigation headers
    navButtons.forEach(btn => {
        // Extract target view ID from switchView('viewName', this) attribute
        const onclickAttr = btn.getAttribute('onclick') || '';
        const match = onclickAttr.match(/switchView\(['"]([^'"]+)['"]/);
        const viewId = match ? match[1] : '';

        // Save the user's custom original HTML from index.html to avoid overwriting it
        if (!btn.hasAttribute('data-original-html')) {
            btn.setAttribute('data-original-html', btn.innerHTML);
        }
        const originalHtml = btn.getAttribute('data-original-html');
        
        // SuperAdmin can see Dashboard, Templates, and Platform Settings. Hide Chats, Leads Board, and SQL DB Viewer!
        if (user.role === UserRoles.SuperAdmin) {
            if (viewId === 'database' || viewId === 'kanban' || viewId === 'chats') {
                btn.style.display = 'none';
            } else {
                btn.style.display = 'flex';
                // Rewrite settings/branding button to platform console for super admins
                if (viewId === 'settings') {
                    btn.innerHTML = `Admin <span style="font-size: 8px; vertical-align: middle;">⚙️</span>`;
                } else {
                    btn.innerHTML = originalHtml;
                }
            }
        } else if (user.role === UserRoles.Agent) {
            // Agent users only see Dashboard, Real-Time Chats, Kanban. Hide SQL Database Viewer, Templates, and Branding Settings!
            if (viewId === 'database' || viewId === 'settings' || viewId === 'templates') {
                btn.style.display = 'none';
            } else {
                btn.style.display = 'flex';
                btn.innerHTML = originalHtml;
            }
        } else {
            // TenantAdmin shows Dashboard, Chats, Kanban, Templates, and Branding Settings but NOT SQL Database Viewer
            if (viewId === 'database') {
                btn.style.display = 'none';
            } else {
                btn.style.display = 'flex';
                if (viewId === 'settings') {
                    btn.innerHTML = `Admin <span style="font-size: 8px; vertical-align: middle;">⚙️</span>`;
                } else {
                    btn.innerHTML = originalHtml;
                }
            }
        }
    });

    // Make default active view selection based on role: SuperAdmin boots into settings platform console directly
    if (user.role === UserRoles.SuperAdmin) {
        const platformBtn = Array.from(navButtons).find(btn => {
            const onclickAttr = btn.getAttribute('onclick') || '';
            const match = onclickAttr.match(/switchView\(['"]([^'"]+)['"]/);
            return match && match[1] === 'settings';
        });
        if (platformBtn) {
            switchView('settings', platformBtn);
        }
    } else {
        // Standard user boots into dashboard
        const dashBtn = Array.from(navButtons).find(btn => {
            const onclickAttr = btn.getAttribute('onclick') || '';
            const match = onclickAttr.match(/switchView\(['"]([^'"]+)['"]/);
            return match && match[1] === 'dashboard';
        });
        if (dashBtn) {
            switchView('dashboard', dashBtn);
        }
    }

    // Initialize sub-modules
    if (typeof Chat !== 'undefined') await Chat.initialize();
    if (typeof Kanban !== 'undefined') await Kanban.initialize();
    if (typeof Templates !== 'undefined') await Templates.initialize();
    if (typeof DbInspector !== 'undefined') await DbInspector.initialize();
    if (typeof Settings !== 'undefined') await Settings.initialize();

    updateDashboardStats();

    // Set up real-time periodic dashboard refresh interval (every 10 seconds)
    if (window.dashboardInterval) {
        clearInterval(window.dashboardInterval);
    }
    window.dashboardInterval = setInterval(() => {
        const activePane = document.querySelector('.saas-content .view-pane.active');
        if (Auth.isAuthenticated() && activePane && activePane.id === 'view-dashboard') {
            updateDashboardStats();
        }
    }, 10000);
}

// ----------------------------------------------------
// CRM PANE TOGGLES
// ----------------------------------------------------
function switchView(viewId, btn) {
    document.querySelectorAll('.saas-nav .nav-btn').forEach(b => b.classList.remove('active'));
    if (btn) btn.classList.add('active');

    document.querySelectorAll('.saas-content .view-pane').forEach(v => v.classList.remove('active'));
    const pane = document.getElementById('view-' + viewId);
    if (pane) pane.classList.add('active');

    logConsole(`Switched active workspace pane to: ${viewId.toUpperCase()}`);

    // Refresh view states
    if (viewId === 'dashboard') {
        updateDashboardStats();
    } else if (viewId === 'templates' && typeof Templates !== 'undefined') {
        Templates.loadTemplatesList();
    } else if (viewId === 'database' && typeof DbInspector !== 'undefined') {
        DbInspector.renderTable();
    } else if (viewId === 'settings' && typeof Settings !== 'undefined') {
        Settings.renderPane();
        const user = Auth.getUser();
        if (user && user.role === UserRoles.SuperAdmin) {
            Settings.loadPlatformAnalytics();
        } else if (user && user.role === UserRoles.TenantAdmin) {
            Settings.loadTeamRegistry();
            Settings.loadCurrentBranding();
        }
    }
}

// ----------------------------------------------------
// CRM REPLY ACTION
// ----------------------------------------------------
function sendOutboundMessage() {
    if (typeof Chat !== 'undefined') {
        Chat.sendOutboundReply();
    }
}

// ----------------------------------------------------
// AGGREGATE DASHBOARD ANALYTICS COUNTERS
// ----------------------------------------------------
async function updateDashboardStats() {
    try {
        logConsole(`[API Request] GET /api/leads/analytics...`);
        const res = await Auth.apiFetch('/api/leads/analytics');
        if (!res.ok) return;
        const data = await res.json();

        // Update aggregate cards
        const totalLeadsLabel = document.getElementById('metric-total-leads');
        if (totalLeadsLabel) totalLeadsLabel.innerText = data.totalLeads;
        
        const rateLabel = document.getElementById('metric-conversion-rate');
        if (rateLabel) rateLabel.innerText = Math.round(data.conversionRate) + '%';
        
        const activeChatsLabel = document.getElementById('metric-active-chats');
        if (activeChatsLabel) activeChatsLabel.innerText = data.activeChats;

        const responseTimeLabel = document.getElementById('metric-response-time');
        if (responseTimeLabel) responseTimeLabel.innerText = data.avgResponseMinutes + 'm';

        // ---------------------------------------------
        // RENDER CURVED LINE GRAPH (WhatsApp Message Velocity)
        // ---------------------------------------------
        const velocityWrapper = document.getElementById('velocity-chart-wrapper');
        if (velocityWrapper && data.velocity && data.velocity.length > 0) {
            const counts = data.velocity.map(v => v.count);
            const dayNames = data.velocity.map(v => v.dayName);
            const maxVal = Math.max(...counts, 5); // default min height scale of 5 messages

            // Calculate SVG coordinates
            const points = [];
            const width = 500;
            const height = 200;
            const xStep = (width - 40) / (data.velocity.length - 1 || 1);

            for (let i = 0; i < data.velocity.length; i++) {
                const x = 20 + i * xStep;
                // Scale y: 170 is bottom grid line, 30 is top boundary
                const y = 170 - (counts[i] / maxVal) * 140;
                points.push({ x, y, val: counts[i], day: dayNames[i] });
            }

            // Create Path definition for line and area fill
            let linePath = `M ${points[0].x} ${points[0].y} `;
            let areaPath = `M ${points[0].x} 170 L ${points[0].x} ${points[0].y} `;

            // Build smooth curves (Sleek Quadratic curves)
            for (let i = 1; i < points.length; i++) {
                const prev = points[i - 1];
                const curr = points[i];
                const cpX = prev.x + (curr.x - prev.x) / 2;
                linePath += `Q ${cpX} ${prev.y} ${curr.x} ${curr.y} `;
                areaPath += `Q ${cpX} ${prev.y} ${curr.x} ${curr.y} `;
            }
            areaPath += `L ${points[points.length - 1].x} 170 Z`;

            // Draw dynamic dots/circles
            const circlesHtml = points.map((p, idx) => `
                <g class="chart-point-group" style="cursor: pointer;">
                    <circle cx="${p.x}" cy="${p.y}" r="10" fill="transparent" />
                    <circle cx="${p.x}" cy="${p.y}" r="4.5" fill="var(--theme-glow)" stroke="#0a0f1d" stroke-width="1.5" />
                    <title>${p.day}: ${p.val} messages</title>
                </g>
            `).join('');

            // Draw Dynamic X Axis Labels inside SVG
            const labelsHtml = points.map(p => `
                <text x="${p.x}" y="192" font-size="9" fill="var(--text-muted)" font-family="var(--font-mono)" text-anchor="middle">${p.day}</text>
            `).join('');

            velocityWrapper.innerHTML = `
                <svg viewBox="0 0 500 200" style="width: 100%; height:100%;">
                    <!-- Background grid lines -->
                    <line x1="0" y1="50" x2="500" y2="50" stroke="rgba(255,255,255,0.03)" stroke-width="1" />
                    <line x1="0" y1="100" x2="500" y2="100" stroke="rgba(255,255,255,0.03)" stroke-width="1" />
                    <line x1="0" y1="150" x2="500" y2="150" stroke="rgba(255,255,255,0.03)" stroke-width="1" />
                    <line x1="0" y1="170" x2="500" y2="170" stroke="rgba(255,255,255,0.05)" stroke-width="1" />
                    
                    <!-- Area Fill -->
                    <path d="${areaPath}" fill="url(#area-gradient)" opacity="0.12" />
                    
                    <!-- Curved Line -->
                    <path d="${linePath}" fill="none" stroke="url(#line-gradient)" stroke-width="3" />
                    
                    <!-- Circles on points -->
                    ${circlesHtml}
                    
                    <!-- Labels -->
                    ${labelsHtml}
                    
                    <defs>
                        <linearGradient id="area-gradient" x1="0" y1="0" x2="0" y2="1">
                            <stop offset="0%" stop-color="var(--theme-glow)" />
                            <stop offset="100%" stop-color="transparent" />
                        </linearGradient>
                        <linearGradient id="line-gradient" x1="0" y1="0" x2="1" y2="0">
                            <stop offset="0%" stop-color="var(--theme-glow)" />
                            <stop offset="100%" stop-color="var(--theme-glow-end)" />
                        </linearGradient>
                    </defs>
                </svg>
            `;
        }

        // ---------------------------------------------
        // RENDER DONUT GRAPH (Lead Distribution)
        // ---------------------------------------------
        const donutWrapper = document.getElementById('donut-chart-wrapper');
        if (donutWrapper) {
            // Get database distribution ratios
            const bk = data.pipelineBreakdown || {};
            const won = bk['Won'] || bk['won'] || 0;
            const lost = bk['Lost'] || bk['lost'] || 0;
            const pending = data.totalLeads - won - lost;

            const total = data.totalLeads || 1;
            const wonPct = won / total;
            const lostPct = lost / total;
            const pendingPct = pending / total;

            const circumference = 238.76;
            const wonStroke = wonPct * circumference;
            const lostStroke = lostPct * circumference;
            const pendingStroke = pendingPct * circumference;

            const wonOffset = 0;
            const lostOffset = -wonStroke;
            const pendingOffset = -(wonStroke + lostStroke);

            // Won percentage value text
            const displayWonPct = data.totalLeads > 0 ? Math.round(wonPct * 100) : 0;

            donutWrapper.innerHTML = `
                <svg viewBox="0 0 100 100" style="width: 140px; height: 140px; transform: rotate(-90deg);">
                    <circle cx="50" cy="50" r="38" fill="transparent" stroke="rgba(255,255,255,0.03)" stroke-width="12" />
                    
                    <!-- Segment 1: Won -->
                    <circle cx="50" cy="50" r="38" fill="transparent" stroke="var(--accent-emerald)" stroke-width="12" 
                        stroke-dasharray="${wonStroke} ${circumference}" stroke-dashoffset="${wonOffset}" style="transition: stroke-dasharray 0.5s ease;" />
                    
                    <!-- Segment 2: Lost -->
                    <circle cx="50" cy="50" r="38" fill="transparent" stroke="var(--accent-rose)" stroke-width="12" 
                        stroke-dasharray="${lostStroke} ${circumference}" stroke-dashoffset="${lostOffset}" style="transition: stroke-dasharray 0.5s ease;" />
                    
                    <!-- Segment 3: Pending / In Progress -->
                    <circle cx="50" cy="50" r="38" fill="transparent" stroke="var(--theme-glow)" stroke-width="12" 
                        stroke-dasharray="${pendingStroke} ${circumference}" stroke-dashoffset="${pendingOffset}" style="transition: stroke-dasharray 0.5s ease;" />
                </svg>
                <div style="position: absolute; text-align: center;">
                    <span style="font-size: 0.65rem; color: var(--text-muted); text-transform: uppercase;">Won Ratio</span>
                    <div style="font-family: var(--font-display); font-size: 1.25rem; font-weight:700; color: var(--text-main);">${displayWonPct}%</div>
                </div>
            `;
        }

    } catch (err) {
        logConsole(`[Dashboard Analytics Error] Failed: ${err.message}`);
    }
}

// ----------------------------------------------------
// IN-APP SYSTEM TELEMETRY COUNTERS
// ----------------------------------------------------
function logConsole(message) {
    console.log(`[WCRM Log] ${message}`);
}

// System toast dispatcher
function triggerToast(title, body) {
    const toast = document.getElementById('system-toast');
    if (!toast) return;

    document.getElementById('toast-title').innerText = title;
    document.getElementById('toast-body').innerText = body;
    
    toast.style.display = 'flex';

    // Play sleek premium tone using AudioContext synthesizer
    try {
        const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
        const osc = audioCtx.createOscillator();
        const gain = audioCtx.createGain();
        osc.connect(gain);
        gain.connect(audioCtx.destination);
        
        osc.frequency.setValueAtTime(880, audioCtx.currentTime); // A5 note
        osc.type = 'triangle';
        gain.gain.setValueAtTime(0.08, audioCtx.currentTime);
        gain.gain.exponentialRampToValueAtTime(0.001, audioCtx.currentTime + 0.15);
        
        osc.start();
        osc.stop(audioCtx.currentTime + 0.15);
    } catch (e) {}

    setTimeout(() => {
        toast.style.display = 'none';
    }, 4000);
}

// ----------------------------------------------------
// EXPORT GLOBAL APP NAMESPACE
// ----------------------------------------------------
const App = {
    bootstrapApp,
    switchView,
    sendOutboundMessage,
    updateDashboardStats,
    logConsole,
    triggerToast
};
window.App = App;
