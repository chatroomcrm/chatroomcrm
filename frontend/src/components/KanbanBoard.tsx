import React from 'react';
import { User, MessageCircle, ArrowRight, TrendingUp } from 'lucide-react';

interface Lead {
  id: string;
  status: string;
  contact: {
    id: string;
    name: string;
    phone: string;
  };
}

interface KanbanBoardProps {
  leads: Lead[];
  onSelectLead: (lead: Lead) => void;
  onUpdateLeadStatus: (leadId: string, newStatus: string) => void;
  activeLeadId?: string;
}

const COLUMNS = [
  { id: 'New', title: 'New Leads', color: 'border-t-cyan-400' },
  { id: 'Contacted', title: 'Contacted', color: 'border-t-indigo-400' },
  { id: 'Qualified', title: 'Qualified', color: 'border-t-purple-400' },
  { id: 'Proposal', title: 'Proposal', color: 'border-t-yellow-400' },
  { id: 'Won', title: 'Won (Closed)', color: 'border-t-emerald-400' },
  { id: 'Lost', title: 'Lost', color: 'border-t-rose-400' }
];

export const KanbanBoard: React.FC<KanbanBoardProps> = ({
  leads,
  onSelectLead,
  onUpdateLeadStatus,
  activeLeadId
}) => {
  const getLeadsByStatus = (status: string) => {
    return leads.filter((lead) => lead.status.toLowerCase() === status.toLowerCase());
  };

  const handleDragStart = (e: React.DragEvent, leadId: string) => {
    e.dataTransfer.setData('text/plain', leadId);
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
  };

  const handleDrop = (e: React.DragEvent, columnId: string) => {
    e.preventDefault();
    const leadId = e.dataTransfer.getData('text/plain');
    if (leadId) {
      onUpdateLeadStatus(leadId, columnId);
    }
  };

  return (
    <div className="grid grid-cols-1 md:grid-cols-3 lg:grid-cols-6 gap-4 h-full overflow-x-auto pb-4 scrollbar-thin">
      {COLUMNS.map((col) => {
        const columnLeads = getLeadsByStatus(col.id);

        return (
          <div
            key={col.id}
            onDragOver={handleDragOver}
            onDrop={(e) => handleDrop(e, col.id)}
            className={`flex flex-col bg-slate-900/40 border border-white/5 border-t-2 ${col.color} rounded-xl p-3 min-w-[200px] h-[600px] shadow-lg`}
          >
            {/* Column Header */}
            <div className="flex items-center justify-between mb-3 px-1">
              <h5 className="font-semibold text-sm text-slate-200">{col.title}</h5>
              <span className="text-xs bg-slate-800 text-slate-400 px-2 py-0.5 rounded-full font-bold">
                {columnLeads.length}
              </span>
            </div>

            {/* Lead Cards Grid */}
            <div className="flex-1 overflow-y-auto space-y-2.5 scrollbar-none pr-0.5">
              {columnLeads.length === 0 ? (
                <div className="border border-dashed border-white/5 rounded-xl h-24 flex items-center justify-center text-xs text-slate-600 text-center p-4">
                  Drag leads here
                </div>
              ) : (
                columnLeads.map((lead) => (
                  <div
                    key={lead.id}
                    draggable
                    onDragStart={(e) => handleDragStart(e, lead.id)}
                    onClick={() => onSelectLead(lead)}
                    className={`p-3 bg-slate-950/80 hover:bg-slate-900/90 border border-white/5 rounded-xl cursor-grab active:cursor-grabbing hover:border-white/10 hover:shadow-lg transition-all ${
                      activeLeadId === lead.id ? 'ring-1 ring-cyan-500/50 border-cyan-500/30' : ''
                    }`}
                  >
                    <div className="flex items-start justify-between gap-1">
                      <p className="font-semibold text-sm text-slate-200 truncate">
                        {lead.contact.name}
                      </p>
                      <MessageCircle className="w-3.5 h-3.5 text-cyan-400 shrink-0" />
                    </div>
                    <p className="text-xs text-slate-500 font-mono mt-1 truncate">
                      {lead.contact.phone}
                    </p>

                    <div className="flex items-center justify-between mt-3 pt-2 border-t border-white/5">
                      <div className="flex gap-1">
                        {COLUMNS.map((moveCol) => {
                          if (moveCol.id === col.id) return null;
                          return (
                            <button
                              key={moveCol.id}
                              title={`Move to ${moveCol.title}`}
                              onClick={(e) => {
                                e.stopPropagation();
                                onUpdateLeadStatus(lead.id, moveCol.id);
                              }}
                              className="w-4 h-4 rounded bg-slate-800 hover:bg-slate-700 text-[8px] font-bold text-slate-300 flex items-center justify-center hover:text-white transition-colors"
                            >
                              {moveCol.id.charAt(0)}
                            </button>
                          );
                        })}
                      </div>
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
};
