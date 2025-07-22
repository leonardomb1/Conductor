// src/Frontend/src/lib/stores/theme.svelte.ts
interface ThemeState {
  isDark: boolean;
  isMobileMenuOpen: boolean;
}

class ThemeStore {
  private state = $state<ThemeState>({
    isDark: false,
    isMobileMenuOpen: false
  });

  constructor() {
    // Load theme preference from localStorage on initialization
    if (typeof window !== 'undefined') {
      const savedTheme = localStorage.getItem('conductor_theme');
      const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
      
      if (savedTheme === 'dark' || (!savedTheme && prefersDark)) {
        this.state.isDark = true;
        this.applyTheme();
      }

      // Listen for storage changes (e.g., from other tabs)
      window.addEventListener('storage', (e) => {
        if (e.key === 'conductor_theme') {
          const newTheme = e.newValue;
          if (newTheme === 'dark' || newTheme === 'light') {
            this.state.isDark = newTheme === 'dark';
            this.applyTheme();
          }
        }
      });

      // Listen for system theme changes
      const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
      mediaQuery.addEventListener('change', (e) => {
        // Only apply system theme if no user preference is saved
        const savedTheme = localStorage.getItem('conductor_theme');
        if (!savedTheme) {
          this.state.isDark = e.matches;
          this.applyTheme();
        }
      });
    }
  }

  get isDark() {
    return this.state.isDark;
  }

  get isMobileMenuOpen() {
    return this.state.isMobileMenuOpen;
  }

  toggleTheme() {
    this.state.isDark = !this.state.isDark;
    this.applyTheme();
    
    if (typeof window !== 'undefined') {
      localStorage.setItem('conductor_theme', this.state.isDark ? 'dark' : 'light');
    }
  }

  setDark(isDark: boolean) {
    this.state.isDark = isDark;
    this.applyTheme();
    
    if (typeof window !== 'undefined') {
      localStorage.setItem('conductor_theme', isDark ? 'dark' : 'light');
    }
  }

  toggleMobileMenu() {
    this.state.isMobileMenuOpen = !this.state.isMobileMenuOpen;
  }

  closeMobileMenu() {
    this.state.isMobileMenuOpen = false;
  }

  // Public method to ensure theme is applied - useful after navigation
  applyTheme() {
    if (typeof window !== 'undefined') {
      const html = document.documentElement;
      if (this.state.isDark) {
        html.classList.add('dark');
      } else {
        html.classList.remove('dark');
      }
    }
  }

  // Force theme reapplication - useful for ensuring consistency after navigation
  forceApplyTheme() {
    // Re-read from localStorage to ensure consistency
    if (typeof window !== 'undefined') {
      const savedTheme = localStorage.getItem('conductor_theme');
      const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
      
      const shouldBeDark = savedTheme === 'dark' || (!savedTheme && prefersDark);
      
      if (this.state.isDark !== shouldBeDark) {
        this.state.isDark = shouldBeDark;
      }
      
      this.applyTheme();
    }
  }
}

export const themeStore = new ThemeStore();