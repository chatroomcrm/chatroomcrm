/* =========================================================================
   CHATFLOW WHATSAPP CRM - MODULAR DATABASE INSPECTOR SERVICE (db.js)
   ========================================================================= */

const DbInspector = {
    activeTable: 'Tenants',

    async initialize() {
        this.renderTable();
    },

    async switchTable(tableName, btn) {
        document.querySelectorAll('.db-tabs .db-tab-btn').forEach(b => b.classList.remove('active'));
        if (btn) btn.classList.add('active');

        this.activeTable = tableName;
        await this.renderTable();
    },

    async renderTable() {
        const table = document.getElementById('db-inspector-table');
        if (!table) return;

        const currentUser = Auth.getUser();
        
        // Strictly Enforce Role Access Control boundaries!
        // Only Platform Super Administrators are allowed to access Database Inspector!
        if (currentUser && currentUser.role !== UserRoles.SuperAdmin) {
            table.innerHTML = `
                <tr>
                    <td style="padding: 3rem; text-align: center;">
                        <div style="font-size: 2rem; margin-bottom: 0.5rem;">🔒</div>
                        <h4 style="font-family: var(--font-display); font-weight:600; color: var(--accent-rose);">Access Restricted</h4>
                        <p style="color: var(--text-muted); font-size: 0.75rem; margin-top: 0.25rem;">
                            Relational SQL database tables can only be inspected by Platform Super Administrators.
                        </p>
                    </td>
                </tr>
            `;
            return;
        }

        let rows = [];

        try {
            if (this.activeTable === 'Tenants') {
                // SuperAdmin can view all tenants, TenantAdmin can only view theirs
                if (currentUser.role === UserRoles.SuperAdmin) {
                    logConsole(`[API Request] GET /api/superadmin/tenants...`);
                    const res = await Auth.apiFetch('/api/superadmin/tenants');
                    rows = await res.json();
                } else {
                    // TenantAdmin gets their own tenant
                    rows = [ { Id: currentUser.tenantId, Name: currentUser.tenantName, ThemeColor: '#00f2fe' } ];
                }
            } else if (this.activeTable === 'Users') {
                // Fetch team members
                if (currentUser.role === UserRoles.SuperAdmin) {
                    logConsole(`[API Request] GET /api/superadmin/users...`);
                    const res = await Auth.apiFetch('/api/superadmin/users');
                    rows = await res.json();
                } else {
                    logConsole(`[API Request] GET /api/auth/team...`);
                    // Use local cache of Users populated from live logins/events
                    rows = db.Users.filter(u => u.TenantId === currentUser.tenantId || u.tenantId === currentUser.tenantId);
                }
            } else if (this.activeTable === 'Contacts') {
                rows = db.Contacts;
            } else if (this.activeTable === 'Leads') {
                rows = db.Leads;
            } else if (this.activeTable === 'Messages') {
                rows = db.Messages;
            } else if (this.activeTable === 'Tasks') {
                rows = db.Tasks;
            }
        } catch (err) {
            logConsole(`[Database Inspector API Error] ${err.message}. Showing cached rows.`);
            rows = db[this.activeTable] || [];
        }

        table.innerHTML = '';

        if (!rows || rows.length === 0) {
            table.innerHTML = `<tr><td style="color:var(--text-muted); text-align:center; padding: 2rem; font-size: 0.75rem;">Empty Table</td></tr>`;
            return;
        }

        // Render Headers
        const firstRow = rows[0];
        const keys = Object.keys(firstRow);
        
        let headerHtml = '<tr>';
        keys.forEach(key => {
            headerHtml += `<th>${key}</th>`;
        });
        headerHtml += '</tr>';
        table.innerHTML += headerHtml;

        // Render Rows
        rows.forEach(row => {
            let rowHtml = '<tr>';
            keys.forEach(key => {
                let val = row[key];
                
                // Pretty format objects or long Guids
                if (typeof val === 'object' && val !== null) {
                    val = JSON.stringify(val);
                }
                if (typeof val === 'string' && val.length > 30) {
                    val = val.substring(0, 27) + '...';
                }
                
                rowHtml += `<td>${val}</td>`;
            });
            rowHtml += '</tr>';
            table.innerHTML += rowHtml;
        });
    }
};
