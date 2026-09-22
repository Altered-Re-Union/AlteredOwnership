// Single source of truth for translatable strings. English is canonical:
// any new key must exist in `en`; missing keys in other languages fall back to English.
window.AO_I18N = {
  en: {
    "nav.back": "Back to altered.re",
    "nav.backHome": "Back",
    "auth.login": "Login",
    "auth.editProfile": "Edit my profile",
    "auth.admin": "Admin",
    "auth.logout": "Logout",
    "main.title": "Collection importer",
    "section.export.title": "How do I export my current collection?",
    "section.export.body":
      '<p>Under GDPR, you can request access to your personal data from Equinox. You need to make the request by emailing <a href="mailto:support@altered.gg">support@altered.gg</a>, but with a significantly longer processing time.</li>',
    "section.whatImported.title": "What will be imported?",
    "section.whatImported.body":
      "<p>Only alternative arts and uniques will be imported. Commons, rares and exalted cards will not be. Simply because they will be accessible with no quantity limit on every account.</p>",
    "section.terms.title": "What are the terms?",
    "section.terms.body":
      "<p>Keep a few important points in mind:</p>" +
      "<ul>" +
      "<li>We don't grant you ownership of the cards the way Equinox did. It's just a list of cards playable in certain defined formats, which may evolve in the future, nothing more.</li>" +
      "<li>You accept that your uniques may be used by others in certain formats (for example: a format without ownership, a format where each player has X uniques that change every week, etc.).</li>" +
      "</ul>" +
      '<p>Exact terms: <a href="/legal/reunion-digital-collection-eula-v1.pdf" target="_blank" rel="noopener" download="ReUnion Digital Collection EULA v1.pdf">download the full terms (PDF)</a>.</p>',
    "import.title": "Import my collection",
    "import.description":
      "Import the <code>altered-personal-data-export.zip</code> file.",
    "import.loginRegister": "Log in / Sign up",
    "import.termsAccept": "I accept the terms above.",
    "import.button": "Import",
    "import.inProgress": "Import in progress…",
    "import.success": "Collection imported successfully.",
    "import.sessionExpired": "Session expired. Please log in again.",
    "import.error": "Error",
    "import.sendError": "Send failed",
    "history.title": "History",
    "history.loading": "Loading…",
    "history.empty": "No events yet.",
    "history.loadError": "Could not load your history.",
    "history.networkError": "Network error.",
    "history.received": "Cards received",
    "history.given": "Cards given",
    "boosters.title": "My boosters",
    "boosters.loading": "Loading…",
    "boosters.empty": "No boosters to open.",
    "boosters.loadError": "Could not load your boosters.",
    "boosters.openError": "Could not open this booster.",
    "boosters.networkError": "Network error.",
    "boosters.open": "Open",
    "boosters.previousBooster": "Previous booster",
    "boosters.nextBooster": "Next booster",
    "nav.collection": "Collection",
    "nav.boosters": "Boosters",
    "nav.history": "History",
    "nav.import": "Import",
    "collection.title": "My collection",
    "collection.loading": "Loading…",
    "collection.empty": "No cards match these filters.",
    "collection.loadError": "Could not load your collection.",
    "collection.networkError": "Network error.",
    "collection.searchPlaceholder": "Search by name…",
    "collection.advancedFilters": "Advanced filters",
    "collection.faction": "Faction",
    "collection.set": "Set",
    "collection.rarity": "Rarity",
    "collection.type": "Type",
    "collection.variation": "Variation",
    "collection.subtype": "Subtype",
    "collection.handCost": "Hand cost",
    "collection.reserveCost": "Reserve cost",
    "collection.forestPower": "Forest power",
    "collection.mountainPower": "Mountain power",
    "collection.oceanPower": "Ocean power",
    "collection.min": "Min",
    "collection.max": "Max",
    "collection.reset": "Reset filters",
    "rarity.COMMON": "Common",
    "rarity.RARE": "Rare",
    "rarity.UNIQUE": "Unique",
    "rarity.EXALTED": "Exalted",
    "cardType.HERO": "Hero",
    "cardType.CHARACTER": "Character",
    "cardType.SPELL": "Spell",
    "cardType.LANDMARK_PERMANENT": "Landmark Permanent",
    "cardType.EXPEDITION_PERMANENT": "Expedition Permanent",
    "cardType.TOKEN": "Token",
    "cardType.TOKEN_MANA": "Mana",
    "set.ALIZE": "Trial by Frost",
    "set.BISE": "Whispers from the Maze",
    "set.CORE": "Beyond the Gates",
    "set.COREKS": "Beyond the Gates (KS)",
    "set.CYCLONE": "Skybound Odyssey",
    "set.DUSTER": "Seeds of Unity",
    "set.EOLE": "Roots of Corruption",
    "set.FUGUE": "Neverending Journey",

    "admin.pageTitle": "Administration",
    "admin.accessDenied": "You must be signed in with an administrator account to access this page.",
    "admin.distributeTitle": "Distribute cards for an event",
    "admin.targetedPlayers": "Targeted players",
    "admin.searchPlaceholder": "Email or username",
    "admin.search": "Search",
    "admin.searchHint": "Email accepts a partial match, username must be exact.",
    "admin.bgaSearchLabel": "Or search by BGA username",
    "admin.bgaSearchPlaceholder": "BGA username",
    "admin.bgaSearchHint": "The BGA username must be exact and match an already played game.",
    "admin.bgaNotFound": "No known Reunion id for this BGA username.",
    "admin.manualIdsLabel": "Or paste Keycloak ids (one per line)",
    "admin.add": "Add",
    "admin.reward": "Reward",
    "admin.modeCard": "Specific card",
    "admin.modeBooster": "Booster",
    "admin.cardReferenceLabel": "Card reference(s)",
    "admin.cardReferenceHint": "Several references allowed, separated by spaces, commas or semicolons.",
    "admin.quantityPerPlayer": "Quantity (per player)",
    "admin.boosterTypeLabel": "Booster type",
    "admin.acquiredFromLabel": "Source (event name)",
    "admin.giveReward": "Give the reward",
    "admin.administrators": "Administrators",
    "admin.currentAdmins": "Current administrators",
    "admin.promotePlayer": "Promote a player",
    "admin.manualIdLabel": "Or enter a Keycloak id directly",
    "admin.keycloakIdPlaceholder": "Keycloak id",
    "admin.promoteAdminBtn": "Promote to admin",
    "admin.searching": "Searching…",
    "admin.searchError": "Search failed.",
    "admin.noResults": "No results.",
    "admin.remove": "Remove",
    "admin.cardRefAndQtyRequired": "Reference and quantity required.",
    "admin.boosterTypeAndQtyRequired": "Booster type and quantity required.",
    "admin.selectAtLeastOnePlayer": "Select at least one player.",
    "admin.addAtLeastOneItem": "Add at least one card or booster.",
    "admin.sending": "Sending…",
    "admin.rewardDistributed": "Reward given to every targeted player.",
    "admin.error": "Error",
    "admin.networkError": "Network error",
    "admin.loading": "Loading…",
    "admin.loadError": "Loading failed.",
    "admin.updating": "Updating…",
    "admin.confirmDemote": "Remove admin rights from {name}?",
    "admin.demoted": "{name} is no longer an admin.",
    "admin.confirmPromote": "Give admin rights to {name}?",
    "admin.promoted": "{name} is now an admin.",
  },
  fr: {
    "nav.back": "Retour sur altered.re",
    "nav.backHome": "Retour",
    "auth.login": "Connexion",
    "auth.editProfile": "Modifier mon profil",
    "auth.admin": "Admin",
    "auth.logout": "Déconnexion",
    "main.title": "Importer ma collection",
    "section.export.title": "Comment exporter ma collection actuelle ?",
    "section.export.body":
      '<p>Dans le cadre du RGPD, vous pouvez demander l\'accès à vos données personnelles à Equinox en envoyant un mail à <a href="mailto:support@altered.fr">support@altered.fr</a> :</p>',
    "section.whatImported.title": "Qu'est-ce qui sera importé ?",
    "section.whatImported.body":
      "<p>Uniquement les arts alternatifs et les uniques seront importés. Les communes, les rares et les exaltées ne seront pas récupérées, pour la simple raison qu'elles seront accessibles sans limite de quantité sur tous les comptes.</p>",
    "section.terms.title": "Quelles sont les conditions ?",
    "section.terms.body":
      "<p>Gardez à l'esprit quelques points importants :</p>" +
      "<ul>" +
      "<li>On ne vous donne pas la propriété des cartes comme Equinox a pu le faire. C'est juste une liste de cartes jouables dans certains formats définis, qui peuvent évoluer dans le futur, rien de plus.</li>" +
      "<li>Vous acceptez que vos uniques soient utilisées par d'autres dans certains formats (par exemple : un format sans propriété, un format où chaque joueur aurait X uniques qui changent chaque semaine, etc.).</li>" +
      "</ul>" +
      '<p>Conditions exactes : <a href="/legal/reunion-digital-collection-eula-v1.pdf" target="_blank" rel="noopener" download="ReUnion Digital Collection EULA v1.pdf">télécharger les conditions complètes (PDF)</a>.</p>',
    "import.title": "Importer ma collection",
    "import.description":
      "Importez le fichier <code>altered-personal-data-export.zip</code>.",
    "import.loginRegister": "Me connecter / M'inscrire",
    "import.termsAccept": "J'accepte les conditions ci-dessus.",
    "import.button": "Importer",
    "import.inProgress": "Import en cours…",
    "import.success": "Collection importée avec succès.",
    "import.sessionExpired": "Session expirée. Veuillez vous reconnecter.",
    "import.error": "Erreur",
    "import.sendError": "Échec de l’envoi",
    "history.title": "Historique",
    "history.loading": "Chargement…",
    "history.empty": "Aucun événement pour le moment.",
    "history.loadError": "Impossible de charger l'historique.",
    "history.networkError": "Erreur réseau.",
    "history.received": "Cartes reçues",
    "history.given": "Cartes données",
    "boosters.title": "Mes boosters",
    "boosters.loading": "Chargement…",
    "boosters.empty": "Aucun booster à ouvrir.",
    "boosters.loadError": "Impossible de charger vos boosters.",
    "boosters.openError": "Impossible d'ouvrir ce booster.",
    "boosters.networkError": "Erreur réseau.",
    "boosters.open": "Ouvrir",
    "boosters.previousBooster": "Booster précédent",
    "boosters.nextBooster": "Booster suivant",
    "nav.collection": "Collection",
    "nav.boosters": "Boosters",
    "nav.history": "Historique",
    "nav.import": "Import",
    "collection.title": "Ma collection",
    "collection.loading": "Chargement…",
    "collection.empty": "Aucune carte ne correspond à ces filtres.",
    "collection.loadError": "Impossible de charger votre collection.",
    "collection.networkError": "Erreur réseau.",
    "collection.searchPlaceholder": "Rechercher par nom…",
    "collection.advancedFilters": "Filtres avancés",
    "collection.faction": "Faction",
    "collection.set": "Édition",
    "collection.rarity": "Rareté",
    "collection.type": "Type",
    "collection.variation": "Variante",
    "collection.subtype": "Sous-type",
    "collection.handCost": "Coût de main",
    "collection.reserveCost": "Coût de réserve",
    "collection.forestPower": "Puissance Forêt",
    "collection.mountainPower": "Puissance Montagne",
    "collection.oceanPower": "Puissance Océan",
    "collection.min": "Min",
    "collection.max": "Max",
    "collection.reset": "Réinitialiser les filtres",
    "rarity.COMMON": "Commune",
    "rarity.RARE": "Rare",
    "rarity.UNIQUE": "Unique",
    "rarity.EXALTED": "Exaltée",
    "cardType.HERO": "Héros",
    "cardType.CHARACTER": "Personnage",
    "cardType.SPELL": "Sort",
    "cardType.LANDMARK_PERMANENT": "Repère Permanent",
    "cardType.EXPEDITION_PERMANENT": "Permanent d'Expédition",
    "cardType.TOKEN": "Jeton",
    "cardType.TOKEN_MANA": "Mana",
    "set.ALIZE": "Épreuve du froid",
    "set.BISE": "Murmures du Labyrinthe",
    "set.CORE": "Au-delà des portes",
    "set.COREKS": "Au-delà des portes – KS",
    "set.CYCLONE": "Odyssée des cieux",
    "set.DUSTER": "Les Graines de l'Unité",
    "set.EOLE": "Les Racines de la Corruption",
    "set.FUGUE": "La Traversée Éternelle",

    "admin.pageTitle": "Administration",
    "admin.accessDenied": "Vous devez être connecté avec un compte administrateur pour accéder à cette page.",
    "admin.distributeTitle": "Distribuer des cartes pour un événement",
    "admin.targetedPlayers": "Joueurs ciblés",
    "admin.searchPlaceholder": "Email ou pseudo",
    "admin.search": "Rechercher",
    "admin.searchHint": "L'email accepte une recherche partielle, le pseudo doit être exact.",
    "admin.bgaSearchLabel": "Ou rechercher par pseudo BGA",
    "admin.bgaSearchPlaceholder": "Pseudo BGA",
    "admin.bgaSearchHint": "Le pseudo BGA doit être exact et correspondre à une partie déjà jouée.",
    "admin.bgaNotFound": "Aucun identifiant Reunion connu pour ce pseudo BGA.",
    "admin.manualIdsLabel": "Ou coller des identifiants Keycloak (un par ligne)",
    "admin.add": "Ajouter",
    "admin.reward": "Récompense",
    "admin.modeCard": "Carte spécifique",
    "admin.modeBooster": "Booster",
    "admin.cardReferenceLabel": "Référence(s) de la carte",
    "admin.cardReferenceHint": "Plusieurs références possibles, séparées par espace, virgule ou point-virgule.",
    "admin.quantityPerPlayer": "Quantité (par joueur)",
    "admin.boosterTypeLabel": "Type de booster",
    "admin.acquiredFromLabel": "Origine (nom de l'événement)",
    "admin.giveReward": "Donner la récompense",
    "admin.administrators": "Administrateurs",
    "admin.currentAdmins": "Administrateurs actuels",
    "admin.promotePlayer": "Promouvoir un joueur",
    "admin.manualIdLabel": "Ou saisir un identifiant Keycloak directement",
    "admin.keycloakIdPlaceholder": "id Keycloak",
    "admin.promoteAdminBtn": "Promouvoir admin",
    "admin.searching": "Recherche…",
    "admin.searchError": "Erreur de recherche.",
    "admin.noResults": "Aucun résultat.",
    "admin.remove": "Retirer",
    "admin.cardRefAndQtyRequired": "Référence et quantité requises.",
    "admin.boosterTypeAndQtyRequired": "Type de booster et quantité requis.",
    "admin.selectAtLeastOnePlayer": "Sélectionnez au moins un joueur.",
    "admin.addAtLeastOneItem": "Ajoutez au moins une carte ou un booster.",
    "admin.sending": "Envoi en cours…",
    "admin.rewardDistributed": "Récompense distribuée à tous les joueurs ciblés.",
    "admin.error": "Erreur",
    "admin.networkError": "Erreur réseau",
    "admin.loading": "Chargement…",
    "admin.loadError": "Erreur de chargement.",
    "admin.updating": "Mise à jour…",
    "admin.confirmDemote": "Retirer les droits admin de {name} ?",
    "admin.demoted": "{name} n'est plus admin.",
    "admin.confirmPromote": "Donner les droits admin à {name} ?",
    "admin.promoted": "{name} est maintenant admin.",
  },
  es: {},
  it: {},
  de: {},
};

