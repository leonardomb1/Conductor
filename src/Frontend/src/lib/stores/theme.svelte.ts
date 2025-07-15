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

  private applyTheme() {
    if (typeof window !== 'undefined') {
      const html = document.documentElement;
      if (this.state.isDark) {
        html.classList.add('dark');
      } else {
        html.classList.remove('dark');
      }
    }
  }
}

export const themeStore = new ThemeStore();