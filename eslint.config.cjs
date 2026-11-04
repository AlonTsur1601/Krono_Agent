const parser = require('@typescript-eslint/parser')
const typescript = require('@typescript-eslint/eslint-plugin')
module.exports = [
  { ignores: ['dist/**', 'node_modules/**', 'release/**'] },
  {
    files: ['src/**/*.ts'],
    languageOptions: { parser, ecmaVersion: 'latest', sourceType: 'module' },
    plugins: { '@typescript-eslint': typescript },
    rules: {
      ...typescript.configs.recommended.rules,
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
      '@typescript-eslint/no-explicit-any': 'warn',
    },
  },
]
