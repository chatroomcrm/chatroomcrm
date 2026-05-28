/* =========================================================================
   CHATFLOW WHATSAPP CRM - MODULAR AUTHENTICATION SERVICES (auth.js)
   ========================================================================= */

const AUTH_STORAGE_KEYS = {
    TOKEN: 'crm_token',
    USER: 'crm_user',
    API_URL: 'crm_backend_url'
};

const DEFAULT_API_URL = 'https://chatroomcrm-001-site1.ktempurl.com';

// Initialize Auth state
const Auth = {
    getMode() {
        return 'live';
    },
    
    getToken() {
        return localStorage.getItem(AUTH_STORAGE_KEYS.TOKEN);
    },
    
    getUser() {
        try {
            const userStr = localStorage.getItem(AUTH_STORAGE_KEYS.USER);
            return userStr ? JSON.parse(userStr) : null;
        } catch (e) {
            return null;
        }
    },
    
    getApiUrl() {
        const currentOrigin = window.location.origin;
        const isLocalhost = window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1';
        
        let stored = localStorage.getItem(AUTH_STORAGE_KEYS.API_URL);
        
        if (!isLocalhost) {
            // Self-healing: Force cloud URL if we are in the cloud (so localhost caches never bleed in)
            if (!stored || stored.includes('localhost') || stored.includes('127.0.0.1') || (currentOrigin.startsWith('https://') && stored.startsWith('http://'))) {
                localStorage.setItem(AUTH_STORAGE_KEYS.API_URL, currentOrigin);
                return currentOrigin;
            }
            return stored;
        } else {
            // Local dev: Fall back to default local backend SSL port
            if (!stored || !stored.includes('localhost')) {
                const localDefault = 'https://localhost:64723';
                localStorage.setItem(AUTH_STORAGE_KEYS.API_URL, localDefault);
                return localDefault;
            }
            return stored;
        }
    },
    
    isAuthenticated() {
        return !!this.getUser() && !!this.getToken();
    },
    
    setLiveSession(token, user) {
        localStorage.setItem(AUTH_STORAGE_KEYS.TOKEN, token);
        localStorage.setItem(AUTH_STORAGE_KEYS.USER, JSON.stringify(user));
    },
    
    logout() {
        localStorage.removeItem(AUTH_STORAGE_KEYS.TOKEN);
        localStorage.removeItem(AUTH_STORAGE_KEYS.USER);
        
        // Reload page to show login screen clean
        window.location.reload();
    },

    // Perform API call with Bearer authentication
    async apiFetch(endpoint, options = {}) {
        const url = `${this.getApiUrl()}/${endpoint.replace(/^\//, '')}`;
        const headers = {
            'Content-Type': 'application/json',
            ...options.headers
        };
        
        const token = this.getToken();
        if (token) {
            headers['Authorization'] = `Bearer ${token}`;
        }
        
        const response = await fetch(url, {
            ...options,
            headers
        });
        
        if (response.status === 401) {
            // Unauthenticated
            this.logout();
            throw new Error("Your session has expired. Please log in again.");
        }
        
        if (response.status === 403) {
            // Suspended/Blocked account boundary
            const errBody = await response.json().catch(() => ({}));
            const errMsg = errBody.message || "Access denied. Your account is suspended.";
            this.showSuspensionModal(errMsg);
            throw new Error(errMsg);
        }
        
        return response;
    },

    showSuspensionModal(message) {
        // Build beautiful glassmorphic suspension overlay dynamically
        let modal = document.getElementById('suspension-overlay');
        if (!modal) {
            modal = document.createElement('div');
            modal.id = 'suspension-overlay';
            modal.style.position = 'fixed';
            modal.style.top = '0';
            modal.style.left = '0';
            modal.style.width = '100vw';
            modal.style.height = '100vh';
            modal.style.backgroundColor = 'rgba(5, 7, 12, 0.95)';
            modal.style.display = 'flex';
            modal.style.alignItems = 'center';
            modal.style.justifyContent = 'center';
            modal.style.zIndex = '100000';
            modal.style.backdropFilter = 'blur(15px)';
            modal.style.padding = '2rem';
            document.body.appendChild(modal);
        }

        modal.innerHTML = `
            <div class="login-card" style="max-width: 440px; border: 1px solid var(--accent-rose); box-shadow: 0 0 30px rgba(244, 63, 94, 0.2);">
                <div class="login-header">
                    <div class="login-logo" style="background: linear-gradient(135deg, var(--accent-rose), #f43f5e); box-shadow: 0 0 20px rgba(244, 63, 94, 0.4); color: white;">⚠️</div>
                    <h2 style="background: linear-gradient(135deg, #ffffff 60%, var(--accent-rose) 100%); -webkit-background-clip: text; -webkit-text-fill-color: transparent;">Account Suspended</h2>
                    <p style="margin-top: 0.5rem; color: #fda4af; font-size: 0.85rem; line-height: 1.5;">${message}</p>
                </div>
                <button class="login-submit-btn" style="background: linear-gradient(135deg, var(--accent-rose), #e11d48); box-shadow: 0 4px 15px rgba(244, 63, 94, 0.3); color: white;" onclick="Auth.logout()">
                    Return to Login
                </button>
            </div>
        `;
        modal.style.display = 'flex';
    }
};

