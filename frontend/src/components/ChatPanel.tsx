import React, { useState, useEffect, useRef } from 'react';
import { Send, Phone, User, Shield, MessageSquare, Clock } from 'lucide-react';

interface Message {
  id: string;
  content: string;
  direction: 'Incoming' | 'Outgoing';
  timestamp: string;
  providerMessageId?: string;
}

interface Lead {
  id: string;
  status: string;
  contact: {
    id: string;
    name: string;
    phone: string;
  };
}

interface ChatPanelProps {
  activeLead: Lead | null;
  messages: Message[];
  isTyping: boolean;
  onSendMessage: (content: string) => void;
}

export const ChatPanel: React.FC<ChatPanelProps> = ({
  activeLead,
  messages,
  isTyping,
  onSendMessage
}) => {
  const [inputText, setInputText] = useState('');
  const messagesEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, isTyping]);

  const handleSend = (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputText.trim()) return;
    onSendMessage(inputText);
    setInputText('');
  };

  if (!activeLead) {
    return (
      <div className="flex flex-col items-center justify-center h-full text-slate-400 p-8 glass-panel border border-white/5 rounded-2xl">
        <MessageSquare className="w-16 h-16 text-slate-600 mb-4 animate-bounce" />
        <p className="text-lg font-medium text-slate-200">No Chat Selected</p>
        <p className="text-sm text-slate-500 text-center max-w-xs mt-1">
          Select an active lead from the list or drag a card on the Kanban board to review conversation logs.
        </p>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full glass-panel border border-white/5 rounded-2xl overflow-hidden shadow-2xl">
      {/* Active Conversation Header */}
      <div className="p-4 bg-slate-900/60 border-b border-white/5 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-full bg-gradient-to-tr from-cyan-400 to-indigo-500 flex items-center justify-center font-bold text-white shadow-md">
            {activeLead.contact.name.charAt(0).toUpperCase()}
          </div>
          <div>
            <h4 className="font-semibold text-slate-100">{activeLead.contact.name}</h4>
            <div className="flex items-center gap-1.5 text-xs text-emerald-400 mt-0.5">
              <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse"></span>
              {activeLead.contact.phone}
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <span className="px-3 py-1 rounded-full text-xs font-semibold bg-cyan-500/10 text-cyan-400 border border-cyan-500/20">
            Stage: {activeLead.status}
          </span>
        </div>
      </div>

      {/* Messages Log */}
      <div className="flex-1 overflow-y-auto p-4 space-y-3 scrollbar-thin scrollbar-thumb-slate-800">
        {messages.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-full text-slate-500 text-sm py-12">
            <Clock className="w-8 h-8 mb-2 stroke-1 text-slate-600" />
            <p>No messages in this conversation thread yet.</p>
          </div>
        ) : (
          messages.map((msg) => {
            const isIncoming = msg.direction === 'Incoming';
            return (
              <div
                key={msg.id}
                className={`flex w-full ${isIncoming ? 'justify-start' : 'justify-end'}`}
              >
                <div
                  className={`max-w-[70%] p-3.5 rounded-2xl shadow-lg relative ${
                    isIncoming
                      ? 'bg-slate-800/80 text-slate-100 rounded-tl-none border border-white/5'
                      : 'bg-gradient-to-r from-cyan-500 to-blue-600 text-white rounded-tr-none'
                  }`}
                >
                  <p className="text-sm whitespace-pre-wrap leading-relaxed">{msg.content}</p>
                  <div
                    className={`text-[10px] mt-1.5 flex items-center gap-1 justify-end ${
                      isIncoming ? 'text-slate-400' : 'text-cyan-100'
                    }`}
                  >
                    {new Date(msg.timestamp).toLocaleTimeString([], {
                      hour: '2-digit',
                      minute: '2-digit'
                    })}
                  </div>
                </div>
              </div>
            );
          })
        )}

        {isTyping && (
          <div className="flex justify-start items-center gap-2 text-slate-400 text-xs mt-2 pl-2">
            <span className="w-1.5 h-1.5 bg-slate-400 rounded-full animate-bounce"></span>
            <span className="w-1.5 h-1.5 bg-slate-400 rounded-full animate-bounce [animation-delay:0.2s]"></span>
            <span className="w-1.5 h-1.5 bg-slate-400 rounded-full animate-bounce [animation-delay:0.4s]"></span>
            <span className="italic ml-1">Customer is typing...</span>
          </div>
        )}
        <div ref={messagesEndRef} />
      </div>

      {/* Input Form Footer */}
      <form onSubmit={handleSend} className="p-3 bg-slate-900/40 border-t border-white/5 flex gap-2">
        <input
          type="text"
          value={inputText}
          onChange={(e) => setInputText(e.target.value)}
          placeholder={`Reply to ${activeLead.contact.name} via WhatsApp...`}
          className="flex-1 bg-slate-950/80 text-slate-100 rounded-xl px-4 py-3 text-sm focus:outline-none focus:ring-1 focus:ring-cyan-500/50 border border-white/5 placeholder-slate-500 transition-all"
        />
        <button
          type="submit"
          className="bg-gradient-to-r from-cyan-400 to-indigo-500 hover:from-cyan-500 hover:to-indigo-600 text-white p-3 rounded-xl shadow-lg hover:shadow-cyan-500/20 active:scale-95 transition-all flex items-center justify-center"
        >
          <Send className="w-4 h-4" />
        </button>
      </form>
    </div>
  );
};
