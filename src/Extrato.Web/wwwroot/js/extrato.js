// Ponte mínima para o Blazor: armazenamento local (dados para uso offline) e eventos de rede.
window.extrato = {
  online: () => navigator.onLine,

  observarRede: (ref) => {
    window.addEventListener("online", () => ref.invokeMethodAsync("RedeMudou", true));
    window.addEventListener("offline", () => ref.invokeMethodAsync("RedeMudou", false));
  },

  ler: (chave) => {
    try { return localStorage.getItem(chave); } catch { return null; }
  },

  gravar: (chave, valor) => {
    try { localStorage.setItem(chave, valor); return true; } catch { return false; }
  },

  remover: (chave) => {
    try { localStorage.removeItem(chave); } catch { /* armazenamento indisponível */ }
  },

  limparPrefixo: (prefixo) => {
    try {
      Object.keys(localStorage).filter((k) => k.startsWith(prefixo)).forEach((k) => localStorage.removeItem(k));
    } catch { /* armazenamento indisponível */ }
  },

  confirmar: (mensagem) => window.confirm(mensagem),
};
