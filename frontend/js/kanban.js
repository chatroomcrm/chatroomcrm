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
    }
};
