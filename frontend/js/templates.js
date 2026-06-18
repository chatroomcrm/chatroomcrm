/* =========================================================================
   CHATFLOW WHATSAPP CRM - MESSAGE TEMPLATES CONTROLLER (templates.js)
   ========================================================================= */

const Templates = {
    selectedTenantId: null,
    page: 1,
    search: '',

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

        // Proactively insert search bar in the card header if it doesn't exist yet!
        const cardHeader = document.querySelector('.templates-inventory-card > div:first-child');
        if (cardHeader && !document.getElementById('templates-search-input')) {
            cardHeader.innerHTML += `
                <div style="display:flex; gap:0.5rem; align-items:center; margin-left: auto; margin-right: 1.5rem;">
                    <input type="text" id="templates-search-input" placeholder="🔍 Search template..." oninput="Templates.handleSearch(this.value)" style="padding:0.35rem 0.6rem; font-size:0.75rem; background:rgba(255,255,255,0.03); border:1px solid rgba(255,255,255,0.08); border-radius:6px; color:var(--text-main); width:180px;">
                </div>
            `;
        }

        // Proactively insert pagination controls at the bottom of the card if they don't exist yet!
        const cardContainer = document.querySelector('.templates-inventory-card');
        if (cardContainer && !document.getElementById('templates-pagination')) {
            cardContainer.insertAdjacentHTML('beforeend', `
                <div id="templates-pagination" class="pagination-controls" style="display:flex; justify-content:space-between; align-items:center; padding:1rem 1.5rem; border-top:1px solid var(--border-color); font-size:0.7rem; color:var(--text-muted); flex-shrink:0;">
                    <button class="login-mode-btn" id="btn-templates-prev" onclick="Templates.changePage(-1)" style="padding:0.25rem 0.5rem; font-size:0.65rem;">◀ Prev</button>
                    <span id="info-templates-page">Page 1</span>
                    <button class="login-mode-btn" id="btn-templates-next" onclick="Templates.changePage(1)" style="padding:0.25rem 0.5rem; font-size:0.65rem;">Next ▶</button>
                </div>
            `);
        }

        tableBody.innerHTML = `
            <tr>
                <td colspan="5" style="text-align: center; color: var(--text-muted); padding: 2rem;">
                    <div class="animate-pulse">Loading templates from live database...</div>
                </td>
            </tr>
        `;

        try {
            const tenantParam = this.selectedTenantId ? `&tenantId=${this.selectedTenantId}` : '';
            const queryParam = `?page=${this.page}&pageSize=10&search=${encodeURIComponent(this.search)}${tenantParam}`;
            App.logConsole(`[API Request] GET /api/templates${queryParam}...`);
            const res = await Auth.apiFetch(`/api/templates${queryParam}`);
            if (res.ok) {
                const list = await res.json();
                this.renderTemplatesTable(list);

                const totalHeader = res.headers.get('X-Pagination-Total-Count');
                const total = totalHeader ? parseInt(totalHeader) : list.length;
                const totalPages = Math.ceil(total / 10) || 1;

                const infoEl = document.getElementById('info-templates-page');
                if (infoEl) infoEl.innerText = `Page ${this.page} of ${totalPages}`;

                const prevBtn = document.getElementById('btn-templates-prev');
                if (prevBtn) prevBtn.disabled = this.page <= 1;

                const nextBtn = document.getElementById('btn-templates-next');
                if (nextBtn) nextBtn.disabled = this.page >= totalPages;
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

    handleSearch: function(val) {
        this.search = val;
        this.page = 1;
        this.loadTemplatesList();
    },

    changePage: function(direction) {
        this.page += direction;
        if (this.page < 1) this.page = 1;
        this.loadTemplatesList();
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
            let statusClass = 'badge-amber';
            if (item.status === 'Approved') statusClass = 'badge-emerald';
            else if (item.status === 'Rejected') statusClass = 'badge-rose';
            else if (item.status === 'Simulated') statusClass = 'badge-violet';
            
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
            const langSelect = document.getElementById('templates-language-select');
            const lang = langSelect ? langSelect.value : 'en';

            let queryParam = this.selectedTenantId ? `?tenantId=${this.selectedTenantId}` : '';
            queryParam += queryParam ? `&language=${lang}` : `?language=${lang}`;

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
    },

    syncFromMeta: async function(event) {
        if (event) event.preventDefault();

        const btn = document.getElementById('btn-sync-templates');
        const originalText = btn ? btn.innerText : 'Sync from Meta';
        if (btn) {
            btn.disabled = true;
            btn.innerText = 'Syncing... 🔄';
        }

        try {
            const tenantParam = this.selectedTenantId ? `?tenantId=${this.selectedTenantId}` : '';
            App.logConsole(`[API Request] POST /api/templates/sync${tenantParam}...`);
            
            const res = await Auth.apiFetch(`/api/templates/sync${tenantParam}`, {
                method: 'POST'
            });

            if (res.ok) {
                const data = await res.json();
                App.triggerToast("Sync Success!", data.message);
                
                // Refresh table
                await this.loadTemplatesList();
            } else {
                const errText = await res.text();
                App.triggerToast("Sync Failed", errText || `Server returned error status ${res.status}`);
            }
        } catch (e) {
            App.triggerToast("Sync Error", `Connection failed: ${e.message}`);
        } finally {
            if (btn) {
                btn.disabled = false;
                btn.innerText = originalText;
            }
        }
    }
};

window.Templates = Templates;
