/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/renderer/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        dark: {
          bg: '#111316',
          sidebar: '#15181D',
          surface: '#1B2027',
          border: '#2B323C',
          text: '#F1F3F5',
          muted: '#A5ADB8',
          accent: '#8FD7C7',
          'accent-hover': '#A0E5D5',
        },
        light: {
          bg: '#F6F7F9',
          surface: '#FFFFFF',
          hover: '#EBEDF0',
          border: '#D1D5DB',
          text: '#171B22',
          muted: '#6B7280',
          accent: '#237D70',
          'accent-hover': '#2A9685',
        },
        halloween: {
          bg: '#15101D',
          surface: '#21182C',
          hover: '#2C1F3A',
          border: '#3D2F4F',
          text: '#F7F0E7',
          muted: '#C4B8A8',
          pumpkin: '#FF9B50',
          violet: '#B89AFF',
        },
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', 'sans-serif'],
        mono: ['JetBrains Mono', 'Consolas', 'monospace'],
      },
      fontSize: {
        'xs': '0.75rem',      // 12px
        'sm': '0.8125rem',    // 13px
        'base': '0.9375rem',  // 15px
        'lg': '1.0625rem',    // 17px
        'xl': '1.25rem',      // 20px
        '2xl': '1.5rem',      // 24px
      },
      spacing: {
        '1': '0.5rem',   // 8px
        '2': '1rem',     // 16px
        '3': '1.5rem',   // 24px
        '4': '2rem',     // 32px
        '6': '3rem',     // 48px
        '8': '4rem',     // 64px
      },
      borderRadius: {
        'sm': '0.375rem',  // 6px
        'md': '0.75rem',   // 12px
        'lg': '1rem',      // 16px
        'xl': '1.5rem',    // 24px
      },
      transitionDuration: {
        'fast': '140ms',
        'base': '180ms',
        'slow': '240ms',
      },
      transitionTimingFunction: {
        'smooth': 'cubic-bezier(0.4, 0, 0.2, 1)',
      },
    },
  },
  plugins: [],
}
