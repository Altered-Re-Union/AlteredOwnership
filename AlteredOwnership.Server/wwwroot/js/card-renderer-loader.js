(() => {
    const url = window.AppConfig && window.AppConfig.cardRendererScriptUrl;
    if (!url) return;
    const script = document.createElement('script');
    script.src = url;
    script.async = false;
    document.currentScript.replaceWith(script);
})();
