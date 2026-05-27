/* =========================================================================
   CHATFLOW WHATSAPP CRM - MODULAR CHAT SERVICE (chat.js)
   ========================================================================= */

const Chat = {
    connection: null,

    async initialize() {
        await this.connectSignalR();
        await this.loadLiveThreads();
    },

    // ----------------------------------------------------
    // SIGNALR SOCKET HUB METHODS (LIVE MODE)
    // ----------------------------------------------------
    async connectSignalR() {
        const token = Auth.getToken();
        const hubUrl = `${Auth.getApiUrl()}/chathub?access_token=${token}`;
        
        logConsole(`[SignalR Hub] Connecting to hub at ${hubUrl}...`);

        try {
            if (typeof signalR === 'undefined') {
                throw new Error("SignalR client SDK not loaded.");
            }

            this.connection = new signalR.HubConnectionBuilder()
                .withUrl(hubUrl, {
                    skipNegotiation: true,
                    transport: signalR.HttpTransportType.WebSockets
                })
                .withAutomaticReconnect()
                .build();

            this.connection.on("ReceiveMessage", (message) => {
                logConsole(`[SignalR Server] Event 'ReceiveMessage' received: MsgId=${message.messageId || message.id}`);
                this.handleIncomingSocketMessage(message);
            });

            this.connection.on("ReceiveLeadStatusUpdate", (update) => {
                logConsole(`[SignalR Server] Event 'ReceiveLeadStatusUpdate' received: LeadId=${update.leadId}, Status=${update.newStatus}`);
                
                // Find and update lead status locally
                const lead = db.Leads.find(l => l.Id === update.leadId || l.id === update.leadId);
                if (lead) {
                    lead.Status = update.newStatus;
                    lead.status = update.newStatus;
                }
                
                // Trigger updates across panes
                if (typeof Kanban !== 'undefined' && Kanban.renderBoard) {
                    Kanban.renderBoard();
                }
                if (typeof App !== 'undefined') {
                    App.updateDashboardStats();
                }
                if (typeof DbInspector !== 'undefined') {
                    DbInspector.renderTable();
                }
            });

            this.connection.on("ReceiveNewLead", (newLead) => {
                logConsole(`[SignalR Server] Event 'ReceiveNewLead' received: LeadId=${newLead.leadId}`);
                
                // Add to internal caches if not present
                const exists = db.Leads.find(l => l.Id === newLead.leadId || l.id === newLead.leadId);
                if (!exists) {
                    db.Leads.push({
                        Id: newLead.leadId,
                        Status: newLead.status,
                        AssignedTo: null,
                        ContactId: newLead.leadId // simple mapping
                    });
                    db.Contacts.push({
                        Id: newLead.leadId,
                        Name: newLead.contactName,
                        Phone: newLead.phone
                    });
                }
                
                // Re-render
                this.renderThreads();
                if (typeof Kanban !== 'undefined') {
                    Kanban.renderBoard();
                }
                if (typeof App !== 'undefined') {
                    App.updateDashboardStats();
                }
                if (typeof DbInspector !== 'undefined') {
                    DbInspector.renderTable();
                }
            });

            await this.connection.start();
            logConsole(`[SignalR Hub] Socket connection established successfully!`);
            
            // Join the Tenant Group to receive real-time status and message updates!
            const currentUser = Auth.getUser();
            if (currentUser && currentUser.tenantId) {
                logConsole(`[SignalR Hub] Joining Tenant Group: Tenant_${currentUser.tenantId}`);
                await this.connection.invoke("JoinTenantGroup", currentUser.tenantId);
            }
        } catch (err) {
            logConsole(`[SignalR Hub] Connection failed: ${err.message}`);
        }
    },

    // ----------------------------------------------------
    // THREAD LOADING METHODS (LIVE ONLY)
    // ----------------------------------------------------
    async loadLiveThreads() {
        try {
            logConsole(`[API Request] GET /api/leads?pageSize=1000...`);
            const res = await Auth.apiFetch('/api/leads?pageSize=1000');
            const leads = await res.json();
            
            // Map lead data into sandbox-compatible format for unified template rendering
            db.Leads = leads.map(l => ({
                Id: l.id || l.Id,
                Status: l.status || l.Status,
                AssignedTo: l.assignedTo || l.AssignedTo,
                ContactId: l.contact ? (l.contact.id || l.contact.Id) : null
            }));
            
            db.Contacts = leads.filter(l => l.contact).map(l => ({
                Id: l.contact.id || l.contact.Id,
                Name: l.contact.name || l.contact.Name,
                Phone: l.contact.phone || l.contact.Phone
            }));

            // Initialize active lead if there is any lead and none is set
            if (db.Leads.length > 0 && !activeLeadId) {
                activeLeadId = db.Leads[0].Id;
            }

            if (activeLeadId) {
                // Fetch initial messages for active lead
                try {
                    logConsole(`[API Request] GET /api/messages/${activeLeadId}...`);
                    const msgsRes = await Auth.apiFetch(`/api/messages/${activeLeadId}`);
                    const history = await msgsRes.json();
                    db.Messages = history.map(m => ({
                        Id: m.id || m.Id,
                        LeadId: activeLeadId,
                        Content: m.content || m.Content,
                        Direction: m.direction || m.Direction,
                        Timestamp: m.timestamp || m.Timestamp
                    }));
                } catch (e) {
                    db.Messages = [];
                }
            } else {
                db.Messages = [];
            }

            this.renderThreads();
            this.renderChatLogs();
        } catch (err) {
            logConsole(`[API Error] Failed loading live threads: ${err.message}`);
        }
    },

    // ----------------------------------------------------
    // INBOUND LOGIC (SIGNALR WEBSOCKET CHANNELS)
    // ----------------------------------------------------
    async handleIncomingSocketMessage(msg) {
        const msgId = msg.messageId || msg.id;
        const leadId = msg.leadId;
        
        // Cache the incoming message into database state
        const exists = db.Messages.find(m => m.Id === msgId || m.id === msgId);
        if (exists) return;

        db.Messages.push({
            Id: msgId,
            LeadId: leadId,
            Content: msg.content,
            Direction: msg.direction,
            Timestamp: msg.timestamp
        });

        // Trigger visual alerts
        const lead = db.Leads.find(l => l.Id === leadId || l.id === leadId);
        if (lead) {
            const contactId = lead.ContactId || lead.contactId;
            const contact = db.Contacts.find(c => c.Id === contactId || c.id === contactId);
            const name = contact ? contact.Name || contact.name : "Incoming WhatsApp Lead";
            triggerToast("New WhatsApp Message", `${name}: "${msg.content}"`);
        }

        // Re-render chat panels
        this.renderThreads();
        if (activeLeadId === leadId) {
            this.renderChatLogs();
        }
        
        if (typeof App !== 'undefined') {
            App.updateDashboardStats();
        }
        if (typeof DbInspector !== 'undefined') {
            DbInspector.renderTable();
        }
    },

    // ----------------------------------------------------
    // SEND OUTBOUND REPLY
    // ----------------------------------------------------
    async sendOutboundReply() {
        const input = document.getElementById('crm-input-reply');
        const text = input.value.trim();
        if (!text) return;

        const activeLead = db.Leads.find(l => l.Id === activeLeadId || l.id === activeLeadId);
        if (!activeLead) return;

        input.value = '';

        try {
            logConsole(`[API Request] POST /api/messages/send...`);
            const response = await Auth.apiFetch('/api/messages/send', {
                method: 'POST',
                body: JSON.stringify({
                    leadId: activeLead.Id || activeLead.id,
                    content: text
                })
            });

            if (!response.ok) {
                throw new Error("Failed delivering WhatsApp response via API gateway.");
            }

            const msg = await response.json();
            const msgId = msg.messageId || msg.id;
            
            // Add to internal caches
            db.Messages.push({
                Id: msgId,
                LeadId: activeLeadId,
                Content: text,
                Direction: 'Outgoing',
                Timestamp: new Date().toISOString()
            });

            logConsole(`[Twilio REST API] Delivery state: SENT to Twilio Provider successfully.`);
            
        } catch (err) {
            logConsole(`[API Error] Failed sending outbound message: ${err.message}`);
            triggerToast("Delivery Failure", err.message);
        }

        this.renderChatLogs();
        this.renderThreads();
        
        if (typeof App !== 'undefined') {
            App.updateDashboardStats();
        }
        if (typeof DbInspector !== 'undefined') {
            DbInspector.renderTable();
        }
    },

    // ----------------------------------------------------
    // RENDERING LOGIC
    // ----------------------------------------------------
    renderThreads() {
        const container = document.getElementById('crm-threads-container');
        if (!container) return;
        container.innerHTML = '';

        const currentUser = Auth.getUser();

        // Filter and display chats based on Role constraints!
        let visibleLeads = [...db.Leads];
        
        if (currentUser && currentUser.role === UserRoles.Agent) {
            visibleLeads = visibleLeads.filter(l => {
                const assignedTo = l.AssignedTo || l.assignedTo;
                return !assignedTo || assignedTo === currentUser.id;
            });
        }

        if (visibleLeads.length === 0) {
            container.innerHTML = `<div style="padding: 1.5rem; text-align: center; color: var(--text-muted); font-size: 0.75rem;">No active chats found.</div>`;
            return;
        }

        visibleLeads.forEach(lead => {
            const lId = lead.Id || lead.id;
            const contactId = lead.ContactId || lead.contactId;
            const contact = db.Contacts.find(c => c.Id === contactId || c.id === contactId);
            const name = contact ? contact.Name || contact.name : "Unknown Lead";
            const phone = contact ? contact.Phone || contact.phone : "+0 000 000";

            const leadMsgs = db.Messages.filter(m => m.LeadId === lId || m.leadId === lId);
            const sortedMsgs = leadMsgs.sort((a,b) => new Date(b.Timestamp || b.timestamp) - new Date(a.Timestamp || a.timestamp));
            const lastMsg = sortedMsgs[0];
            const previewText = lastMsg ? lastMsg.Content || lastMsg.content : 'No chat history';
            const timestamp = lastMsg ? new Date(lastMsg.Timestamp || lastMsg.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';

            const isActive = lId === activeLeadId ? 'active' : '';

            container.innerHTML += `
                <div class="chat-thread-item ${isActive}" onclick="Chat.selectThread('${lId}')">
                    <div class="thread-name">
                        <span>${name}</span>
                        <span style="font-size:0.6rem; color:var(--text-muted);">${timestamp}</span>
                    </div>
                    <div class="thread-phone">${phone}</div>
                    <div class="thread-preview">${previewText}</div>
                </div>
            `;
        });
    },

    renderChatLogs() {
        const container = document.getElementById('crm-chat-logs-container');
        if (!container) return;
        container.innerHTML = '';

        const activeLead = db.Leads.find(l => l.Id === activeLeadId || l.id === activeLeadId);
        if (!activeLead) {
            container.innerHTML = `<div style="padding:2rem; text-align:center; color:var(--text-muted);">Select a chat thread from the left menu to start typing response.</div>`;
            return;
        }

        const lId = activeLead.Id || activeLead.id;
        const contactId = activeLead.ContactId || activeLead.contactId;
        const contact = db.Contacts.find(c => c.Id === contactId || c.id === contactId);
        
        const name = contact ? contact.Name || contact.name : "Active Lead";
        const phone = contact ? contact.Phone || contact.phone : "+0 000 000";
        const status = activeLead.Status || activeLead.status || 'New';

        // Populate header
        document.getElementById('crm-chat-active-name').innerText = name;
        document.getElementById('crm-chat-active-phone').innerText = phone;
        
        const statusBadge = document.getElementById('crm-chat-active-status');
        statusBadge.innerText = status;
        
        // Remove existing class tags and append appropriate coloring
        statusBadge.className = '';
        statusBadge.style.padding = '0.25rem 0.75rem';
        statusBadge.style.borderRadius = '20px';
        statusBadge.style.fontSize = '0.7rem';
        statusBadge.style.fontWeight = '700';
        statusBadge.style.textTransform = 'uppercase';
        
        if (status.toLowerCase() === 'won') {
            statusBadge.style.background = 'rgba(16, 185, 129, 0.12)';
            statusBadge.style.border = '1px solid rgba(16, 185, 129, 0.2)';
            statusBadge.style.color = 'var(--accent-emerald)';
        } else if (status.toLowerCase() === 'lost') {
            statusBadge.style.background = 'rgba(244, 63, 94, 0.12)';
            statusBadge.style.border = '1px solid rgba(244, 63, 94, 0.2)';
            statusBadge.style.color = 'var(--accent-rose)';
        } else {
            statusBadge.style.background = 'rgba(0, 242, 254, 0.1)';
            statusBadge.style.border = '1px solid rgba(0, 242, 254, 0.2)';
            statusBadge.style.color = 'var(--theme-glow)';
        }

        const messages = db.Messages.filter(m => m.LeadId === lId || m.leadId === lId)
                                    .sort((a,b) => new Date(a.Timestamp || a.timestamp) - new Date(b.Timestamp || b.timestamp));

        // Render CRM Message logs
        messages.forEach(msg => {
            const isIncoming = (msg.Direction || msg.direction || 'incoming').toLowerCase() === 'incoming';
            const dirClass = isIncoming ? 'incoming' : 'outgoing';
            const text = msg.Content || msg.content;
            const time = new Date(msg.Timestamp || msg.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

            container.innerHTML += `
                <div class="crm-msg-bubble ${dirClass}">
                    <p>${text}</p>
                    <div class="time">${time}</div>
                </div>
            `;
        });

        container.scrollTop = container.scrollHeight;
    },

    async selectThread(leadId) {
        activeLeadId = leadId;
        
        try {
            logConsole(`[API Request] GET /api/messages/${leadId}...`);
            const res = await Auth.apiFetch(`/api/messages/${leadId}`);
            const history = await res.json();
            
            db.Messages = db.Messages.filter(m => m.LeadId !== leadId && m.leadId !== leadId);
            history.forEach(m => {
                db.Messages.push({
                    Id: m.id || m.Id,
                    LeadId: leadId,
                    Content: m.content || m.Content,
                    Direction: m.direction || m.Direction,
                    Timestamp: m.timestamp || m.Timestamp
                });
            });
        } catch (err) {
            logConsole(`[API Error] Failed fetching message history: ${err.message}`);
        }
        
        this.renderThreads();
        this.renderChatLogs();
    }
};
