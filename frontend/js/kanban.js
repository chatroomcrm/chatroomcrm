/* =========================================================================
   CHATFLOW WHATSAPP CRM - MODULAR KANBAN BOARD SERVICE (kanban.js)
   ========================================================================= */

const Kanban = {
    async initialize() {
        await this.loadLeads();
    },

    async loadLeads() {
        try {
            logConsole(`[API Request] GET /api/leads...`);
            const res = await Auth.apiFetch('/api/leads');
            const leads = await res.json();
            
            // Update in-memory db cache
            db.Leads = leads;
            this.renderBoard();
        } catch (err) {
            logConsole(`[API Error] Failed fetching leads for Kanban: ${err.message}`);
        }
    },

    renderBoard() {
        const statuses = ['New', 'Contacted', 'Qualified', 'Proposal', 'Won', 'Lost'];
        const currentUser = Auth.getUser();

        statuses.forEach(status => {
            const container = document.getElementById(`kanban-${status}`);
            const countBadge = document.getElementById(`count-${status}`);
            if (!container) return;

            container.innerHTML = '';
            
            // Filter leads by status and also by Agent constraints!
            let visibleLeads = db.Leads.filter(l => (l.Status || l.status).toLowerCase() === status.toLowerCase());
            
            if (currentUser && currentUser.role === UserRoles.Agent) {
                visibleLeads = visibleLeads.filter(l => {
                    const assignedTo = l.AssignedTo || l.assignedTo;
                    return !assignedTo || assignedTo === currentUser.id;
                });
            }

            if (countBadge) {
                countBadge.innerText = visibleLeads.length;
            }

            visibleLeads.forEach(lead => {
                const leadId = lead.Id || lead.id;
                const contactId = lead.ContactId || lead.contactId;
                const contact = lead.contact || db.Contacts.find(c => c.Id === contactId || c.id === contactId);
                const name = contact ? contact.Name || contact.name : "Unknown Lead";
                const phone = contact ? contact.Phone || contact.phone : "+0 000 000";

                const otherStatuses = statuses.filter(s => s.toLowerCase() !== status.toLowerCase());

                container.innerHTML += `
                    <div class="kanban-card" draggable="true" ondragstart="Kanban.drag(event, '${leadId}')" onclick="Kanban.openChat('${leadId}')">
                        <h5>${name}</h5>
                        <p>${phone}</p>
                        
                        <div class="card-footer">
                            <span class="time">Active</span>
                            <div class="actions">
                                ${otherStatuses.map(s => `
                                    <button class="action-dot" onclick="event.stopPropagation(); Kanban.changeLeadStatus('${leadId}', '${s}')" title="Move to ${s}">
                                        ${s.charAt(0)}
                                    </button>
                                `).join('')}
                            </div>
                        </div>
                    </div>
                `;
            });
        });
    },

    drag(ev, leadId) {
        ev.dataTransfer.setData("text", leadId);
    },

    allowDrop(ev) {
        ev.preventDefault();
    },

    handleDrop(ev, newStatus) {
        ev.preventDefault();
        const leadId = ev.dataTransfer.getData("text");
        if (leadId) {
            this.changeLeadStatus(leadId, newStatus);
        }
    },

    async changeLeadStatus(leadId, newStatus) {
        const lead = db.Leads.find(l => l.Id === leadId || l.id === leadId);
        if (!lead) return;

        const oldStatus = lead.Status || lead.status;

        // Enforce Agent boundaries: Agent cannot edit status of a lead assigned to another agent
        const currentUser = Auth.getUser();
        if (currentUser && currentUser.role === UserRoles.Agent) {
            const assignedTo = lead.AssignedTo || lead.assignedTo;
            if (assignedTo && assignedTo !== currentUser.id) {
                triggerToast("Access Denied", "You cannot edit status of a lead assigned to another agent.");
                return;
            }
        }

        logConsole(`[Kanban] Moving Lead ${leadId.substring(0, 6)}... from ${oldStatus} to ${newStatus}`);

        try {
            logConsole(`[API Request] PUT /api/leads/${leadId}/status...`);
            const response = await Auth.apiFetch(`/api/leads/${leadId}/status`, {
                method: 'PUT',
                body: JSON.stringify({ status: newStatus })
            });

            if (!response.ok) {
                throw new Error("API server rejected the status update.");
            }

            // Update cache locally
            lead.Status = newStatus;
            lead.status = newStatus;

            logConsole(`[API Success] Status updated to ${newStatus} on the backend.`);
            
        } catch (err) {
            logConsole(`[API Error] Failed updating status on server: ${err.message}`);
            triggerToast("Update Failed", err.message);
            return;
        }

        this.renderBoard();
        
        // Update other views
        if (typeof Chat !== 'undefined' && activeLeadId === leadId) {
            Chat.renderChatLogs();
            Chat.renderThreads();
        } else if (typeof Chat !== 'undefined') {
            Chat.renderThreads();
        }

        if (typeof App !== 'undefined') {
            App.updateDashboardStats();
        }
        if (typeof DbInspector !== 'undefined') {
            DbInspector.renderTable();
        }
    },

    openChat(leadId) {
        if (typeof Chat !== 'undefined') {
            Chat.selectThread(leadId);
            // Switch view pane to Real-Time Chats using active navigation
            const chatNavBtn = document.querySelector('.saas-nav button[onclick*="chats"]');
            if (chatNavBtn && typeof App !== 'undefined') {
                App.switchView('chats', chatNavBtn);
            }
        }
    },

    downloadReport(format) {
        if (!db.Leads || db.Leads.length === 0) {
            triggerToast("No Data", "There are no leads in the pipeline to report.");
            return;
        }

        const currentUser = Auth.getUser();
        let visibleLeads = [...db.Leads];
        
        // Respect Agent boundaries in report too!
        if (currentUser && currentUser.role === UserRoles.Agent) {
            visibleLeads = visibleLeads.filter(l => {
                const assignedTo = l.AssignedTo || l.assignedTo;
                return !assignedTo || assignedTo === currentUser.id;
            });
        }

        if (format === 'csv') {
            this.downloadCSV(visibleLeads);
        } else if (format === 'pdf') {
            this.downloadPDF(visibleLeads);
        }
    },

    downloadCSV(leads) {
        let csv = 'Lead ID,Customer Name,Email Address,Phone Number,Created Date/Time (Local),Pipeline Stage,Assigned Agent ID\r\n';
        
        leads.forEach(lead => {
            const leadId = lead.Id || lead.id;
            const contactId = lead.ContactId || lead.contactId;
            const contact = lead.contact || db.Contacts.find(c => c.Id === contactId || c.id === contactId);
            const name = contact ? contact.Name || contact.name : "Unknown Lead";
            const email = contact ? contact.Email || contact.email || "No Email" : "No Email";
            const phone = contact ? contact.Phone || contact.phone : "+0 000 000";
            const timestamp = lead.Timestamp || lead.timestamp;
            const localTime = timestamp ? new Date(timestamp).toLocaleString() : "N/A";
            const status = lead.Status || lead.status || "New";
            const assignedTo = lead.AssignedTo || lead.assignedTo || "Unassigned";

            // Clean values of commas or quotes
            const cleanName = name.replace(/"/g, '""');
            const cleanEmail = email.replace(/"/g, '""');
            const cleanPhone = phone.replace(/"/g, '""');
            const cleanStatus = status.replace(/"/g, '""');

            csv += `"${leadId}","${cleanName}","${cleanEmail}","${cleanPhone}","${localTime}","${cleanStatus}","${assignedTo}"\r\n`;
        });

        const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
        const link = document.createElement("a");
        const url = URL.createObjectURL(blob);
        link.setAttribute("href", url);
        link.setAttribute("download", `ChatRoom_Leads_Report_${new Date().toISOString().slice(0,10)}.csv`);
        link.style.visibility = 'hidden';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        triggerToast("Report Generated", "CSV leads report has been downloaded successfully!");
    },

    downloadPDF(leads) {
        const printWindow = window.open('', '_blank');
        if (!printWindow) {
            triggerToast("Popup Blocked", "Please enable popups to download PDF reports.");
            return;
        }

        // Calculate pipeline metrics
        const total = leads.length;
        const won = leads.filter(l => (l.Status || l.status || '').toLowerCase() === 'won').length;
        const lost = leads.filter(l => (l.Status || l.status || '').toLowerCase() === 'lost').length;
        const contacted = leads.filter(l => {
            const s = (l.Status || l.status || '').toLowerCase();
            return s !== 'new';
        }).length;
        const active = total - won - lost;

        const tableRows = leads.map((lead, index) => {
            const contactId = lead.ContactId || lead.contactId;
            const contact = lead.contact || db.Contacts.find(c => c.Id === contactId || c.id === contactId);
            const name = contact ? contact.Name || contact.name : "Unknown Lead";
            const email = contact ? contact.Email || contact.email || "—" : "—";
            const phone = contact ? contact.Phone || contact.phone : "+0 000 000";
            const timestamp = lead.Timestamp || lead.timestamp;
            const localTime = timestamp ? new Date(timestamp).toLocaleString() : "N/A";
            const status = lead.Status || lead.status || "New";
            return `
                <tr>
                    <td>${index + 1}</td>
                    <td><strong>${name}</strong></td>
                    <td>${email}</td>
                    <td>${phone}</td>
                    <td>${localTime}</td>
                    <td><span class="badge ${status.toLowerCase()}">${status}</span></td>
                </tr>
            `;
        }).join('');

        printWindow.document.write(`
            <html>
            <head>
                <title>ChatRoom CRM - Leads Pipeline Report</title>
                <style>
                    body {
                        font-family: 'Inter', 'Segoe UI', sans-serif;
                        color: #1e293b;
                        margin: 40px;
                        line-height: 1.5;
                    }
                    .header {
                        display: flex;
                        justify-content: space-between;
                        align-items: center;
                        border-bottom: 2px solid #e2e8f0;
                        padding-bottom: 20px;
                        margin-bottom: 30px;
                    }
                    .title h1 {
                        margin: 0;
                        font-size: 24px;
                        color: #0f172a;
                    }
                    .title p {
                        margin: 5px 0 0 0;
                        color: #64748b;
                        font-size: 14px;
                    }
                    .meta {
                        text-align: right;
                        font-size: 12px;
                        color: #64748b;
                    }
                    .stats-grid {
                        display: grid;
                        grid-template-columns: repeat(4, 1fr);
                        gap: 15px;
                        margin-bottom: 30px;
                    }
                    .stat-card {
                        background: #f8fafc;
                        border: 1px solid #e2e8f0;
                        padding: 15px;
                        border-radius: 8px;
                        text-align: center;
                    }
                    .stat-card .label {
                        font-size: 11px;
                        text-transform: uppercase;
                        color: #64748b;
                        font-weight: 600;
                    }
                    .stat-card .val {
                        font-size: 20px;
                        font-weight: 700;
                        color: #0f172a;
                        margin-top: 5px;
                    }
                    table {
                        width: 100%;
                        border-collapse: collapse;
                        margin-bottom: 30px;
                    }
                    th, td {
                        padding: 12px 15px;
                        text-align: left;
                        border-bottom: 1px solid #e2e8f0;
                    }
                    th {
                        background-color: #f1f5f9;
                        color: #475569;
                        font-weight: 600;
                        font-size: 12px;
                        text-transform: uppercase;
                    }
                    td {
                        font-size: 12px;
                    }
                    .badge {
                        display: inline-block;
                        padding: 3px 8px;
                        font-size: 10px;
                        font-weight: 600;
                        border-radius: 12px;
                        text-transform: uppercase;
                    }
                    .badge.new { background: #dbeafe; color: #1e40af; }
                    .badge.contacted { background: #fef3c7; color: #92400e; }
                    .badge.qualified { background: #e0f2fe; color: #0369a1; }
                    .badge.proposal { background: #f3e8ff; color: #6b21a8; }
                    .badge.won { background: #dcfce7; color: #166534; }
                    .badge.lost { background: #ffe4e6; color: #9f1239; }
                    .footer {
                        text-align: center;
                        font-size: 11px;
                        color: #94a3b8;
                        border-top: 1px solid #e2e8f0;
                        padding-top: 20px;
                        margin-top: 50px;
                    }
                </style>
            </head>
            <body>
                <div class="header">
                    <div class="title">
                        <h1>Leads Pipeline Report</h1>
                        <p>ChatRoom CRM Sales Performance & Conversion Pipeline</p>
                    </div>
                    <div class="meta">
                        <strong>Generated:</strong> ${new Date().toLocaleString()}<br>
                        <strong>Timezone:</strong> ${Intl.DateTimeFormat().resolvedOptions().timeZone || 'Local'}
                    </div>
                </div>

                <div class="stats-grid">
                    <div class="stat-card">
                        <div class="label">Total Leads</div>
                        <div class="val">${total}</div>
                    </div>
                    <div class="stat-card">
                        <div class="label">Customers Contacted</div>
                        <div class="val" style="color: #0369a1;">${contacted}</div>
                    </div>
                    <div class="stat-card">
                        <div class="label">Deals Won</div>
                        <div class="val" style="color: #166534;">${won}</div>
                    </div>
                    <div class="stat-card">
                        <div class="label">Deals Lost</div>
                        <div class="val" style="color: #9f1239;">${lost}</div>
                    </div>
                </div>

                <table>
                    <thead>
                        <tr>
                            <th style="width: 30px;">#</th>
                            <th>Customer Name</th>
                            <th>Email Address</th>
                            <th>Phone Number</th>
                            <th>Created Date/Time</th>
                            <th>Status</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${tableRows}
                    </tbody>
                </table>

                <div class="footer">
                    This report was automatically generated by ChatRoom CRM. Confidential internal sales document. All database timestamps are processed and resolved to standard local time zone.
                </div>
                
                <script>
                    window.onload = function() {
                        window.print();
                        window.onafterprint = function() {
                            window.close();
                        };
                    };
                </script>
            </body>
            </html>
        `);
        printWindow.document.close();
    }
};
