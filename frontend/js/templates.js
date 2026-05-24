/* =========================================================================
   CHATFLOW WHATSAPP CRM - MESSAGE TEMPLATES CONTROLLER (templates.js)
   ========================================================================= */

const Templates = {
    selectedTenantId: null,

    initialize: async function() {
        App.logConsole("[Templates Module] Initializing templates view...");
        
        // Setup file input listener for upload styling
        const fileInput = document.getElementById('templates-csv-file');
        if (fileInput) {
            fileInput.addEventListener('change', (e) => {
                const fileNameSpan = document.getElementById('templates-file-name');
                if (fileNameSpan) {
                    if (e.target.files.length > 0) {
                        fileNameSpan.innerText = e.target.files[0].name;
                        fileNameSpan.style.color = 'var(--theme-glow)';
                    } else {
                        fileNameSpan.innerText = 'No file selected';
                        fileNameSpan.style.color = 'var(--text-muted)';
                    }
                }
            });
        }

        // Configure Tenant Selector dropdown visibility and data population based on role
        const user = Auth.getUser();
        const selectorGroup = document.getElementById('templates-tenant-selector-group');
        const displayBadge = document.getElementById('templates-tenant-display-badge');
        const tenantNameLbl = document.getElementById('templates-tenant-name-lbl');
        
        if (user && user.role === 'SuperAdmin') {
            if (selectorGroup) selectorGroup.style.display = 'flex';
            if (displayBadge) displayBadge.style.display = 'none';
            await this.populateTenantDropdown();
        } else {
            if (selectorGroup) selectorGroup.style.display = 'none';
            if (displayBadge) {
                displayBadge.style.display = 'flex';
                if (tenantNameLbl && user) {
                    tenantNameLbl.innerText = user.tenantName || "My Organization";
                }
            }
            this.selectedTenantId = null;
        }
    },

    populateTenantDropdown: async function() {
        const select = document.getElementById('templates-tenant-select');
        if (!select) return;

        try {
            App.logConsole("[API Request] GET /api/superadmin/tenants...");
            const res = await Auth.apiFetch('/api/superadmin/tenants');
            if (res.ok) {
                const tenants = await res.json();
                
                if (tenants.length > 0) {
                    select.innerHTML = tenants.map(t => 
                        `<option value="${t.id}">${t.name}</option>`
                    ).join('');
                    
                    // Set active tenant ID to the first tenant by default
                    this.selectedTenantId = select.value;
                } else {
                    select.innerHTML = '<option value="">No Tenants Found</option>';
                    this.selectedTenantId = null;
                }
            } else {
                select.innerHTML = '<option value="">Error loading tenants</option>';
            }
        } catch (e) {
            App.logConsole(`[Tenant Load Error] Failed: ${e.message}`);
            select.innerHTML = '<option value="">Connection error</option>';
        }
    },

    handleTenantChange: function(tenantId) {
        this.selectedTenantId = tenantId;
        App.logConsole(`[Templates Module] Active tenant changed to: ${tenantId}`);
        this.loadTemplatesList();
    },

    loadTemplatesList: async function() {
        const tableBody = document.getElementById('templates-list-body');
        if (!tableBody) return;

        tableBody.innerHTML = `
            <tr>
                <td colspan="5" style="text-align: center; color: var(--text-muted); padding: 2rem;">
                    <div class="animate-pulse">Loading templates from live database...</div>
                </td>
            </tr>
        `;

        try {
            const queryParam = this.selectedTenantId ? `?tenantId=${this.selectedTenantId}` : '';
            App.logConsole(`[API Request] GET /api/templates${queryParam}...`);
            const res = await Auth.apiFetch(`/api/templates${queryParam}`);
            if (res.ok) {
                const list = await res.json();
                this.renderTemplatesTable(list);
            } else {
                tableBody.innerHTML = `
                    <tr>
                        <td colspan="5" style="text-align: center; color: var(--accent-rose); padding: 2rem;">
                            Failed to load message templates. (Status ${res.status})
                        </td>
                    </tr>
                `;
            }
        } catch (e) {
            tableBody.innerHTML = `
                <tr>
                    <td colspan="5" style="text-align: center; color: var(--accent-rose); padding: 2rem;">
                        Connection error: ${e.message}
                    </td>
                </tr>
            `;
        }
    },

    renderTemplatesTable: function(list) {
        const tableBody = document.getElementById('templates-list-body');
        if (!tableBody) return;

        if (list.length === 0) {
            tableBody.innerHTML = `
                <tr>
                    <td colspan="5" style="text-align: center; color: var(--text-muted); padding: 3rem;">
                        No message templates found. Upload a CSV file above to bulk import!
                    </td>
                </tr>
            `;
            return;
        }

        tableBody.innerHTML = list.map((item, idx) => {
            const statusClass = item.status === 'Approved' ? 'badge-emerald' : 'badge-amber';
            
            return `
                <tr style="border-bottom: 1px solid rgba(255,255,255,0.02); transition: background 0.2s;" onmouseover="this.style.background='rgba(255,255,255,0.01)'" onmouseout="this.style.background='transparent'">
                    <td style="padding: 1rem; color: var(--text-main); font-family: var(--font-mono); font-size: 0.8rem; font-weight: 600;">
                        ${item.name}
                    </td>
                    <td style="padding: 1rem;">
                        <span class="badge" style="background: rgba(255,255,255,0.05); color: var(--text-main); font-size: 0.7rem; border-radius: 4px; padding: 0.2rem 0.4rem; font-family: var(--font-mono);">
                            ${item.category}
                        </span>
                    </td>
                    <td style="padding: 1rem; color: var(--text-muted); font-size: 0.75rem; font-family: var(--font-mono);">
                        ${item.language}
                    </td>
                    <td style="padding: 1rem; color: var(--text-main); font-size: 0.8rem; max-width: 300px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;" title="${item.body}">
                        ${item.body}
                    </td>
                    <td style="padding: 1rem;">
                        <span class="badge ${statusClass}" style="font-size: 0.7rem; border-radius: 4px; padding: 0.2rem 0.5rem; font-weight: 600;">
                            ${item.status}
                        </span>
                    </td>
                </tr>
            `;
        }).join('');
    },

    uploadCsv: async function(event) {
        if (event) event.preventDefault();

        const fileInput = document.getElementById('templates-csv-file');
        if (!fileInput || fileInput.files.length === 0) {
            App.triggerToast("File Required", "Please select a templates.csv file to upload.");
            return;
        }

        const btn = document.getElementById('btn-upload-templates');
        const originalText = btn ? btn.innerText : 'Upload Templates';
        if (btn) {
            btn.disabled = true;
            btn.innerText = 'Uploading... ⚡';
        }

        const file = fileInput.files[0];
        const formData = new FormData();
        formData.append('file', file);

        try {
            const queryParam = this.selectedTenantId ? `?tenantId=${this.selectedTenantId}` : '';
            App.logConsole(`[API Request] POST /api/templates/upload${queryParam} (multipart/form-data)...`);
            
            const token = Auth.getToken();
            const apiUrl = Auth.getApiUrl();

            const res = await fetch(`${apiUrl}/api/templates/upload${queryParam}`, {
                method: 'POST',
                headers: {
                    'Authorization': `Bearer ${token}`
                },
                body: formData
            });

            if (res.ok) {
                const data = await res.json();
                App.triggerToast("Upload Success!", `Imported ${data.importedCount} templates successfully.`);
                
                // Clear input
                fileInput.value = '';
                const fileNameSpan = document.getElementById('templates-file-name');
                if (fileNameSpan) fileNameSpan.innerText = 'No file selected';

                // Refresh table
                await this.loadTemplatesList();
            } else {
                const errText = await res.text();
                App.triggerToast("Upload Failed", errText || `Server returned error status ${res.status}`);
            }
        } catch (e) {
            App.triggerToast("Upload Error", `Connection failed: ${e.message}`);
        } finally {
            if (btn) {
                btn.disabled = false;
                btn.innerText = originalText;
            }
        }
    }
};

window.Templates = Templates;
