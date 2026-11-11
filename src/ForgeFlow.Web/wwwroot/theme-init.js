(() => {
    const STORAGE_KEY = 'forgeflow.theme';
    const mediaQuery = typeof window.matchMedia === 'function'
        ? window.matchMedia('(prefers-color-scheme: dark)')
        : null;

    let currentMode = 'light';
    try {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (stored === 'light' || stored === 'dark' || stored === 'system') {
            currentMode = stored;
        }
    } catch { }

    function resolveTheme(mode) {
        if (mode === 'dark') return 'dark';
        if (mode === 'system') return mediaQuery?.matches ? 'dark' : 'light';
        return 'light';
    }

    function applyResolved(mode) {
        const resolved = resolveTheme(mode);
        document.documentElement.dataset.theme = resolved;
        document.documentElement.dataset.themeMode = mode;
        document.documentElement.style.colorScheme = resolved;
        return resolved;
    }

    applyResolved(currentMode);

    if (mediaQuery) {
        const onChange = () => {
            if (currentMode === 'system') {
                applyResolved('system');
            }
        };
        if (typeof mediaQuery.addEventListener === 'function') {
            mediaQuery.addEventListener('change', onChange);
        } else if (typeof mediaQuery.addListener === 'function') {
            mediaQuery.addListener(onChange);
        }
    }

    window.__forgeflowTheme = {
        getMode() {
            return currentMode;
        },
        apply(mode) {
            currentMode = mode === 'dark' || mode === 'system' ? mode : 'light';
            try {
                localStorage.setItem(STORAGE_KEY, currentMode);
            } catch { }
            return applyResolved(currentMode);
        },
        preview(mode) {
            const candidate = mode === 'dark' || mode === 'system' ? mode : 'light';
            return applyResolved(candidate);
        },
        restore() {
            return applyResolved(currentMode);
        }
    };
})();