// Shared engine: lookup/apply (t/applyI18n), language state (setLang/initLang), and a
// small flag-button control -- built once here so both app.js (public site) and
// admin.js (admin panel) drive the same dictionary instead of each keeping its own
// copy. A manual choice (the flag button) is persisted in localStorage and always
// wins over any auto-detected fallback (e.g. the Keycloak account locale) passed to
// initLang, so switching the flag sticks across reloads and across the Keycloak
// locale claim.
(() => {
  const SUPPORTED_LANGS = ['en', 'fr'];
  const DEFAULT_LANG = 'en';
  const STORAGE_KEY = 'ao_lang';
  const FLAGS = { en: '🇬🇧', fr: '🇫🇷' };
  const FLAG_TITLES = { en: 'Passer en français', fr: 'Switch to English' };
  const html = document.documentElement;

  let currentLang = DEFAULT_LANG;
  const listeners = new Set();

  const t = (key) => {
    const dict = window.AO_I18N || {};
    return (dict[currentLang] && dict[currentLang][key])
      || (dict[DEFAULT_LANG] && dict[DEFAULT_LANG][key])
      || key;
  };

  const applyI18n = () => {
    document.querySelectorAll('[data-i18n]').forEach((el) => {
      el.textContent = t(el.dataset.i18n);
    });
    document.querySelectorAll('[data-i18n-html]').forEach((el) => {
      el.innerHTML = t(el.dataset.i18nHtml);
    });
    document.querySelectorAll('[data-i18n-title]').forEach((el) => {
      el.title = t(el.dataset.i18nTitle);
    });
    document.querySelectorAll('[data-i18n-aria-label]').forEach((el) => {
      el.setAttribute('aria-label', t(el.dataset.i18nAriaLabel));
    });
    document.querySelectorAll('[data-i18n-placeholder]').forEach((el) => {
      el.setAttribute('placeholder', t(el.dataset.i18nPlaceholder));
    });
  };

  const renderLangControl = () => {
    const el = document.getElementById('ao-lang-control');
    if (!el) return;
    el.innerHTML =
      '<button type="button" class="btn btn-sm btn-outline-secondary ao-lang-btn" title="' +
      FLAG_TITLES[currentLang] + '">' + FLAGS[currentLang] + '</button>';
    el.querySelector('button').addEventListener('click', () => {
      const next = currentLang === 'fr' ? 'en' : 'fr';
      try { localStorage.setItem(STORAGE_KEY, next); } catch { /* per-viewer convenience only */ }
      setLang(next);
    });
  };
  listeners.add(renderLangControl);

  const setLang = (lang) => {
    currentLang = SUPPORTED_LANGS.includes(lang) ? lang : DEFAULT_LANG;
    html.lang = currentLang;
    applyI18n();
    listeners.forEach((fn) => fn(currentLang));
  };

  // Maps a raw locale (e.g. "fr", "fr-FR", "de_DE") to a supported UI language, or null.
  const normalizeLang = (raw) => {
    if (!raw) return null;
    const base = String(raw).toLowerCase().split(/[-_]/)[0];
    return SUPPORTED_LANGS.includes(base) ? base : null;
  };

  const getStoredLang = () => {
    try { return localStorage.getItem(STORAGE_KEY); } catch { return null; }
  };

  // Call once at page load with an optional fallback locale (e.g. the Keycloak account
  // locale) -- a previously chosen flag click always takes priority over it.
  const initLang = (fallback) => {
    setLang(getStoredLang() || normalizeLang(fallback) || DEFAULT_LANG);
  };

  window.AoI18n = {
    t,
    applyI18n,
    setLang,
    initLang,
    normalizeLang,
    onLangChange: (fn) => listeners.add(fn),
    get currentLang() { return currentLang; },
  };
})();