async function handleLoginSubmit(event) {
    if (event) event.preventDefault();
    
    const email = document.getElementById('login-email').value.trim();
    const password = document.getElementById('login-password').value.trim();
    const errorBox = document.getElementById('login-err-box');
    
    if (!email || !password) {
        showLoginError("Please enter your email and password.");
        return;
    }
    
    errorBox.style.display = 'none';
    
    const submitBtn = document.getElementById('btn-login-submit');
    const originalText = submitBtn.innerHTML;
    submitBtn.innerHTML = `Connecting... <span class="animate-pulse">⚡</span>`;
    submitBtn.disabled = true;
    
    try {
        const res = await fetch(`${Auth.getApiUrl()}/api/auth/login`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ email, password })
        });
        
        if (res.status === 403) {
            const body = await res.json().catch(() => ({}));
            const msg = body.message || "Your account has been suspended.";
            Auth.showSuspensionModal(msg);
            return;
        }
        
        if (!res.ok) {
            const body = await res.json().catch(() => ({}));
            throw new Error(body.message || "Invalid credentials or server connection failed.");
        }
        
        const data = await res.json();
        Auth.setLiveSession(data.token, data.user);
        
        // Remember Login functionality
        const rememberCheckbox = document.getElementById('login-remember');
        if (rememberCheckbox && rememberCheckbox.checked) {
            localStorage.setItem('crm_remembered_email', email);
            localStorage.setItem('crm_remembered_password', password);
        } else {
            localStorage.removeItem('crm_remembered_email');
            localStorage.removeItem('crm_remembered_password');
        }
        
        hideLoginOverlay();
        bootstrapApp(); // Defined in app.js
        
    } catch (err) {
        showLoginError(err.message);
    } finally {
        submitBtn.innerHTML = originalText;
        submitBtn.disabled = false;
    }
}

function showLoginError(msg) {
    const errorBox = document.getElementById('login-err-box');
    errorBox.innerText = msg;
    errorBox.style.display = 'flex';
    
    // Add visual bounce animation to error box
    errorBox.style.animation = 'none';
    errorBox.offsetHeight; /* trigger reflow */
    errorBox.style.animation = null;
}

function hideLoginOverlay() {
    const overlay = document.getElementById('login-overlay');
    if (overlay) {
        overlay.style.opacity = '0';
        setTimeout(() => {
            overlay.style.display = 'none';
        }, 400);
    }
}

function showLoginOverlay() {
    const overlay = document.getElementById('login-overlay');
    if (overlay) {
        overlay.style.display = 'flex';
        overlay.style.opacity = '1';
    }
}

// Prefill saved credentials on DOM Content Loaded
document.addEventListener("DOMContentLoaded", () => {
    const rememberEmail = localStorage.getItem('crm_remembered_email');
    const rememberPassword = localStorage.getItem('crm_remembered_password');
    if (rememberEmail) {
        const emailInput = document.getElementById('login-email');
        if (emailInput) emailInput.value = rememberEmail;
        
        const checkbox = document.getElementById('login-remember');
        if (checkbox) checkbox.checked = true;
    }
    if (rememberPassword) {
        const passwordInput = document.getElementById('login-password');
        if (passwordInput) passwordInput.value = rememberPassword;
    }
});
