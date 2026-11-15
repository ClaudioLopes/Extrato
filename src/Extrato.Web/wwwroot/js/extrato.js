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

  /**
   * Registra o service worker e avisa quando há versão nova. Sem isso, o navegador só troca de versão
   * depois que todas as abas do app fecham (no celular, o app aberto há dias fica na versão antiga).
   * "Atualizar" manda o worker novo assumir (skipWaiting) e recarrega quando ele passa a controlar a página.
   */
  registrarServiceWorker: async () => {
    if (!("serviceWorker" in navigator)) return;
    let reg;
    try { reg = await navigator.serviceWorker.register("service-worker.js", { updateViaCache: "none" }); }
    catch { return; }

    const avisar = (worker) => {
      // sem controller é a primeira instalação: nada a trocar
      if (!worker || !navigator.serviceWorker.controller || document.getElementById("aviso-versao")) return;
      const aviso = document.createElement("div");
      aviso.id = "aviso-versao";
      aviso.className = "aviso-versao";
      aviso.setAttribute("role", "status");
      aviso.innerHTML = '<span>Nova versão do extrato disponível.</span><button type="button" class="btn mini">Atualizar</button>';
      aviso.querySelector("button").addEventListener("click", (e) => {
        e.target.disabled = true;
        e.target.textContent = "Atualizando…";
        worker.postMessage({ tipo: "atualizar" });
      });
      document.body.appendChild(aviso);
    };

    if (reg.waiting) avisar(reg.waiting);
    reg.addEventListener("updatefound", () => {
      const novo = reg.installing;
      novo?.addEventListener("statechange", () => { if (novo.state === "installed") avisar(novo); });
    });

    let recarregando = false;
    navigator.serviceWorker.addEventListener("controllerchange", () => {
      if (recarregando) return;
      recarregando = true;
      location.reload();
    });

    // procura versão nova ao voltar para o app
    document.addEventListener("visibilitychange", () => {
      if (document.visibilityState === "visible") reg.update().catch(() => { });
    });
  },
};
