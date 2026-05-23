/* =========================================================================
   CHATFLOW WHATSAPP CRM - ROLE DEFINITIONS & ENUMS (roles.js)
   ========================================================================= */

const UserRoles = {
    SuperAdmin: 'SuperAdmin',
    TenantAdmin: 'TenantAdmin',
    Agent: 'Agent'
};

const UserRoleLabels = {
    [UserRoles.SuperAdmin]: 'Super Admin',
    [UserRoles.TenantAdmin]: 'Admin',
    [UserRoles.Agent]: 'Agent'
};

// Prevent runtime modifications
Object.freeze(UserRoles);
Object.freeze(UserRoleLabels);
